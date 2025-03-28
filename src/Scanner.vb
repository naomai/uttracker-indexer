Imports System.Net
Imports System.Text
Imports System.Data
Imports Naomai.UTT.Indexer.Utt2Database
Imports Microsoft.EntityFrameworkCore.Storage
Imports System.Environment
Imports Microsoft.SqlServer

Public Class Scanner
    Friend scanLastTouchAll As Date


    Protected Friend log As Logger
    Protected Friend ini As IniPropsProvider
    Protected Friend dbCtx As Utt2Context
    Protected Friend dyncfg As IPropsProvider
    Protected Friend serverRepo As ServerRepository

    Protected serverList As New List(Of String)

    Protected fullScanDeadline As Date = Date.UtcNow

    Protected WithEvents masterServerQuery As MasterServerManager
    Protected WithEvents sockets As SocketManager
    Protected serverWorkers As New Dictionary(Of String, ServerQuery)
    Protected serverWorkersLock As New Object

    Protected Friend _targetCommLog As New Hashtable

    Public Sub New(log As Logger, context As Utt2Context, masterServerManager As MasterServerManager)
        Me.log = log
        Me.dbCtx = context
        Me.masterServerQuery = masterServerManager
        Me.serverRepo = New ServerRepository(dbCtx)

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
                    ' we don't speak with this host anymore (offline/misbehaving)
                    Continue For
                End If
                worker.Update()
            Next

            dbCtx.SaveChanges()

            scanLastTouchAll = Date.UtcNow
            If scanLastTouchAll >= showStatesDeadline Then
                debugShowStates()
                showStatesDeadline = scanLastTouchAll.AddSeconds(5)
            End If

            taskSleep()
        Loop
    End Sub

    ''' <summary>
    ''' Loads list of servers to scan from supported providers
    ''' </summary>
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
                ' set all previously processed targets as inactive
                ' the relevant ones will be resurrected in a while
                worker.isActive = False
            Next

            For Each server In serverList
                Dim worker As ServerQuery
                If Not serverWorkers.ContainsKey(server) Then
                    worker = New ServerQuery(Me, server)
                    serverWorkers(server) = worker
                    worker.setSocket(sockets)
                Else
                    ' resurrect
                    worker = serverWorkers(server)
                End If
                worker.isActive = True
            Next
        End SyncLock
    End Sub


    Public Sub packetHandler(packetBuffer As EndpointPacketBuffer, source As IPEndPoint) Handles sockets.NewDataReceived
        Dim target As ServerQuery, ipString As String

        Dim packetString As String
        ipString = source.ToString
        If Not serverWorkers.ContainsKey(ipString) Then
            ' unknown source!! we got packet that wasn't sent by any of the queried servers (haxerz?)
            packetBuffer.Clear()
            Return
        End If
        target = serverWorkers(ipString)

        If target.getState().done Then
            ' no more packets are expected from this server, because:
            ' we might have assumed the target is not a game server, or is misbehaving
            packetBuffer.Clear()
            Return
        End If

        ' get content of packet buffer without clearing it:
        ' if the response is incomplete, the next packets will be
        ' appended and evaluated again
        Dim packet As Byte() = packetBuffer.PeekAll()
        packetString = Encoding.Unicode.GetString(Encoding.Convert(target.GetPacketCharset(), Encoding.Unicode, packet))
        Try
            target.incomingPacket = New UTQueryPacket(packetString)

            ' successfully parsed
            commLogWrite(target.addressQuery, "DDD", packetString)
            packetBuffer.Clear()

            ' process response
            target.Tick()

        Catch ex As UTQueryResponseIncompleteException
            ' let's try another time, maybe the missing pieces will join us
            commLogWrite(target.addressQuery, "Dxx", packetString)

        Catch ex As UTQueryInvalidResponseException
            ' we found a port that belongs to other service, so we're not going to bother it anymore
            commLogWrite(target.addressQuery, "Dxx", packetString)
            target.abortScan("Unknown service", dumpCommLog:=True)
            sockets.AddIgnoredIp(target.addressQuery)

        Catch ex As Exception
            target.abortScan($"Unhandled error: {ex.Message}", dumpCommLog:=True)
        End Try

    End Sub

    Protected Sub initSockets()
        sockets = New SocketManager
    End Sub

    Private Sub debugShowStates()
        Dim sta, bas, inf, infex, pl, ru, don, onl As Integer
        Dim st As ServerQueryState
        SyncLock serverWorkersLock
            For Each t As ServerQuery In serverWorkers.Values
                st = t.getState
                sta += IIf(st.IsStarted, 1, 0)
                bas += IIf(st.HasBasic, 1, 0)
                inf += IIf(st.HasInfo, 1, 0)
                infex += IIf(st.HasInfoExtended, 1, 0)
                pl += IIf(st.HasPlayers, 1, 0)
                ru += IIf(st.HasVariables, 1, 0)
                don += IIf(st.done, 1, 0)
                onl += IIf(t.isOnline, 1, 0)
            Next
        End SyncLock
        debugWriteLine("States: STA {7} BAS {0} INF {1} INFEX {2} PL {3} RU {4} DO {5} ON {6}", bas, inf, infex, pl, ru, don, onl, sta)
    End Sub


    Protected Async Function GetRecentlyScannedServerList(Optional seconds As Integer = 86400) As Task(Of List(Of String))
        Dim scanTimeRange As DateTime
        If seconds = 0 Then
            scanTimeRange = DateTime.Parse("1.01.2009 0:00:00")
        Else
            scanTimeRange = DateTime.UtcNow.AddSeconds(-seconds)
        End If

        Await serverRepo.LoadAsync()
        Dim recentServers = (From server In serverRepo.All()
                             Where Not IsNothing(server.LastSuccess) AndAlso server.LastSuccess > scanTimeRange
                             Select server.AddressQuery).ToList()

        Return recentServers
    End Function

    Protected Async Function GetServersPendingQueue() As Task(Of List(Of String))
        Dim query = From server In dbCtx.ScanQueueEntries
                    Select server.Address
        Dim queueServers = Await query.ToListAsync()

        If queueServers.Count > 0 Then
            Await dbCtx.ScanQueueEntries.ExecuteDeleteAsync()
        End If
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

    Protected Sub taskSleep()
        System.Threading.Thread.CurrentThread.Join(1000)
    End Sub

    Protected Friend Sub commLogWrite(targetHost As String, tag As String, packet As String)
        Dim dateNow = Now.ToString("HH:mm:ss")
        '_targetCommLog(targetHost) &= $"[{dateNow}] {tag}: {packet}" & NewLine
    End Sub
End Class

