Imports MySql.Data.MySqlClient
Imports System.Threading
Imports System.Data
Imports System.Text.Json


Public Class GSMasterServerBridge
    Implements IServerListProvider
    Dim db As MySqlConnection

    Dim serversByGame As New Dictionary(Of String, ServersListForGame)

    Public Sub New(databaseConnection As MySqlConnection)
        db = databaseConnection
    End Sub
    Private Function getServerListForGame(gamename As String) As List(Of String) Implements IServerListProvider.getServerListForGame
        maybeRefreshServerListForGame(gamename)
        Return serversByGame(gamename).serverList
    End Function

    Private Function getAboutInfo() As Dictionary(Of String, String) Implements IServerListProvider.getAboutInfo
        Dim aboutInfo = New Dictionary(Of String, String)
        aboutInfo("Bridge.DBServer") = db.ServerVersion
        aboutInfo("Bridge.ServersCache") = ""
        For Each gameEntry In serversByGame.Keys
            aboutInfo("Bridge.ServersCache") &= gameEntry & "(Num=" & serversByGame(gameEntry).serverList.Count & ",Age=" & Math.Round((Date.UtcNow - serversByGame(gameEntry).lastUpdate).TotalSeconds) & ") "
        Next
        Return aboutInfo
    End Function

    Private Sub maybeRefreshServerListForGame(gamename As String)
        If Not serversByGame.ContainsKey(gamename) OrElse (Date.UtcNow - serversByGame(gamename).lastUpdate).TotalMinutes > 2 Then
            refreshServerListForGame(gamename)
        End If
    End Sub

    Private Sub refreshServerListForGame(gamename As String)
        Dim serverList As New List(Of String), gameEntry As ServersListForGame, table As DataTable
        Dim rules As Hashtable
        Dim serverListCmd As New MySqlCommand("Select `address`,`rules` from `serverinfo` where `lastscan`>@lastscan and `gamename`=@gamename", db)
        serverListCmd.CommandType = CommandType.Text
        serverListCmd.Parameters.AddWithValue("@lastscan", unixTime() - 600)
        serverListCmd.Parameters.AddWithValue("@gamename", gamename)
        Dim queryAdapter = New MySqlDataAdapter(serverListCmd)
        table = New DataTable
        Try
            SyncLock db
                queryAdapter.Fill(table)
            End SyncLock
            queryAdapter.Dispose()
            serverListCmd.Dispose()
            For Each server As DataRow In table.Rows
                Dim fullQueryIp = server("address")
                Try
                    If Not IsDBNull(server("rules")) AndAlso server("rules") <> "" Then
                        rules = JsonSerializer.Deserialize(Of Hashtable)(server("rules"))
                        If Not IsNothing(rules) AndAlso rules.ContainsKey("queryport") Then
                            Dim ip = getIp(server("address"))
                            fullQueryIp = ip & ":" & rules("queryport")
                        End If
                    End If
                Catch e As Exception
                End Try
                serverList.Add(fullQueryIp)
            Next
            gameEntry.serverList = serverList
            gameEntry.lastUpdate = Date.UtcNow
            serversByGame(gamename) = gameEntry
        Catch e As Exception When e.Source = "MySql.Data"
            If Not (db.State And ConnectionState.Open) Then
                db.Close()
                db.Open()
            End If
        End Try
    End Sub

    Private Structure ServersListForGame
        Dim serverList As List(Of String)
        Dim lastUpdate As DateTime
    End Structure


End Class
