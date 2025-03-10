Imports System.Net
Imports System.Text
Imports System.Data
Imports Naomai.UTT.Indexer.Utt2Database
Imports Microsoft.EntityFrameworkCore.Storage
Imports System.Environment
Imports Microsoft.SqlServer

Public Class Scanner
    Implements IDisposable

    Public scanInterval = 120
    Friend scanLastTouchAll As Date
    Public masterServerUpdateInterval As Integer = 3600
    Public masterServerPingInterval As Integer = 600

    Public serversListCache

    Protected Friend log As Logger
    Protected Friend ini As IniPropsProvider
    Protected Friend dbCtx As Utt2Context
    Protected Friend dyncfg As IPropsProvider

    Protected serverList As New List(Of String)
    Public serverRecords As New Dictionary(Of String, Server)

    Protected fullScanDeadline As Date = Date.UtcNow


    Protected WithEvents masterServerQuery As MasterServerManager
    Protected WithEvents sockets As SocketManager
    Protected serverWorkers As New Dictionary(Of String, ServerQuery)
    Protected serverWorkersLock As New Object 'prevent 'For Each mess when collection is modified'

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

        initSockets()

        debugWriteLine("ServerScanner ready")

    End Sub

    Public Sub ScannerThread()
        Dim listRefreshDeadline As Date = Date.UtcNow
        Dim showStatesDeadline As Date = Date.UtcNow

        Do
            If Date.UtcNow >= listRefreshDeadline Then
                RefreshServerList().Wait()
                listRefreshDeadline = Date.UtcNow.AddMinutes(5)
            End If

            For Each worker As ServerQuery In serverWorkers.Values
                If Not worker.isActive Then
                    Continue For
                End If
                worker.Update()
                'sockets.Tick()
            Next
            dbCtx.SaveChanges()
            scanLastTouchAll = Date.UtcNow
            If scanLastTouchAll >= showStatesDeadline Then
                debugShowStates()
                showStatesDeadline = scanLastTouchAll.AddSeconds(5)
            End If

            taskSleep()

            'Threading.Thread.Sleep(10)
        Loop
    End Sub

    Public Async Function RefreshServerList() As Task
        Dim recentServersTimeRange As Integer = 60 * 60 ' hour by default
        Dim serverAddresses As New List(Of String)

        If fullScanDeadline <= Date.UtcNow Then
            recentServersTimeRange = 0 ' everything in database
            fullScanDeadline = Date.UtcNow.AddHours(1)
        End If
        serverAddresses.AddRange(masterServerQuery.GetList())
        serverAddresses.AddRange(
                Await GetRecentlyScannedServerList(recentServersTimeRange)
            )
        serverAddresses.AddRange(
            Await GetServersPendingQueue()
        )
        SyncLock serverList
            serverList.Clear()
            serverList.AddRange(serverAddresses.Distinct())
        End SyncLock

        InitWorkersFromServerList(serverList)
    End Function

    ''' <summary>
    ''' Creates ServerQuery worker objects from serverList and
    ''' marks workers not present in the list as inactive
    ''' </summary>
    ''' <param name="serverList"></param>
    Protected Sub InitWorkersFromServerList(serverList As List(Of String))
        SyncLock serverWorkersLock
            For Each worker As ServerQuery In serverWorkers.Values
                worker.isActive = False
            Next

            For Each server In serverList
                Dim worker As ServerQuery
                If Not serverWorkers.ContainsKey(server) Then
                    worker = New ServerQuery(Me, server)
                    serverWorkers(server) = worker
                    worker.setSocket(sockets)
                Else
                    worker = serverWorkers(server)
                End If
                worker.isActive = True
            Next
        End SyncLock
    End Sub



    ''' LEGACY

    Public Sub packetHandler(packetBuffer As EndpointPacketBuffer, source As IPEndPoint) Handles sockets.NewDataReceived
        Dim target As ServerQuery, ipString As String

        Dim packetString As String = ""
        ipString = source.ToString
        If Not serverWorkers.ContainsKey(ipString) Then Return ' unknown source!! we got packet that wasn't sent by any of the queried servers (haxerz?)
        target = serverWorkers(ipString)
        Try
            If target.getState().done Then
                ' prevent processing the packets from targets in "done" state
                packetBuffer.Clear()
                Return
            End If
            Dim packet As Byte() = packetBuffer.PeekAll()

            packetString = Encoding.Unicode.GetString(Encoding.Convert(target.GetPacketCharset(), Encoding.Unicode, packet))

            target.incomingPacketObj = New UTQueryPacket(packetString)

            target.incomingPacket = target.incomingPacketObj.ConvertToHashtablePacket()
            commLogWrite(target.addressQuery, "DDD", packetString)
            packetBuffer.Clear()

            target.Tick()


        Catch ex As UTQueryResponseIncompleteException
            ' let's try another time, maybe the missing pieces will join us
            commLogWrite(target.addressQuery, "Dxx", packetString)
        Catch ex As UTQueryInvalidResponseException ' we found a port that belongs to other service, so we're not going to bother it anymore
            'target.logDbg("InvalidQuery: found unknown service")
            commLogWrite(target.addressQuery, "Dxx", packetString)
            target.abortScan("Unknown service", dumpCommLog:=True)
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
                don += IIf(st.done, 1, 0)
                onl += IIf(t.isOnline, 1, 0)
            Next
        End SyncLock
        debugWriteLine("States: STA {9} BAS {0} INF {1} INFEX {2} PL {3} RU {4} DO {6} ON {7}", bas, inf, infex, pl, ru, tt, don, onl, ttp, sta)
    End Sub


    Protected Async Function GetRecentlyScannedServerList(Optional seconds As Integer = 86400) As Task(Of List(Of String))
        Dim scanTimeRange As DateTime
        If seconds = 0 Then
            scanTimeRange = DateTime.Parse("1.01.2009 0:00:00")
        Else
            scanTimeRange = DateTime.UtcNow.AddSeconds(-seconds)
        End If

        Try
            Await dbCtx.Servers _
                .Select(Function(s) New With {
                    s,
                    .LatestMatch = s.ServerMatches.OrderByDescending(Function(m) m.Id).FirstOrDefault()
                }) _
            .ToListAsync()

            For Each server In dbCtx.Servers.Local
                If Not serverRecords.ContainsKey(server.AddressQuery) Then
                    serverRecords(server.AddressQuery) = server
                End If
            Next

        Catch e As Exception

        End Try

        Dim listQuery = dbCtx.Servers.Local.Where(
                Function(p As Server) Not IsNothing(p.LastSuccess) AndAlso p.LastSuccess > scanTimeRange
            )

        Dim servers As List(Of Server) = listQuery.ToList()

        serversListCache = servers

        Dim recentServers = New List(Of String)

        For Each server In servers
            Dim fullQueryIp = server.AddressQuery

            recentServers.Add(fullQueryIp)
        Next
        Return recentServers
    End Function

    Protected Async Function GetServersPendingQueue() As Task(Of List(Of String))
        Dim recentServers = Await dbCtx.ScanQueueEntries.ToListAsync()

        Dim queueServers = New List(Of String)
        If recentServers.Count > 0 Then
            Await dbCtx.ScanQueueEntries.ExecuteDeleteAsync()

            For Each server In recentServers
                queueServers.Add(server.Address)
            Next
        End If
        debugWriteLine("getServersPendingQueue: {0}", queueServers.Count)
        Return queueServers
    End Function


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
        System.Threading.Thread.CurrentThread.Join(250)
    End Sub

    Protected Friend Sub commLogWrite(targetHost As String, tag As String, packet As String)
        Dim dateNow = Now.ToString("HH:mm:ss")
        '_targetCommLog(targetHost) &= $"[{dateNow}] {tag}: {packet}" & NewLine
    End Sub

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
        dyncfg.SetProperty(key, data, priv)
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

