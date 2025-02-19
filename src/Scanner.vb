Imports System.Net
Imports System.Text
Imports System.Text.Json
Imports System.Data
Imports Naomai.UTT.Indexer.Utt2Database
Imports Microsoft.EntityFrameworkCore.Storage
Imports System.Environment
Imports Naomai.UTT.Indexer.JulkinNet
Imports Microsoft.EntityFrameworkCore.Query.Internal
Imports Microsoft.SqlServer

Public Class Scanner
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
    Public serversListCache

    Private plannedScanTimeMs As Integer
    Private lastScanOverdueTimeMs As Integer = 0
    Private deployScanJobIntervalMs As Single
    Private jobsWaitingToDeploy As Boolean

    Protected Friend log As Logger
    Protected Friend ini As IniPropsProvider
    Protected Friend dbCtx As Utt2Context
    Protected Friend dyncfg As IPropsProvider


    Protected WithEvents masterServerQuery As MasterServerManager
    Protected WithEvents sockets As SocketManager
    Protected serverWorkers As New Hashtable ' of ServerScannerWorker
    Protected serverWorkersLock As New Object 'prevent 'For Each mess when collection is modified'

    Protected tickCounter As Integer = 0
    Private dbTransaction As RelationalTransaction

    Event OnScanBegin(serverCount As Integer)
    Event OnScanComplete(scannedServerCount As Integer, onlineServerCount As Integer, elapsedTime As TimeSpan)

    Protected Friend _targetCommLog As New Hashtable

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
        Dim serversToScan As List(Of String)
        Dim recentServersTimeRange = 60 * 60 ' only servers seen in last hour
        _targetCommLog.Clear()

        serversToScan = masterServerQuery.GetList()
        Dim serversFromDB = getRecentlyScannedServerList(recentServersTimeRange)
        For Each server As String In serversFromDB
            serversToScan.Add(server)
        Next
        serversFromDB = getServersPendingQueue()
        For Each server As String In serversFromDB
            serversToScan.Add(server)
        Next

        log.autoFlush = False

        debugWriteLine("Scanning using settings: recentServersTimeRange={0}", recentServersTimeRange)

        serversToScan = serversToScan.Distinct().ToList

        RaiseEvent OnScanBegin(serversToScan.Count)

        initSockets()
        initServerWorkers(serversToScan)

        scanStart = Date.UtcNow
        scanLastActivity = Date.UtcNow
        serversCountTotal = serverWorkers.Count

        Try

            touchAll(True)

            tickCounter = 0

            Do While jobsWaitingToDeploy OrElse
                (Date.UtcNow - scanLastActivity).TotalSeconds < 10 OrElse
                (Date.UtcNow - scanLastTouchAll).TotalSeconds >= 3 ' check: avoid ending the scan too early when 'time-travelling'

                sockets.Tick()
                If jobsWaitingToDeploy OrElse (Date.UtcNow - scanLastTouchAll).TotalSeconds > 2 Then
                    touchInactive()
                    scanLastTouchAll = Date.UtcNow
                End If
                If (Date.UtcNow - scanLastActivity).TotalSeconds > 5 Then
                    touchAll()
                    taskSleepLonger()
                End If

                tickCounter += 1
                'If tickCounter Mod 48 = 0 Then taskSleep()\
                If tickCounter Mod 300 = 0 Then debugShowStates()
                taskSleep()
            Loop

            serversCountOnline = 0
            SyncLock serverWorkersLock
                For Each target As ServerQuery In serverWorkers.Values
                    If target.getState().done AndAlso target.isOnline Then
                        serversCountOnline += 1
                    End If
                Next
            End SyncLock
            scanEnd = Date.UtcNow

            updateScanInfo()

            RaiseEvent OnScanComplete(serversCountTotal, serversCountOnline, scanEnd - scanStart)

            log.autoFlush = True
            debugWriteLine("Scan done in {0} seconds, {1} network ticks.", Math.Round((scanEnd - scanStart).TotalSeconds), tickCounter)
        Catch e As MySqlException
            log.autoFlush = True
            logWriteLine("Scan failed with database error: {0}", e.Message)
            dbTransaction = Nothing
        End Try
        lastScanOverdueTimeMs = Math.Max(0, GetScanTimeMs() - plannedScanTimeMs)

        debugShowStates()
        disposeTargets()
        disposeSockets()

    End Sub


    Public Sub packetHandler(packetBuffer As EndpointPacketBuffer, source As IPEndPoint) Handles sockets.NewDataReceived
        Dim target As ServerQuery, ipString As String

        Dim packetString As String = ""
        ipString = source.ToString
        If Not serverWorkers.ContainsKey(ipString) Then Return ' unknown source!! we got packet that wasn't sent by any of the queried servers (haxerz?)
        target = serverWorkers(ipString)
        Try
            If target.getState().done Then Return ' prevent processing the packets from targets in "done" state
            Dim packet As Byte() = packetBuffer.PeekAll()

            packetString = Encoding.Unicode.GetString(Encoding.Convert(target.GetPacketCharset(), Encoding.Unicode, packet))

            target.incomingPacketObj = New UTQueryPacket(packetString)

            target.incomingPacket = target.incomingPacketObj.ConvertToHashtablePacket()
            _targetCommLog(target.addressQuery) &= "DDD " & packetString & NewLine
            target.tick()

            scanLastActivity = Date.UtcNow
            packetBuffer.Clear()

        Catch ex As UTQueryResponseIncompleteException
            ' let's try another time, maybe the missing pieces will join us
            _targetCommLog(target.addressQuery) &= "Dxx " & packetString & NewLine
        Catch ex As UTQueryInvalidResponseException ' we found a port that belongs to other service, so we're not going to bother it anymore
            'target.logDbg("InvalidQuery: found unknown service")
            _targetCommLog(target.addressQuery) &= "Dxx " & packetString & NewLine
            target.abortScan("Unknown service")
            sockets.AddIgnoredIp(target.addressQuery)
        End Try
        'debugShowStates()

    End Sub


    Protected Sub initSockets()
        sockets = New SocketManager
    End Sub
    Protected Sub disposeSockets()
        sockets.ClearIgnoredIps()
        sockets = Nothing
    End Sub

    Private Sub debugShowStates()
        Dim sta, bas, inf, infex, pl, ru, tt, don, onl, ttp As Integer
        Dim st As ServerQueryState
        SyncLock serverWorkersLock
            For Each t As ServerQuery In serverWorkers.Values
                st = t.getState
                sta += IIf(st.started, 1, 0)
                bas += IIf(st.hasBasic, 1, 0)
                inf += IIf(st.hasInfo, 1, 0)
                infex += IIf(st.hasInfoExtended, 1, 0)
                pl += IIf(st.hasPlayers, 1, 0)
                ru += IIf(st.hasVariables, 1, 0)
                tt += IIf(st.hasTimeTest, 1, 0)
                don += IIf(st.done, 1, 0)
                onl += IIf(t.isOnline, 1, 0)
            Next
        End SyncLock
        debugWriteLine("States: STA {9} BAS {0} INF {1} INFEX {2} PL {3} RU {4} TT {5} TTP {8} DO {6} ON {7}", bas, inf, infex, pl, ru, tt, don, onl, ttp, sta)
    End Sub

    Protected Sub initServerWorkers(serverList As List(Of String))
        plannedScanTimeMs = (scanInterval - 20) * 1000 - lastScanOverdueTimeMs
        deployScanJobIntervalMs = plannedScanTimeMs / serverList.Count

        Dim jobIndex = 0
        SyncLock serverWorkersLock
            For Each server In serverList
                Dim worker = New ServerQuery(Me, server)
                worker.setSocket(sockets)
                worker.deployTimeOffsetMs = deployScanJobIntervalMs * jobIndex
                serverWorkers(server) = worker
                jobIndex += 1
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

    Protected Function getRecentlyScannedServerList(Optional seconds As Integer = 86400) As List(Of String)
        Dim scanTimeRange As DateTime
        If seconds = 0 Then
            scanTimeRange = DateTime.Parse("1.01.2009 0:00:00")
        Else
            scanTimeRange = DateTime.UtcNow.AddSeconds(-seconds)
        End If

        Dim listQuery = dbCtx.Servers.Where(
                Function(p As Server) p.LastSuccess > scanTimeRange
            )

        Dim servers As List(Of Server) = listQuery.ToList()

        serversListCache = servers

        Dim recentServers = New List(Of String)
        'Dim rules As Hashtable

        For Each server In servers
            Dim fullQueryIp = server.AddressQuery
            'Try
            '    If Not IsDBNull(server.Variables) AndAlso server.Variables <> "" Then
            '        rules = JsonSerializer.Deserialize(Of Hashtable)(server.Variables)
            '        If Not IsNothing(rules) AndAlso rules.ContainsKey("queryport") Then
            '            Dim ip = GetHost(server.AddressQuery)
            '            fullQueryIp = ip & ":" & rules("queryport").ToString
            '        End If
            '    End If
            'Catch e As Exception
            'End Try

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
            Dim lastPacketBufferFlush As Date = Date.UtcNow
            Dim currentScanTime = GetScanTimeMs()
            jobsWaitingToDeploy = False
            For Each target As ServerQuery In serverWorkers.Values
                If currentScanTime < target.deployTimeOffsetMs Then
                    jobsWaitingToDeploy = True
                Else
                    target.tick()
                End If


                If (Date.UtcNow - lastPacketBufferFlush).TotalMilliseconds > 50 Then
                    sockets.Tick()
                    taskSleep()
                    lastPacketBufferFlush = Date.UtcNow
                End If
            Next

        End SyncLock
    End Sub

    Protected Sub touchInactive()
        jobsWaitingToDeploy = False
        For Each target As ServerQuery In serverWorkers.Values
            If GetScanTimeMs() < target.deployTimeOffsetMs Then
                jobsWaitingToDeploy = True
                Continue For
            End If

            If (Date.UtcNow - target.lastActivity).TotalSeconds > 10 Then
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
        System.Threading.Thread.CurrentThread.Join(10)
    End Sub

    Protected Sub taskSleepLonger()
        System.Threading.Thread.CurrentThread.Join(200)
    End Sub

    Protected Function GetScanTimeMs() As Integer
        Return (Date.UtcNow - scanStart).TotalMilliseconds
    End Function

    Private Sub ServerScanner_OnScanBegin(serverCount As Integer) Handles Me.OnScanBegin
        'dbTransaction = dbCtx.Database.BeginTransaction()
    End Sub


    Private Sub ServerScanner_OnScanComplete(scannedServerCount As Integer, onlineServerCount As Integer, elapsedTime As System.TimeSpan) Handles Me.OnScanComplete
        'dbTransaction.Commit()
        dbTransaction.Dispose()
        dbTransaction = Nothing
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
    Dim dyncfg As IPropsProvider
    Dim iniFile As String
End Structure

