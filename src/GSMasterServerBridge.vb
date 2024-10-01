Imports MySql.Data.MySqlClient
Imports System.Threading
Imports System.Data
Imports System.Text.Json
Imports Naomai.UTT.Indexer.JulkinNet
Imports Naomai.UTT.Indexer.Utt2Database


Public Class GSMasterServerBridge
    Implements IServerListProvider
    Dim dbCtx As Utt2Context

    Dim serversByGame As New Dictionary(Of String, ServersListForGame)

    Public Sub New(databaseContext As Utt2Context)
        dbCtx = databaseContext
    End Sub
    Private Function getServerListForGame(gamename As String) As List(Of String) Implements IServerListProvider.getServerListForGame
        maybeRefreshServerListForGame(gamename)
        Return serversByGame(gamename).serverList
    End Function

    Private Function getAboutInfo() As Dictionary(Of String, String) Implements IServerListProvider.getAboutInfo
        Dim aboutInfo = New Dictionary(Of String, String)
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
        Dim serverList As New List(Of String), gameEntry As ServersListForGame
        Dim rules As Hashtable

        Dim timeLowerLimit = DateTime.UtcNow.AddMinutes(-10)

        Dim serverRecords = dbCtx.Servers.
            Where(Function(s) s.LastSuccess > timeLowerLimit AndAlso s.GameName = gamename).
            Select(Function(s) New With {s.Address, s.Variables})


        For Each server In serverRecords
            Dim fullQueryIp = server.Address
            Try
                If Not IsNothing(server.Rules) AndAlso server.Rules <> "" Then
                    rules = JsonSerializer.Deserialize(Of Hashtable)(server.Rules)
                    If Not IsNothing(rules) AndAlso rules.ContainsKey("queryport") Then
                        Dim host = GetHost(server.Address)
                        fullQueryIp = host & ":" & rules("queryport").ToString()
                    End If
                End If
            Catch e As Exception
            End Try
            serverList.Add(fullQueryIp)
        Next
        gameEntry.serverList = serverList
        gameEntry.lastUpdate = Date.UtcNow
        serversByGame(gamename) = gameEntry

    End Sub

    Private Structure ServersListForGame
        Dim serverList As List(Of String)
        Dim lastUpdate As DateTime
    End Structure


End Class
