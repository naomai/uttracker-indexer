'Imports System.Data.SQLite
Imports MySql.Data.MySqlClient
Imports System.Threading
Imports System.Data

Public Class MySQLDB
    Public dbh As MySqlConnection
    Public ready As Boolean = False
    Public dbtr As MySqlTransaction
    Private connStr As String

    '14-07-29 added transactions based on code from sqlitedb.vb

    Public Sub New(ByVal connectionConfig As MySQLDBConfig)
        Open(connectionConfig)
    End Sub

    Public Sub New(ByVal connectionString As String)
        Open(connectionString)
    End Sub
    Public Sub Open(ByVal connectionConfig As MySQLDBConfig)
        Open(makeConnectionStringFromConfigStruct(connectionConfig))
    End Sub

    Public Sub Open(ByVal connectionString As String)
        'Dim e As Exception
        ' Try
        dbh = New MySqlConnection(connectionString)

        dbh.Open()
        Dim cmd = New MySqlCommand("SET NAMES 'UTF8'", dbh)
        cmd.ExecuteNonQuery()
        'dbtr = dbh.BeginTransaction()
        ready = True
        'Catch e
        '   Console.WriteLine("db0penfail: " & e.Message)
        '   dbh.Dispose()
        'End Try
        connStr = connectionString
    End Sub

    Public Sub Reconnect()
        Me.Close()

        Me.Open(connStr)
    End Sub

    Public Function queryGetData(ByVal query As String) As DataTable
        If Me.ready Then

            Dim returnVal As New DataTable
            Dim cmd As New MySqlCommand(query, Me.dbh)
            Dim success = False
            Dim e As Exception = Nothing

            cmd.CommandText = query
            Try
                Dim da = New MySqlDataAdapter(cmd)
                'Do
                '    success = True
                '    Thread.Sleep(10)
                '    Try
                '        da.Fill(returnVal)
                '    Catch e
                '        success = False
                '    End Try
                'Loop While Not success AndAlso IsNothing(e)
                SyncLock dbh
                    da.Fill(returnVal)
                End SyncLock
                da.Dispose()
            Catch e
                Debug.WriteLine("dbqueryfail: " & e.Message)
            End Try


            'reader = cmd.ExecuteReader()
            'returnVal.Load(reader)
            'reader.Close()
            Return returnVal
        End If
        Return Nothing
    End Function

    Public Sub execCmd(ByVal query As String)
        execCmdNow(query)
        'old:
        ' buffer &= query & ";" & vbNewLine
        ' If Len(buffer) >= 8192 Then
        'flushBuffer()
        'End If

    End Sub

    Public Function LastInsertRowId() As Integer
        LastInsertRowId = Nothing
        If Me.ready Then

            Dim cmd As New MySqlCommand("SELECT LAST_INSERT_ID();", Me.dbh)
            Dim e As Exception = Nothing
            'Dim success As Boolean
            ' Do
            'success = True
            'Thread.Sleep(10)

            Try
                SyncLock dbh
                    LastInsertRowId = CInt(cmd.ExecuteScalar())
                End SyncLock
            Catch e
                'success = False
                LastInsertRowId = Nothing
            Finally
                cmd.Dispose()
                cmd = Nothing
            End Try
            'Loop While Not success AndAlso IsNothing(e)
        End If
    End Function

    Public Function execCmdNow(ByVal command As String) As Integer
        execCmdNow = Nothing
        If Me.ready Then

            Dim cmd As New MySqlCommand(command, Me.dbh)
            Dim e As Exception = Nothing
            'Console.WriteLine(query)
            'Dim success As Boolean
            ' Do
            'success = True
            'Thread.Sleep(10)
            If IsNothing(dbtr) Then
                dbtr = dbh.BeginTransaction()
            End If

            Try
                SyncLock dbh
                    execCmdNow = cmd.ExecuteNonQuery()
                End SyncLock
            Catch e
                'success = False
                execCmdNow = Nothing
            Finally
                cmd.Dispose()
                cmd = Nothing
            End Try
            'Loop While Not success AndAlso IsNothing(e)
        End If
    End Function
    Public Sub flushBuffer()
        If Me.ready Then

            Dim e As Exception = Nothing
            Try
                If Not IsNothing(dbtr) AndAlso dbh.State.HasFlag(ConnectionState.Open) Then
                    SyncLock dbh
                        dbtr.Commit()
                    End SyncLock
                    dbtr.Dispose()
                    dbtr = Nothing
                End If
            Catch e
                Debug.Print("sq xcept: " & e.Message)
            End Try
        End If
    End Sub
    Public Sub Close()
        flushBuffer()
        'Dim dbt = New SQLiteConnection(connStr)
        'dbt.Open()
        'dbh.BackupDatabase(dbt, "main", "main", -1, Nothing, -1)
        'dbt.Close()
        dbh.Close()
        dbh.Dispose()
        dbh = Nothing
    End Sub

    ' I know it's EVIL, and I should use SqlCommand.Parameters instead... 
    ' But I don't care!
    Public Shared Function sqlescape(ByVal str As String)
        Return Replace(str, "'", "\'")
    End Function

    Public Shared Function makeConnectionStringFromConfigStruct(connectionConfig As MySQLDBConfig) As String
        Dim connectionString As String
        With connectionConfig
            If .protocol = "pipe" Then
                connectionString = String.Format("server=.;uid={1};pwd={2};database={3};protocol=pipe;pipename={0};charset=utf8", .host, .username, .password, .database)
            Else
                connectionString = String.Format("server={0};uid={1};pwd={2};database={3};protocol={4};charset=utf8", .host, .username, .password, .database, .protocol)
            End If
        End With
        Return connectionString
    End Function
End Class

Public Structure MySQLDBConfig
    Dim host As String
    Dim protocol As String
    Dim username As String
    Dim password As String
    Dim database As String
    ' Dim charset As String
End Structure