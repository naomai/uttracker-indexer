Imports System.Net
Imports System.Text
Imports System.Text.Json
Imports System.Data
Imports Naomai.UTT.Indexer.Utt2Database
Imports Microsoft.EntityFrameworkCore.Storage

Public Class ServerScanner
    Implements IDisposable

    Public scanInterval = 120
    Public scanStart As Date
    Public scanEnd As Date
    Friend scanLastActivity, scanLastTouchAll As Date
    Public masterServerLastUpdate As Date
    Public masterServerUpdateInterval As Integer = 3600
    Public masterServerLastPing As Date
    Public masterServerPingInterval As Integer = 600

    Public serversCountTotal As Integer
    Public serversCountOnline As Integer

    Protected Friend log As Logger
    Protected Friend ini As IniFile
    Protected Friend dbCtx As Utt2Context
    Protected Friend dyncfg As DynConfig


    Protected WithEvents masterServerQuery As MasterServerManager
    Protected WithEvents sockets As SocketManager
    Protected serverWorkers As New Hashtable ' of ServerScannerWorker
    Protected serverWorkersLock As New Object 'prevent 'For Each mess when collection is modified'

    Protected tickCounter As Integer = 0
    Private dbTransaction As RelationalTransaction

    Event OnScanBegin(serverCount As Integer)
    Event OnScanComplete(scannedServerCount As Integer, onlineServerCount As Integer, elapsedTime As TimeSpan)

    Dim serverPacketBuffer As New Hashtable
    Dim disposed As Boolean = False

    Public Sub New(scannerConfig As ServerScannerConfig)
        With scannerConfig
            scanInterval = .scanInterval
            masterServerUpdateInterval = .masterServerUpdateInterval
            log = .log
            dbCtx = .dbCtx
            dyncfg = .dyncfg
            masterServerQuery = .masterServerManager
        End With

        debugWriteLine("ServerScanner ready")

    End Sub

    Public Sub performScan()
        Dim serversToScan As List(Of String), recentServersTimeRange = 3600, includeAncient As Boolean = False

        If (Date.UtcNow - masterServerLastUpdate).TotalSeconds > masterServerUpdateInterval Then ' full scan
            masterServerQuery.refreshServerList()
            masterServerLastUpdate = Date.UtcNow
            masterServerLastPing = Date.UtcNow
            recentServersTimeRange = 86400 * 30
            includeAncient = True
        End If

        If masterServerPingInterval > 0 AndAlso (Date.UtcNow - masterServerLastPing).TotalSeconds > masterServerPingInterval Then ' monitors the other master servers
            masterServerQuery.pingMasterServers()
            masterServerLastPing = Date.UtcNow
        End If

        serversToScan = masterServerQuery.getList()
        Dim serversFromDB = getRecentlyScannedServerList(recentServersTimeRange, includeAncient)
        For Each server As String In serversFromDB
            serversToScan.Add(server)
        Next
        serversFromDB = getServersPendingQueue()
        For Each server As String In serversFromDB
            serversToScan.Add(server)
        Next

        log.autoFlush = False

        debugWriteLine("Scanning using settings: recentServersTimeRange={0},includeAncient={1}", recentServersTimeRange, includeAncient)

        serversToScan = serversToScan.Distinct().ToList

        RaiseEvent OnScanBegin(serversToScan.Count)

        initSockets()
        initServerWorkers(serversToScan)

        scanStart = Date.UtcNow
        scanLastActivity = Date.UtcNow
        serversCountTotal = serverWorkers.Count

        touchAll(True)

        tickCounter = 0

        Do While ((Date.UtcNow - scanLastActivity).TotalSeconds < 10 Or (Date.UtcNow - scanLastTouchAll).TotalSeconds >= 3) ' second check: avoid ending the scan too early when 'time-travelling'
            sockets.Tick()
            If (Date.UtcNow - scanLastTouchAll).TotalSeconds > 2 Then
                'touchAll()
                touchInactive()
                'debugShowStates()
                scanLastTouchAll = Date.UtcNow
            End If
            If (Date.UtcNow - scanLastActivity).TotalSeconds > 5 Then
                touchAll()
                taskSleepLonger()
            End If

            tickCounter += 1
            If tickCounter Mod 48 = 0 Then taskSleep()
        Loop

        serversCountOnline = 0
        SyncLock serverWorkersLock
            For Each target As ServerQuery In serverWorkers.Values
                If target.getState().done AndAlso target.caps.isOnline Then
                    serversCountOnline += 1
                End If
            Next
        End SyncLock
        scanEnd = Date.UtcNow

        updateScanInfo()

        RaiseEvent OnScanComplete(serversCountTotal, serversCountOnline, scanEnd - scanStart)
        log.autoFlush = True
        debugWriteLine("Scan done in {0} seconds, {1} network ticks.", Math.Round((scanEnd - scanStart).TotalSeconds), tickCounter)
        debugShowStates()
        disposeTargets()
        disposeSockets()

    End Sub


    Public Sub packetHandler(packet() As Byte, source As IPEndPoint) Handles sockets.PacketReceived
        Dim fullPacket As String = "", target As ServerQuery, ipString As String

        Dim packetString As String
        ipString = source.ToString
        If Not serverWorkers.ContainsKey(ipString) Then Return ' unknown source!! we got packet that wasn't sent by any of the queried servers (haxerz?)
        target = serverWorkers(ipString)
        Try
            If target.getState().done Then Return ' prevent processing the packets from targets in "done" state
            If packet.Length = 0 Then Return

            packetString = Encoding.Unicode.GetString(Encoding.Convert(Encoding.UTF8, Encoding.Unicode, packet))

            fullPacket = serverPacketBuffer(ipString) & packetString

            target.incomingPacketObj = New UTQueryPacket(fullPacket)

            target.incomingPacket = target.incomingPacketObj.convertToHashtablePacket()
            target.tick()

            scanLastActivity = Date.UtcNow
            serverPacketBuffer(ipString) = ""

        Catch ex As UTQueryResponseIncompleteException
            serverPacketBuffer(source.ToString) = fullPacket
        Catch ex As UTQueryInvalidResponseException ' we found a port that belongs to other service, so we're not going to bother it anymore
            target.logDbg("InvalidQuery: found unknown service")
            target.abortScan()
            sockets.AddIgnoredIp(target.addressQuery)
        End Try
        'debugShowStates()

    End Sub


    Protected Sub initSockets()
        sockets = New SocketManager
    End Sub
    Protected Sub disposeSockets()
        sockets.clearIgnoredIps()
        sockets = Nothing
    End Sub

    Private Sub debugShowStates()
        Dim bas, inf, infex, pl, ru, tt, don, onl, ttp As Integer
        Dim st As ServerQueryState
        SyncLock serverWorkersLock
            For Each t As ServerQuery In serverWorkers.Values
                st = t.getState
                bas += IIf(st.hasBasic, 1, 0)
                inf += IIf(st.hasInfo, 1, 0)
                infex += IIf(st.hasInfoExtended, 1, 0)
                pl += IIf(st.hasPlayers, 1, 0)
                ru += IIf(st.hasRules, 1, 0)
                tt += IIf(st.hasTimeTest, 1, 0)
                don += IIf(st.done, 1, 0)
                onl += IIf(t.caps.isOnline, 1, 0)
                ttp += IIf(t.caps.timeTestPassed, 1, 0)
            Next
        End SyncLock
        debugWriteLine("States: BAS {0} INF {1} INFEX {2} PL {3} RU {4} TT {5} TTP {8} DO {6} ON {7}", bas, inf, infex, pl, ru, tt, don, onl, ttp)
    End Sub

    Protected Sub initServerWorkers(serverList As List(Of String))
        SyncLock serverWorkersLock
            For Each server In serverList
                serverWorkers(server) = New ServerQuery(Me, server)
                serverWorkers(server).setSocket(sockets)
                serverPacketBuffer(server) = ""
            Next
        End SyncLock
    End Sub

    Protected Sub disposeTargets()
        SyncLock serverWorkersLock
            For Each t As ServerQuery In serverWorkers.Values
                Dim s = t.getState()
            Next
            serverWorkers.Clear()
        End SyncLock

        serverPacketBuffer.Clear()

    End Sub

    Protected Sub updateScanInfo()
        dynconfigSet("lastupdate", UnixTime())
        dynconfigSet("scaninfo.serversscanned", serversCountTotal)
        dynconfigSet("scaninfo.serversonline", serversCountOnline)
        dynconfigSet("scaninfo.scantime", (scanEnd - scanStart).TotalSeconds)
        dynconfigSet("scaninfo.netticks", tickCounter)
        dynconfigSet("scaninterval", scanInterval)
        dynconfigSet("masterservers.lastupdate", UnixTime(masterServerLastUpdate))
        dynconfigSet("masterservers.numservers", masterServerQuery.Count)
        debugWriteLine("ScanInfoUpdated")
    End Sub

    Protected Function getRecentlyScannedServerList(Optional seconds As Integer = 86400, Optional includeAncientServers As Boolean = False) As List(Of String)
        Dim ancientTimes As DateTime = DateTime.Parse("1.01.2009 0:00:00") ' include servers with invalid last scan date due to bios time reset
        Dim scanTimeRange As DateTime = DateTime.UtcNow.AddSeconds(-seconds)

        Dim servers = dbCtx.Servers.Where(
            Function(p As Server) p.LastScan > scanTimeRange Or
            p.LastScan < ancientTimes
        ).Select(
            Function(s) New With {.Address = s.Address, .Rules = s.Rules}
        ).ToList()

        Dim recentServers = New List(Of String)
        Dim rules As Hashtable

        For Each server In servers
            Dim fullQueryIp = server.Address
            Try
                If Not IsDBNull(server.Rules) AndAlso server.Rules <> "" Then
                    rules = JsonSerializer.Deserialize(Of Hashtable)(server.Rules)
                    If Not IsNothing(rules) AndAlso rules.ContainsKey("queryport") Then
                        Dim ip = GetHost(server.Address)
                        fullQueryIp = ip & ":" & rules("queryport").ToString
                    End If
                End If
            Catch e As Exception
            End Try

            recentServers.Add(fullQueryIp)
        Next
        Return recentServers
    End Function

    Protected Function getServersPendingQueue() As List(Of String)
        Dim recentServers = dbCtx.ScanQueueEntries.ToList()

        Dim queueServers = New List(Of String)
        If recentServers.Count > 0 Then
            dbCtx.ScanQueueEntries.ExecuteDelete()

            For Each server In recentServers
                queueServers.Add(server.Address)
            Next
        End If
        debugWriteLine("getServersPendingQueue: {0}", queueServers.Count)
        Return queueServers
    End Function

    Protected Sub touchAll(Optional init As Boolean = False)
        SyncLock serverWorkersLock
            'If init Then

            Dim lastPacketBufferFlush As Date = Date.UtcNow
            For Each target As ServerQuery In serverWorkers.Values
                target.tick()
                If (Date.UtcNow - lastPacketBufferFlush).TotalMilliseconds > 50 Then
                    sockets.Tick()
                    taskSleep()
                    lastPacketBufferFlush = Date.UtcNow
                End If
            Next
            'Else
            'For Each target As ServerScannerWorker In serverWorkers.Values
            'target.tick()
            '    Next
            'End If
        End SyncLock
    End Sub

    Protected Sub touchInactive()
        For Each target As ServerQuery In serverWorkers.Values
            If (Date.UtcNow - target.lastActivity).TotalSeconds > 15 Then
                target.tick()
            End If
        Next
    End Sub

    Friend Sub logWriteLine(ByVal message As String)
        log.WriteLine("ServerScanner: " & message)
    End Sub

    Friend Sub logWriteLine(ByVal format As String, ByVal ParamArray arg As Object())
        log.WriteLine("ServerScanner: " & format, arg)
    End Sub

    Friend Sub debugWriteLine(ByVal message As String)
        log.DebugWriteLine("ServerScanner: " & message)
    End Sub

    Friend Sub debugWriteLine(ByVal format As String, ByVal ParamArray arg As Object())
        log.DebugWriteLine("ServerScanner: " & format, arg)
    End Sub

    Protected Sub taskSleep() 'suspends program for 1 ms, since we don't need 100% of cpu power
        ' todo: replace with timer queue api for more predictable execution times
        ' and NO, timeBeginPeriod(1) is not a good solution!!
        System.Threading.Thread.CurrentThread.Join(1)
    End Sub

    Protected Sub taskSleepLonger()
        System.Threading.Thread.CurrentThread.Join(200)
    End Sub

    Private Sub ServerScanner_OnScanBegin(serverCount As Integer) Handles Me.OnScanBegin
        dbTransaction = dbCtx.Database.BeginTransaction()
    End Sub


    Private Sub ServerScanner_OnScanComplete(scannedServerCount As Integer, onlineServerCount As Integer, elapsedTime As System.TimeSpan) Handles Me.OnScanComplete
        dbTransaction.Commit()
        dbTransaction.Dispose()
        dbTransaction = Nothing
    End Sub

    Private Sub masterServerQuery_OnMasterServerManagerRequest(masterServers As List(Of MasterServerInfo)) Handles masterServerQuery.OnMasterServerManagerRequest
        log.WriteLine("Master server query...")
        dyncfg.setProperty("masterservers.nummasters", masterServers.Count)
        dyncfg.UnsetProperty("masterservers.server")

    End Sub

    Private Sub masterServerQuery_OnMasterServerManagerRequestComplete(serverList As System.Collections.Generic.List(Of String)) Handles masterServerQuery.OnMasterServerManagerRequestComplete
        log.WriteLine("Received {0} servers, performing scan...", serverList.Count)
    End Sub

    Private Sub masterServerQuery_OnMasterServerQuery(serverInfo As MasterServerInfo) Handles masterServerQuery.OnMasterServerQuery
        log.WriteLine("MasterQuery ( " & serverInfo.serverClassName & " , " & serverInfo.serverIp & ":" & serverInfo.serverPort & " ) ")
        dyncfg.setProperty("masterservers.server." & serverInfo.serverId & ".checked", UnixTime())
        dyncfg.setProperty("masterservers.server." & serverInfo.serverId & ".info",
            serverInfo.serverIp & ":" & serverInfo.serverPort)
    End Sub


    Private Sub masterServerQuery_OnMasterServerQueryParsed(serverInfo As MasterServerInfo, serverList As System.Collections.Generic.List(Of String)) Handles masterServerQuery.OnMasterServerQueryListReceived
        dyncfg.setProperty("masterservers.server." & serverInfo.serverId & ".lastseen", UnixTime())
        dyncfg.setProperty("masterservers.server." & serverInfo.serverId & ".lastsync", UnixTime())
        dyncfg.setProperty("masterservers.server." & serverInfo.serverId & ".serversnum", serverList.Count)
        log.WriteLine("Got {0} servers.", serverList.Count)
    End Sub
    Private Sub masterServerQuery_OnMasterServerQueryFailure(serverInfo As MasterServerInfo, thrownException As System.Exception) Handles masterServerQuery.OnMasterServerQueryFailure
        log.WriteLine("Query failed for ( {0}:{1} ) : {2}", serverInfo.serverIp, serverInfo.serverPort, thrownException.Message)
    End Sub

    Private Sub masterServerQuery_OnMasterServerPing(serverInfo As MasterServerInfo, online As Boolean) Handles masterServerQuery.OnMasterServerPing
        log.DebugWriteLine("PingingRemoteMasterServer: {0}", serverInfo.serverAddress)
        dyncfg.setProperty("masterservers.server." & serverInfo.serverId & ".checked", UnixTime())
        If online Then
            dyncfg.setProperty("masterservers.server." & serverInfo.serverId & ".lastseen", UnixTime())
        End If
    End Sub
#Region "Dynconfig"
    Public Function dynconfigGet(key As String)
        Return dyncfg.GetProperty(key)
    End Function

    Public Sub dynconfigSet(key As String, data As String, Optional priv As Boolean = False)
        dyncfg.setProperty(key, data, priv)
    End Sub

#End Region

#Region "IDisposable"
    Public Sub Dispose() Implements IDisposable.Dispose
        Dispose(True)
        GC.SuppressFinalize(Me)
    End Sub

    Protected Overridable Sub Dispose(disposing As Boolean)
        If disposed Then Return

        If disposing Then

        End If

        If Not IsNothing(dbTransaction) Then
            dbTransaction.Rollback()
            dbTransaction.Dispose()
            dbTransaction = Nothing
        End If
        disposed = True
    End Sub
#End Region

End Class

Public Structure ServerScannerConfig
    Dim scanInterval As Integer
    Dim masterServerUpdateInterval As Integer
    Dim masterServerManager As MasterServerManager
    Dim log As Logger
    Dim dbCtx As Utt2Context
    Dim dyncfg As DynConfig
    Dim iniFile As String
End Structure

