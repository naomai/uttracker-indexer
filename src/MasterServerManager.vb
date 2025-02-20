Imports System.Text.RegularExpressions
Imports System.IO
Imports Microsoft.Extensions.FileProviders
Imports System.Reflection
Imports System.Net.Http
Imports Naomai.UTT.Indexer.UTQueryPacket

Public Class MasterServerManager
    Public cacheFile As String
    Public gslistFile As String

    Dim masterServers As New List(Of MasterServerInfo)
    Public Shared gamespyKeys As Dictionary(Of String, GamespyGameInfo)

    Public log As Logger
    Public updateInterval As Integer = 3600
    Public pingInterval As Integer = 600

    Public Async Sub ThreadLoop()
        Do
            Refresh()
            Await Task.Delay(100)
        Loop
    End Sub

    Public Async Sub Refresh()
        Dim tasks As New List(Of Task)
        For Each masterServer As MasterServerInfo In masterServers
            If masterServer.ShouldRefresh() Then
                Dim t = masterServer.Refresh()
                tasks.Add(t)
            ElseIf masterServer.ShouldPing() Then
                Dim t = masterServer.Ping()
                tasks.Add(t)
            End If
        Next

        Await Task.WhenAll(tasks)
    End Sub

    Public Sub New()
        MasterServerManager.gamespyKeys = staticLoadGSList()
    End Sub

    Public Sub AddMasterServer(configString As String)
        Dim masterServer As New MasterServerInfo(configString)
        masterServer.updateInterval = updateInterval
        masterServer.pingInterval = pingInterval
        masterServer.manager = Me

        masterServers.Add(masterServer)
    End Sub


    Public Function GetList() As List(Of String)
        Dim servers = New List(Of String)
        For Each server In masterServers
            servers.AddRange(server.serverList)
        Next
        Return servers.Distinct().ToList()
    End Function

    Public Property Count As Integer
        Get
            Return GetList().Count
        End Get
        Set(value As Integer)

        End Set
    End Property

    Private Shared Function staticLoadGSList() As Dictionary(Of String, GamespyGameInfo)
        Dim line As String, fn As StreamReader


        Dim gslistProvider = New EmbeddedFileProvider(Assembly.GetExecutingAssembly(), "Naomai.UTT.Indexer")
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

    Friend Sub logWriteLine(ByVal format As String, ByVal ParamArray arg As Object())
        log.WriteLine("MasterServer: " & format, arg)
    End Sub
End Class

Public Class MasterServerInfo
    Public ReadOnly host As String
    Public ReadOnly port As UInt16
    Public ReadOnly unrealClassName As String
    Public ReadOnly serverList As New List(Of String)
    Protected isBusy As Boolean = False
    Protected lastUpdate As Date
    Protected lastPing As Date
    Protected retryDeadline As Date = Nothing
    Protected factory As MasterListFactory
    Friend iniVariables As Hashtable
    Friend serverId As Integer
    Friend updateInterval As Integer = 3600
    Friend pingInterval As Integer = 600
    Friend manager As MasterServerManager

    ReadOnly Property address As String
        Get
            Return host & ":" & port
        End Get
    End Property

    Public Sub New(iniConfigString As String)
        Static idCounter As Integer = 0
        Dim regexMatches As MatchCollection
        regexMatches = Regex.Matches(iniConfigString, ",([^=]+)=([^,]*)")
        iniVariables = New Hashtable
        unrealClassName = Regex.Match(iniConfigString, "^([^\.]*\.[^\,]*),").Groups(1).Value
        With Me
            For Each serverMatch In regexMatches
                .iniVariables(serverMatch.Groups(1).Value) = serverMatch.Groups(2).Value
                Select Case serverMatch.Groups(1).Value
                    Case "MasterServerAddress"
                        host = serverMatch.Groups(2).Value
                    Case "MasterServerTCPPort"
                        port = serverMatch.Groups(2).Value
                        'Case "Region"
                        '    .region = serverMatch.Groups(2).Value
                        'Case "GameName"
                        '    .gameName = serverMatch.Groups(2).Value
                        '    .gameInfo = gamespyKeys(.gameName)
                End Select
            Next
            serverId = idCounter
            idCounter += 1
        End With

        factory = MasterListFactory.createFactoryForMasterServer(Me)
    End Sub


    Public Async Function Refresh() As Task
        isBusy = True
        If address = Nothing Then
            Return
        End If
        Try
            Log("Querying...")
            Dim tempList = Await factory.query()

            SyncLock serverList
                serverList.Clear()

                serverList.AddRange(
                    tempList.Where(Function(s) s.Contains(":")).Distinct()
                )

            End SyncLock
            Log("Received {0} servers", serverList.Count)
            lastUpdate = Date.UtcNow
            lastPing = Date.UtcNow
        Catch e As Exception
            Log("Failure: ", e.Message)
            QueryFail()
        Finally
            isBusy = False
        End Try
    End Function

    Public Async Function Ping() As Task
        isBusy = True
        Log("Pinging...")
        Dim result As Boolean = Await factory.ping()
        If Not result Then
            QueryFail()
        End If
        lastPing = Date.UtcNow
        isBusy = False
    End Function

    Public Function ShouldRefresh()
        Return (Date.UtcNow - lastUpdate).TotalSeconds > updateInterval _
            AndAlso Not ShouldHold()
    End Function

    Public Function ShouldPing()
        Return (Date.UtcNow - lastPing).TotalSeconds > pingInterval _
            AndAlso Not ShouldHold()
    End Function

    Public Function ShouldHold()
        Return (Not IsNothing(retryDeadline) _
            AndAlso (retryDeadline - Date.UtcNow).TotalSeconds > 0) _
            OrElse isBusy
    End Function

    Protected Sub QueryFail()
        retryDeadline = Date.UtcNow.AddMinutes(10)
        Log("Query failed")
    End Sub

    Protected Sub Log(ByVal format As String, ByVal ParamArray arg As Object())
        manager.log.DebugWriteLine("MasterServer[" + address + "|" + iniVariables("GameName") + "]: " & format, arg)
    End Sub

    Public Overrides Function ToString() As String
        Return address
    End Function

End Class


Public MustInherit Class MasterListFactory
    Protected server As MasterServerInfo
    Public Sub New(serverInfo As MasterServerInfo)
        server = serverInfo
    End Sub

    Public MustOverride Async Function query() As Task(Of List(Of String))
    Public MustOverride Async Function ping() As Task(Of Boolean)

    Public Shared Function createFactoryForMasterServer(serverInfo As MasterServerInfo) As MasterListFactory
        Select Case serverInfo.unrealClassName
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
        If serverInfo.iniVariables.ContainsKey("Region") Then
            region = serverInfo.iniVariables("Region")
        End If
    End Sub

    Public Overrides Async Function ping() As Task(Of Boolean)
        Dim pingResult As Boolean
        Try
            Using connection = New JulkinNet
                connection.Connect(server.address)
                Dim packet = Await connection.ReadNextAsync()

                Dim packetObj = New UTQueryPacket(packet, UTQueryPacket.UTQueryPacketFlags.UTQP_MasterServer)
                pingResult = packetObj.ContainsKey("basic") AndAlso packetObj.ContainsKey("secure")
                connection.Disconnect()
            End Using
            Return pingResult
        Catch ex As Exception
            Return False
        End Try
    End Function

    Public Overrides Async Function query() As Task(Of List(Of String))
        Dim rawList As String = Await getRawList()
        Dim result = New List(Of String)

        Dim packet = New UTQueryPacket(rawList, UTQueryPacket.UTQueryPacketFlags.UTQP_MasterServerIpList)
        For Each ipEntry As UTQueryKeyValuePair In packet
            If ipEntry.key = "ip" Then
                result.Add(ipEntry.value)
            End If
        Next
        Return result
    End Function

    Protected Async Function getRawList() As Task(Of String)
        Dim result As String = ""
        Dim connection As New JulkinNet With {
            .timeout = 2500
        }
        connection.Connect(server.address)

        Dim packet As String = Await connection.ReadNextAsync()
        If Len(packet) = 0 Then
            Throw New Exception("No response from " & server.address)
        End If

        Dim myResponse As New UTQueryPacket(UTQueryPacketFlags.UTQP_MasterServer)

        Using serverResponse = New UTQueryPacket(packet, UTQueryPacketFlags.UTQP_MasterServer Or UTQueryPacketFlags.UTQP_NoFinal)
            If gameInfo.gameName <> "" Then
                myResponse.Add("gamename", gameInfo.gameName)
            End If
            myResponse.Add("location", region)
            If serverResponse("secure") <> "" AndAlso serverResponse("secure") <> "wookie" Then ' challenge!
                Dim challengeReceived = serverResponse("secure")
                Dim challengeResponse = GsGetChallengeResponse(challengeReceived, gameInfo.encKey)
                myResponse.Add("validate", challengeResponse)
            End If
        End Using

        myResponse.Add("list", "")
        Await connection.WriteAsync(myResponse.ToString())

        Dim waitStart = TickCount()
        Do
            result &= Await connection.ReadNextAsync()
        Loop While TickCount() - waitStart < 7000 AndAlso InStr(result, "\final\") = 0

        connection.Disconnect()

        If Len(result) < 5 Then
            Throw New Exception("Master server query failed, no response to request")
        End If

        Dim mat = Regex.Match(result, "\\echo\\([^\\]*)\\final\\")
        If mat.Groups.Count > 1 Then
            Throw New Exception("Master server query failed, server responded: " & mat.Groups(1).Value)
        End If

        Return result
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

    Public Overrides Async Function ping() As Task(Of Boolean)
        Dim requestUrl = "http://" & server.ToString & requestURI

        Dim requestMsg As New HttpRequestMessage(HttpMethod.Head, requestUrl)
        Dim requestResult = Await clientObj.SendAsync(requestMsg)

        Return (requestResult.StatusCode = Net.HttpStatusCode.OK)
    End Function

    Public Overrides Async Function query() As Task(Of System.Collections.Generic.List(Of String))
        Dim serverList = New List(Of String)
        Dim requestUrl = "http://" & server.ToString & requestURI

        Dim requestResult = Await clientObj.GetAsync(requestUrl)

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
