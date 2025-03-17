Imports System.Diagnostics.Metrics
Imports System.Net
Imports System.Net.Sockets
Imports System.Text.Encoding
Imports System.Threading

Public Class GSMasterServer
    Friend listener As TcpListener
    Dim serverPort As Integer
    Dim isActive As Boolean = False, forceStop As Boolean = False
    Friend appServerList As IServerListProvider


    Dim connectedClients As New List(Of GSMasterServerConnection)

    Friend asyncThread As Thread

    Friend tickCount As Int64 = 0

    Friend gameSpyKeys As New Dictionary(Of String, GamespyGameInfo)

    Public Event ClientConnected(client As IPEndPoint)
    Public Event ClientDisconnected(client As IPEndPoint, reason As GSClosingReason, relatedException As Exception)
    Public Event ClientRequestedList(client As IPEndPoint)


    Public Sub New(Optional port As Integer = 28900)
        listener = New TcpListener(IPAddress.Any, port)
        serverPort = port
    End Sub

    Public Sub StartServer()
        If Not isActive Then
            listener.Start()
            isActive = True
            CreateThread()
        End If
    End Sub

    Public Sub StopServer()
        If isActive Then
            listener.Stop()
            forceStop = True
        End If
    End Sub
    Public Sub SetServerListProvider(obj As IServerListProvider)
        appServerList = obj
    End Sub


    Private Sub CreateThread()
        asyncThread = New Thread(AddressOf MasterServerLoop)
        asyncThread.Name = "GSMasterServer"
        asyncThread.Start()
    End Sub

    Public Sub StopThread()
        forceStop = True
    End Sub

    Public Sub LoadGSListFromDict(keys As Dictionary(Of String, GamespyGameInfo))
        gameSpyKeys = keys
    End Sub

    Private Sub MasterServerLoop()
        Do
            Tick()
            Thread.Sleep(15)
        Loop While Not forceStop
    End Sub

    Public Sub Tick()
        Dim workerConnection As Socket, worker As GSMasterServerConnection
        tickCount += 1

        If listener.Pending() Then
            workerConnection = listener.AcceptSocket
            Try
                worker = New GSMasterServerConnection(workerConnection, Me)
                connectedClients.Add(worker)
                OnClientConnected(workerConnection.RemoteEndPoint)
            Catch e As Exception When Not workerConnection.Connected Or e.Source = "MySql.Data"

            End Try
        End If


        connectedClients.RemoveAll(Function(client)
                                       Return Not client.active
                                   End Function)
        For Each client As GSMasterServerConnection In connectedClients
            client.Tick()
        Next

    End Sub

#Region "Receive events from GSMasterServerConnection objects"
    Friend Sub OnClientConnected(client As IPEndPoint)
        RaiseEvent ClientConnected(client)
    End Sub
    Friend Sub OnClientDisconnect(client As IPEndPoint, reason As GSClosingReason, Optional relatedException As Exception = Nothing)
        RaiseEvent ClientDisconnected(client, reason, relatedException)
    End Sub
    Friend Sub OnClientRequestedList(client As IPEndPoint)
        RaiseEvent ClientRequestedList(client)
    End Sub
#End Region
End Class

Public Class GSMasterServerConnection
    Dim conn As Socket

    Dim challengeString As String

    Dim incomingPacket As String = ""
    Dim gameName As String

    Dim lastSentPacketTime, lastPollTime As Date
    Dim requestDisconnect As Boolean = False

    Friend WithEvents masterServer As GSMasterServer
    Dim remote As IPEndPoint

    Dim state As GSMasterServerConnectionState

    Public Property active As Boolean
        Get
            Return Not IsNothing(conn) AndAlso conn.Connected
        End Get
        Set(value As Boolean)
            If Not value Then
                conn.Disconnect(False)
            End If
        End Set
    End Property

    Public Sub New(connection As Socket, master As GSMasterServer)
        masterServer = master
        conn = connection
        remote = connection.RemoteEndPoint
        challengeString = GenerateChallenge()
        SendChallenge()
    End Sub

    Public Sub Tick()
        Dim bytes As Integer
        Dim dataBuffer(1000) As Byte
        Dim packet As UTQueryPacket
        If conn.Available AndAlso active Then
            bytes = conn.Receive(dataBuffer)
            ReDim Preserve dataBuffer(bytes - 1)
            incomingPacket &= ASCII.GetString(dataBuffer)
            Try
                packet = New UTQueryPacket(incomingPacket, UTQueryPacket.UTQueryPacketFlags.UTQP_NoQueryId Or UTQueryPacket.UTQueryPacketFlags.UTQP_MasterServer)
                incomingPacket = ""
                PacketReceived(packet)
            Catch e As UTQueryInvalidResponseException
                PacketSend("\error\Malformed response\final\")
                masterServer.OnClientDisconnect(remote, GSClosingReason.InvalidPacket, e)
                conn.Close()
            Catch e As UTQueryResponseIncompleteException

            Catch e As Exception
                PacketSend("\error\Internal server error\final\")
                masterServer.OnClientDisconnect(remote, GSClosingReason.InternalError, e)
                conn.Close()
            End Try

        End If
        If (Date.UtcNow - lastSentPacketTime).TotalSeconds > 8 AndAlso active Then
            Try
                PacketSend("\error\EmoTimeout. Bye\final\")
                masterServer.OnClientDisconnect(remote, GSClosingReason.Timeout)
                If conn.Connected Then
                    conn.Disconnect(False)
                End If
            Catch ex As Exception
                conn.Dispose()
                conn = Nothing
            End Try

        End If

        If (Date.UtcNow - lastPollTime).TotalSeconds > 5 AndAlso active Then ' detect dropped connections
            PacketSend("")
        End If
    End Sub

    Private Sub PacketReceived(packet As Hashtable)
        With state
            Dim responsePacket As New UTQueryPacket(UTQueryPacket.UTQueryPacketFlags.UTQP_MasterServer)
            Try
                If .expectingChallenge AndAlso packet.ContainsKey("validate") Then ReceivedChallenge(packet)
                If packet.ContainsKey("about") Then ReceivedAbout(packet, responsePacket)
                If packet.ContainsKey("echo") Then ReceivedEcho(packet, responsePacket)
                If packet.ContainsKey("list") Then
                    If .hasChallenge Then
                        ReceivedListRequest(packet, responsePacket)
                    Else
                        responsePacket.Add("error", "You need to verify yourself before sending the list request (use GSMSALG).")
                        responsePacket.SetReadyToSend()
                        requestDisconnect = True
                    End If
                End If
                If responsePacket.packetFlags.HasFlag(UTQueryPacket.UTQueryPacketFlags.UTQP_ReadyToSend) Then
                    PacketSend(responsePacket)
                End If
                If requestDisconnect Then
                    masterServer.OnClientDisconnect(remote, GSClosingReason.Success)
                    conn.Shutdown(SocketShutdown.Both)
                    conn.Close()
                End If
            Catch e As GSMSValidationFailedException
                responsePacket.Add("error", "Sorry, your verification string is not valid.")
                PacketSend(responsePacket)
                masterServer.OnClientDisconnect(remote, GSClosingReason.InvalidChallenge, e)
                conn.Close()
            Catch e As GSMSConnectionException
                responsePacket.Add("error", e.Message)
                PacketSend(responsePacket)
                If Not .hasChallenge Then
                    masterServer.OnClientDisconnect(remote, GSClosingReason.InvalidPacket, e)
                    conn.Close()
                End If
            End Try

        End With
    End Sub

    Private Sub SendChallenge()
        Dim challengePacket As New UTQueryPacket(UTQueryPacket.UTQueryPacketFlags.UTQP_MasterServer)
        challengePacket.Add("basic", "")
        challengePacket.Add("secure", challengeString)
        PacketSend(challengePacket)
        state.expectingChallenge = True
    End Sub

    Private Sub ReceivedChallenge(packet As Hashtable)
        Dim expectedResponse As String, encryptionKey As String

        If Not packet.ContainsKey("gamename") OrElse Not packet.ContainsKey("validate") Then Return 'Throw New GSMSConnectionException("Verification packet must contain both 'validate' and 'gamename' fields.")
        gameName = packet("gamename")

        If Not masterServer.gameSpyKeys.ContainsKey(gameName) Then
            Throw New GSMSConnectionException("This master server doesn't support the game '" & gameName & "'")
        End If
        encryptionKey = masterServer.gameSpyKeys(gameName).encKey
        expectedResponse = GameSpyProtocol.GenerateValidateResponse(challengeString, encryptionKey)

        If packet("validate") = expectedResponse OrElse packet.ContainsKey("debug") Then
            state.hasChallenge = True
            state.expectingChallenge = False
            state.expectingRequest = True
        Else
            Throw New GSMSValidationFailedException(expectedResponse, packet("validate"))
        End If
    End Sub

    Private Sub ReceivedListRequest(packet As Hashtable, ByRef destinationPacket As UTQueryPacket)
        masterServer.OnClientRequestedList(remote)
        Dim serverList = masterServer.appServerList.getServerListForGame(gameName)
        For Each server In serverList
            destinationPacket.Add("ip", server)
        Next
        requestDisconnect = True
        destinationPacket.SetReadyToSend()
    End Sub

    Private Sub ReceivedAbout(packet As Hashtable, ByRef destinationPacket As UTQueryPacket)
        Dim aboutText As String
        aboutText = "// UTTracker Master Server Module; // 2014 Namonaki14; URL: http://amaki.no-ip.eu/uttracker/master/; // Contact: tm.dvtb at gmail.com"
        destinationPacket.Add("about", aboutText)
        destinationPacket.SetReadyToSend()
    End Sub

    Private Sub ReceivedEcho(packet As Hashtable, ByRef destinationPacket As UTQueryPacket)
        destinationPacket.Add("echo_reply", packet("echo"))
        destinationPacket.SetReadyToSend()
    End Sub

    Private Sub PacketSend(packet As String)
        Try
            conn.Send(ASCII.GetBytes(packet))
            If packet <> "" Then lastSentPacketTime = Date.UtcNow
            lastPollTime = Date.UtcNow
            Thread.Sleep(5)
        Catch e As Exception
            masterServer.OnClientDisconnect(remote, GSClosingReason.InternalError, e)
            conn.Close()
        End Try
    End Sub

    Private Shared Function GenerateChallenge() As String
        Static allowedChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"
        Static allowedCharsLen = Len(allowedChars)
        Return allowedChars(Rand(1, allowedCharsLen)) & allowedChars(Rand(1, allowedCharsLen)) & allowedChars(Rand(1, allowedCharsLen)) &
            allowedChars(Rand(1, allowedCharsLen)) & allowedChars(Rand(1, allowedCharsLen)) & allowedChars(Rand(1, allowedCharsLen))
    End Function

    Private Shared Function Rand(min As UInt32, max As UInt32) As UInt32
        Static randomGen = New System.Random()
        Return randomGen.next(min, max)
    End Function

    Private Shared Function GsEscape(str As String)
        Return str.Replace("\", "_")
    End Function

    Private Structure GSMasterServerConnectionState
        Dim hasChallenge As Boolean

        Dim expectingChallenge As Boolean
        Dim expectingRequest As Boolean
    End Structure


End Class

Public Interface IServerListProvider
    Function getServerListForGame(gamename As String) As List(Of String)
    Function getAboutInfo() As Dictionary(Of String, String)
End Interface

Public Class GSMSConnectionException
    Inherits Exception
    Public lastPacket As String

    Public Sub New(description As String)
        MyBase.New(description)
    End Sub
    Public Sub New(description As String, lastPacket As String)
        MyBase.New(description)
        Me.lastPacket = lastPacket
    End Sub
End Class

Public Class GSMSValidationFailedException
    Inherits GSMSConnectionException
    Public expectedChallenge, receivedChallenge As String

    Public Sub New(expectedChallenge As String, receivedChallenge As String)
        MyBase.New("Failed verifying client (Expected response: " & expectedChallenge & ", received: " & receivedChallenge & ")")
    End Sub
End Class

Public Enum GSClosingReason
    Success
    Disconnected
    InvalidChallenge
    InvalidPacket
    Timeout
    InternalError
End Enum

