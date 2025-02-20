Imports System.Threading
Imports MySql.Data.MySqlClient
Imports System.Data

' this one is meant to be used with outcomepred.php
' to get accurate frags count at the end of games
Public Class AsyncLittleScanner
    Inherits Scanner
    'Dim scannerThread, checkerThread As Thread
    'Dim forceStop As Boolean = False


    Public Sub New(config As ServerScannerConfig)
        MyBase.New(config)
        'initSockets()
    End Sub

    'Public Sub asyncBegin()
    '    scannerThread = New Thread(AddressOf asyncLoop)
    '    scannerThread.Name = "AsyncLittleScanner Scanner"
    '    scannerThread.Start()

    '    checkerThread = New Thread(AddressOf asyncCheckerLoop)
    '    checkerThread.Name = "AsyncLittleScanner CheckerLoop"
    '    checkerThread.Start()
    'End Sub

    'Public Sub asyncEnd()
    '    forceStop = True
    'End Sub

    'Public Sub asyncCheckerLoop()
    '    Dim serverList As List(Of String)
    '    Do
    '        Try


    '            serverList = getTinyScanPendingQueue()
    '            If serverList.Count > 0 Then

    '                initTargets(serverList)
    '                touchAllEx(True)
    '                scanLastActivity = Date.UtcNow
    '            End If
    '        Catch ex As Exception

    '        End Try
    '        Thread.Sleep(1000)
    '    Loop While Not forceStop
    'End Sub

    'Public Sub asyncLoop()
    '    tickCounter = 0
    '    Do
    '        Try
    '            If targets.Count = 0 Then
    '                Thread.Sleep(250)
    '            Else
    '                If (Date.UtcNow - scanLastTouchAll).TotalSeconds > 2 Then
    '                    scanLastTouchAll = Date.UtcNow
    '                End If
    '                If (Date.UtcNow - scanLastActivity).TotalSeconds > 3 Then
    '                    touchAllEx()
    '                End If
    '                socketMaster.tick()
    '                tickCounter += 1
    '                If tickCounter Mod 8 = 0 Then taskSleep()
    '            End If
    '        Catch ex As Exception

    '        End Try
    '    Loop While Not forceStop
    'End Sub


    'Protected Sub touchAllEx(Optional init As Boolean = False)
    '    Try
    '        If init Then
    '            SyncLock targetsCollectionLock
    '                For Each target As ServerScannerWorker In targets.Values
    '                    target.tick()
    '                    taskSleep()
    '                Next
    '            End SyncLock
    '        Else
    '            Dim removeQueue As New List(Of String)
    '            SyncLock targetsCollectionLock
    '                For Each targetKey As String In targets.Keys
    '                    Dim target As ServerScannerWorker = targets(targetKey)
    '                    target.tick()
    '                    If (Date.UtcNow - target.lastActivity).TotalSeconds > 15 Then
    '                        target.logDbg("Timeout")
    '                        target.abortScan()
    '                    End If
    '                    If target.getState.done Then
    '                        'targets.Remove(targetKey)
    '                        removeQueue.Add(targetKey)
    '                        serverMarkAsScanned(target.address)
    '                    End If
    '                Next
    '            End SyncLock
    '            If removeQueue.Count > 0 Then
    '                SyncLock targetsCollectionLock
    '                    For Each targetKey As String In removeQueue
    '                        targets.Remove(targetKey)
    '                    Next
    '                End SyncLock
    '            End If

    '        End If
    '    Catch e As Exception When e.Source = "MySql.Data"
    '        If Not (db.dbh.State And ConnectionState.Open) Then
    '            db.Reconnect()
    '        End If
    '    Catch e As Exception

    '    End Try
    'End Sub

    'Protected Sub serverMarkAsScanned(address As String)
    '    Dim currentTime = unixTime()
    '    Dim serverMarkCmd As New MySqlCommand("Update `tinyscanschedule` set `status` = `status` | 2, `scannedtime`=@time where `address` = @address", db.dbh)
    '    serverMarkCmd.Parameters.AddWithValue("@time", currentTime)
    '    serverMarkCmd.Parameters.AddWithValue("@address", address)
    '    serverMarkCmd.CommandType = CommandType.Text
    '    SyncLock db.dbh
    '        serverMarkCmd.ExecuteNonQuery()
    '    End SyncLock
    'End Sub

    'Protected Function getTinyScanPendingQueue() As List(Of String)
    '    Dim currentTime = unixTime()
    '    Dim serverQueueCmd As New MySqlCommand("Select `address`,`time` from `tinyscanschedule` where `status` & 1 = 0 and `time` between @timeStart and @timeEnd", db.dbh)
    '    serverQueueCmd.Parameters.AddWithValue("@timeStart", currentTime - 120)
    '    serverQueueCmd.Parameters.AddWithValue("@timeEnd", currentTime + 2)
    '    serverQueueCmd.CommandType = CommandType.Text
    '    Dim queryAdapter = New MySqlDataAdapter(serverQueueCmd)
    '    Dim table = New DataTable
    '    SyncLock db.dbh
    '        queryAdapter.Fill(table)
    '    End SyncLock
    '    Dim queueServers = New List(Of String)
    '    If table.Rows.Count > 0 Then
    '        Dim queueClearCmd As New MySqlCommand("Update `tinyscanschedule` set `status` = `status` | 1 where `status` & 1 = 0 and `time` <= @timeEnd", db.dbh)
    '        queueClearCmd.Parameters.AddWithValue("@timeEnd", currentTime + 2)
    '        SyncLock db.dbh
    '            queueClearCmd.ExecuteNonQuery()
    '        End SyncLock
    '        For Each server As DataRow In table.Rows
    '            queueServers.Add(server("address"))
    '            log.WriteLine("TINY: CRON(scan:{0},{1}-{2})", server("address"), server("time"), unixTime)
    '        Next
    '    End If
    '    Return queueServers
    'End Function
End Class
