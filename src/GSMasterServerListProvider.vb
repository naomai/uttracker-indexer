Imports System.Data
Imports System.Text.Json
Imports Naomai.UTT.Indexer.JulkinNet
Imports Naomai.UTT.Indexer.Utt2Database


Public Class GSMasterServerListProvider
    Implements IServerListProvider
    Dim dbCtx As Utt2Context

    Dim serversByGame As New Dictionary(Of String, GameServerList)

    Public Sub New(databaseContext As Utt2Context)
        dbCtx = databaseContext
    End Sub
    Private Function GetServerListForGame(gamename As String) As List(Of String) Implements IServerListProvider.GetServerListForGame
        MaybeRefreshServerListForGame(gamename)
        Return serversByGame(gamename).ServerList
    End Function

    Private Sub MaybeRefreshServerListForGame(gamename As String)
        If Not serversByGame.ContainsKey(gamename) OrElse (Date.UtcNow - serversByGame(gamename).LastUpdate).TotalMinutes > 2 Then
            RefreshServerListForGame(gamename)
        End If
    End Sub

    Private Sub RefreshServerListForGame(gamename As String)
        Dim serverList As New List(Of String), gameEntry As GameServerList
        Dim rules As Hashtable

        Dim timeLowerLimit = DateTime.UtcNow.AddMinutes(-10)

        Dim serverRecords = From server In dbCtx.Servers
                            Where server.LastSuccess > timeLowerLimit AndAlso server.GameName = gamename
                            Select New With {server.AddressQuery, server.Variables}


        For Each serverRecord In serverRecords
            Dim fullQueryIp = serverRecord.AddressQuery
            Try
                If Not IsNothing(serverRecord.Variables) AndAlso serverRecord.Variables <> "" Then
                    rules = JsonSerializer.Deserialize(Of Hashtable)(serverRecord.Variables)
                    If Not IsNothing(rules) AndAlso rules.ContainsKey("queryport") Then
                        Dim host = GetHost(serverRecord.AddressQuery)
                        fullQueryIp = host & ":" & rules("queryport").ToString()
                    End If
                End If
            Catch e As Exception
            End Try
            serverList.Add(fullQueryIp)
        Next
        gameEntry.ServerList = serverList
        gameEntry.LastUpdate = Date.UtcNow
        serversByGame(gamename) = gameEntry

    End Sub

    Private Structure GameServerList
        Dim ServerList As List(Of String)
        Dim LastUpdate As DateTime
    End Structure


End Class
