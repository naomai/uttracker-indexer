Imports System.Text.RegularExpressions
Imports System.IO
Imports Microsoft.Extensions.FileProviders
Imports System.Reflection
Imports System.Net.Http

Public Class MasterServerManager
    Public cacheFile As String
    Public gslistFile As String

    Public Event OnMasterServerQuery(serverInfo As MasterServerInfo)
    Public Event OnMasterServerQueryListReceived(serverInfo As MasterServerInfo, serverList As List(Of String))
    Public Event OnMasterServerQueryFailure(serverInfo As MasterServerInfo, thrownException As Exception)
    Public Event OnMasterServerManagerRequest(masterServers As List(Of MasterServerInfo))
    Public Event OnMasterServerManagerRequestComplete(serverList As List(Of String))
    Public Event OnMasterServerPing(serverInfo As MasterServerInfo, online As Boolean)

    Dim masterServers As New List(Of MasterServerInfo)
    Public Shared gamespyKeys As Dictionary(Of String, GamespyGameInfo)
    Dim serverList As List(Of String)

    Public Sub New(cacheFile As String)
        Me.cacheFile = cacheFile
        MasterServerManager.gamespyKeys = staticLoadGSList()
    End Sub

    Public Sub addMasterServer(configString As String)
        Dim regexMatches As MatchCollection, masterServersNewItem As MasterServerInfo
        Static serverNum As Integer = 0
        regexMatches = Regex.Matches(configString, ",([^=]+)=([^,]*)")
        masterServersNewItem = Nothing
        masterServersNewItem.iniVariables = New Hashtable
        masterServersNewItem.serverClassName = Regex.Match(configString, "^([^\.]*\.[^\,]*),").Groups(1).Value
        With masterServersNewItem
            For Each serverMatch In regexMatches
                .iniVariables(serverMatch.Groups(1).Value) = serverMatch.Groups(2).Value
                Select Case serverMatch.Groups(1).Value
                    Case "MasterServerAddress"
                        .serverIp = serverMatch.Groups(2).Value
                    Case "MasterServerTCPPort"
                        .serverPort = serverMatch.Groups(2).Value
                        'Case "Region"
                        '    .region = serverMatch.Groups(2).Value
                        'Case "GameName"
                        '    .gameName = serverMatch.Groups(2).Value
                        '    .gameInfo = gamespyKeys(.gameName)
                End Select
            Next
            .serverId = serverNum
            serverNum += 1
        End With

        masterServers.Add(masterServersNewItem)
    End Sub

    Public Sub refreshServerList()
        Dim rawList As String = "", e As Exception
        Dim tempList As New List(Of String)
        Dim fact As MasterListFactory

        serverList = New List(Of String)

        RaiseEvent OnMasterServerManagerRequest(masterServers)

        For Each masterServer As MasterServerInfo In masterServers
            If masterServer.serverIp = Nothing Then Continue For
            Try
                RaiseEvent OnMasterServerQuery(masterServer)

                fact = MasterListFactory.createFactoryForMasterServer(masterServer)
                tempList = fact.query()

                RaiseEvent OnMasterServerQueryListReceived(masterServer, tempList)
                For Each server As String In tempList
                    If InStr(server, ":") = 0 Then
                        Continue For
                    End If
                    serverList.Add(server)
                Next
            Catch e
                RaiseEvent OnMasterServerQueryFailure(masterServer, e)
            End Try
        Next

        serverList = serverList.Distinct().ToList
        RaiseEvent OnMasterServerManagerRequestComplete(serverList)
    End Sub

    Public Sub pingMasterServers()
        For Each master In masterServers
            Dim fact = MasterListFactory.createFactoryForMasterServer(master)
            Dim result As Boolean = fact.ping()
            RaiseEvent OnMasterServerPing(master, result)
        Next
    End Sub

    Public Function getList() As List(Of String)
        Dim listCopy(0 To Me.serverList.Count - 1) As String ' create a copy of the list object
        Me.serverList.CopyTo(listCopy)
        Return listCopy.ToList()
    End Function

    Public Property Count As Integer
        Get
            Return serverList.Count
        End Get
        Set(value As Integer)

        End Set
    End Property

    Private Shared Function staticLoadGSList() As Dictionary(Of String, GamespyGameInfo)
        Dim line As String, fn As StreamReader


        Dim gslistProvider = New EmbeddedFileProvider(Assembly.GetExecutingAssembly(), "Naomai.UTT.ScannerV2")
        Dim gslistFile = gslistProvider.GetFileInfo("gslist.cfg").CreateReadStream()

        fn = New StreamReader(gslistFile)

        Dim gsList As New Dictionary(Of String, GamespyGameInfo)
        Dim gsNewItem As GamespyGameInfo
        Do While fn.Peek() <> -1
            line = fn.ReadLine()
            With gsNewItem
                .gameFullName = Trim(Mid(line, 1, 54))
                If InStr(.gameFullName, "GSLISTVER") <> 0 Then
                    Continue Do
                End If
                .gameName = Trim(Mid(line, 55, 19))
                .encKey = Trim(Mid(line, 74, 6))
                If .encKey <> "" Then
                    gsList.Item(.gameName) = gsNewItem
                End If
            End With

        Loop
        Return gsList

    End Function
End Class

Public Structure MasterServerInfo
    Dim serverIp As String
    Dim serverPort As UInt16
    Dim serverClassName As String
    Dim serverListFactory As MasterListFactory
    Property serverAddress As String
        Get
            Return serverIp & ":" & serverPort
        End Get
        Set(value As String)
            Dim ipChunks = Split(value, ":")
            If ipChunks.Count = 1 Then
                serverIp = value
            ElseIf ipChunks.Count = 2 Then
                serverIp = ipChunks(1)
                serverPort = Integer.Parse(ipChunks(2))
            End If
        End Set
    End Property

    Public Overrides Function ToString() As String
        Return serverAddress
    End Function

    Friend iniVariables As Hashtable
    Dim serverId As Integer
End Structure


Public MustInherit Class MasterListFactory
    Protected server As MasterServerInfo
    Public Sub New(serverInfo As MasterServerInfo)
        server = serverInfo
    End Sub

    Public MustOverride Function query() As List(Of String)
    Public MustOverride Function ping() As Boolean

    Public Shared Function createFactoryForMasterServer(serverInfo As MasterServerInfo) As MasterListFactory
        Select Case serverInfo.serverClassName
            Case "UBrowser.UBrowserGSpyFact", "XBrowser.XBrowserFactInternet"
                Dim gameInfo As GamespyGameInfo = Nothing
                If serverInfo.iniVariables.ContainsKey("GameName") Then
                    gameInfo = MasterServerManager.gamespyKeys(serverInfo.iniVariables("GameName"))
                End If
                Return New MasterListGSpyFact(serverInfo, gameInfo)
            Case "UBrowser.UBrowserHTTPFact"
                Return New MasterListHTTPFact(serverInfo)
            Case Else
                Return Nothing
        End Select
    End Function
End Class

Class MasterListGSpyFact
    Inherits MasterListFactory
    Public gameInfo As GamespyGameInfo
    Protected region As Integer = 0


    Public Sub New(serverInfo As MasterServerInfo, gInfo As GamespyGameInfo)
        MyBase.New(serverInfo)
        gameInfo = gInfo
        If serverInfo.iniVariables.ContainsKey("Region") Then region = serverInfo.iniVariables("Region")
    End Sub

    Public Overrides Function ping() As Boolean
        Try
            Using connection = New JulkinNet
                connection.Connect(server.serverAddress)
                Dim packet = connection.ReadNext()

                Dim packetObj = New UTQueryPacket(packet, UTQueryPacket.UTQueryPacketFlags.UTQP_MasterServer)
                ping = packetObj.ContainsKey("basic") AndAlso packetObj.ContainsKey("secure")
                connection.Disconnect()
            End Using
        Catch ex As Exception
            Return False
        End Try
    End Function

    Public Overrides Function query() As System.Collections.Generic.List(Of String)
        Dim rawList As String = getRawList()
        Dim result = New List(Of String)

        Dim packet = New UTQueryPacket(rawList, UTQueryPacket.UTQueryPacketFlags.UTQP_MasterServerIpList)
        For Each ipEntry As UTQueryKeyValuePair In packet
            If ipEntry.key = "ip" Then
                result.Add(ipEntry.value)
            End If
        Next
        Return result
    End Function

    Protected Function getRawList()
        Dim lol As JulkinNet, chal As String, tx As Long, packet As String, challengeResponse As String, is333 As Boolean = False
        Dim serverResponse As UTQueryPacket ' As Hashtable
        lol = New JulkinNet
        lol.timeout = 2500
        Dim myResponse = New UTQueryPacket(UTQueryPacket.UTQueryPacketFlags.UTQP_MasterServer)

        lol.Connect(server.serverAddress)
        packet = lol.ReadNext()
        If Len(packet) = 0 Then Throw New Exception("No response from " & server.serverAddress)

        serverResponse = New UTQueryPacket(packet, UTQueryPacket.UTQueryPacketFlags.UTQP_MasterServer Or UTQueryPacket.UTQueryPacketFlags.UTQP_NoFinal)

        If gameInfo.gameName <> "" Then
            myResponse.Add("gamename", gameInfo.gameName)
        End If
        myResponse.Add("location", region)
        If serverResponse("secure") <> "" AndAlso serverResponse("secure") <> "wookie" Then ' challenge!
            chal = serverResponse("secure")
            challengeResponse = gsenc(chal, gameInfo.encKey)
            myResponse.Add("validate", challengeResponse)
        End If



        myResponse.Add("list", "")
        lol.Write(myResponse)

        getRawList = ""
        tx = TickCount()
        Do
            getRawList &= lol.ReadNext()
        Loop While TickCount() - tx < 7000 AndAlso InStr(getRawList, "\final\") = 0
        lol.Disconnect()
        If Len(getRawList) < 5 Then
            Throw New Exception("Master server query failed, no response to request")
        End If
        Dim mat = Regex.Match(getRawList, "\\echo\\([^\\]*)\\final\\")
        If mat.Groups.Count > 1 Then
            Throw New Exception("Master server query failed, server responded: " & mat.Groups(1).Value)
        End If
    End Function
End Class

Class MasterListHTTPFact
    Inherits MasterListFactory
    Dim requestURI As String
    Dim clientObj As New HttpClient()

    Public Sub New(serverInfo As MasterServerInfo)
        MyBase.New(serverInfo)
        requestURI = serverInfo.iniVariables("MasterServerURI")
    End Sub

    Public Overrides Function ping() As Boolean
        Dim requestUrl = "http://" & server.ToString & requestURI

        Dim requestMsg As New HttpRequestMessage(HttpMethod.Head, requestUrl)
        Dim requestTask = clientObj.SendAsync(requestMsg)
        requestTask.Wait()
        Dim requestResult = requestTask.Result

        Return (requestResult.StatusCode = Net.HttpStatusCode.OK)
    End Function

    Public Overrides Function query() As System.Collections.Generic.List(Of String)
        Dim serverList = New List(Of String)
        Dim requestUrl = "http://" & server.ToString & requestURI

        Dim requestTask = clientObj.GetAsync(requestUrl)
        requestTask.Wait()
        Dim requestResult = requestTask.Result

        Using respReader = New StreamReader(requestResult.Content.ReadAsStream())
            Do While Not respReader.EndOfStream
                Dim serverLine = respReader.ReadLine()
                Dim serverLineChunks() = serverLine.Split(" "c)
                If serverLineChunks.Count = 3 Then
                    serverList.Add(serverLineChunks(0) & ":" & serverLineChunks(2))
                End If
            Loop
        End Using
        Return serverList
    End Function
End Class

Public Structure GamespyGameInfo
    Dim gameFullName As String
    Dim gameName As String
    Dim encKey As String
End Structure
