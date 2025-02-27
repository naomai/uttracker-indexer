﻿Imports System.Threading
Imports System.Data
Imports System.Text.Json
Imports Naomai.UTT.Indexer.Utt2Database
Imports Org.BouncyCastle.Asn1.Cms
Imports Microsoft.EntityFrameworkCore.Internal

Public Class ServerInfoSync
    Protected serverWorker As ServerQuery
    Public dbCtx As Utt2Context

    Private serverData As ServerInfo

    Dim uttServerId As Int32
    Dim uttGameId As UInt32
    Dim uttServerScanTime As DateTime

    Public state As ServerScannerSaverState
    Private serverRecord As Server
    Private matchRecord As ServerMatch

    Public Sub New(serverData As ServerInfo, serverWorker As ServerQuery)
        Me.serverData = serverData
        Me.serverWorker = serverWorker
        dbCtx = serverWorker.scannerMaster.dbCtx
        GetServerRecord()
        serverRecord.LastCheck = Date.UtcNow
    End Sub

    Public Sub Tick()
        If state.done Then
            Return
        End If
        Try
            'If Not state.hasDBRecord Then prepareServerRecord()
            If Not state.savedInfo Then TryUpdateInfo()
            If Not state.savedVariables Then TryUpdateVariables()
            If Not state.savedGameInfo Then TryUpdateMatchInfo()
            If Not state.savedPlayers Then TryUpdatePlayerInfo()
            If Not state.savedCumulativeStats Then TryUpdateCumulativePlayersStats()
            If Not state.savedScanInfo Then UpdateCurrentScanInfo()
        Catch e As Exception
            serverWorker.abortScan("Tick Exception")
        End Try
        If state.savedInfo AndAlso state.savedVariables AndAlso state.savedGameInfo And state.savedPlayers And state.savedCumulativeStats And state.savedScanInfo Then
            state.done = True
        End If
    End Sub


    Public Function GetServerRecord() As Server
        If Not IsNothing(serverRecord) Then
            Return serverRecord
        End If

        serverRecord = dbCtx.Servers.Local.SingleOrDefault(Function(s) s.AddressQuery = serverWorker.addressQuery)

        If IsNothing(serverRecord) Then
            serverRecord = New Server() With {
                .AddressQuery = serverWorker.addressQuery,
                .AddressGame = serverWorker.addressGame
            }
        End If
        state.hasDBRecord = True

        ReadServerDataFromDB()

        Return serverRecord
    End Function

    Public Sub ReadServerDataFromDB()
        serverData.lastValidation = serverRecord.LastValidation
    End Sub

    Private Sub TryUpdateInfo()
        Dim scannerState = serverWorker.getState()

        If Not (state.hasDBRecord AndAlso scannerState.hasBasic AndAlso scannerState.hasInfo) Then
            Return
        End If

        With serverRecord
            .Name = serverData.info("hostname")
            .GameName = serverData.info("gamename")
            .AddressGame = serverWorker.addressGame
            .LastValidation = serverData.lastValidation
            .LastCheck = serverData.lastActivity
        End With

        dbCtx.Servers.Update(serverRecord)
        Try
            dbCtx.SaveChanges()
        Catch e As DbUpdateException
            ' conflict of AddressGame - one server, many QueryPorts
            Dim reason As String = "Database update fail - " & e.Message

            If e.InnerException.GetType() = GetType(MySqlException) Then
                Dim dbEx As MySqlException = e.InnerException
                If dbEx.Number = 1062 Then
                    reason = "One server-multiple query ports"
                End If
            End If
            serverWorker.abortScan(reason)
            dbCtx.Entry(serverRecord).State = EntityState.Detached
            Return
        End Try
        uttServerId = serverRecord.Id


        uttServerScanTime = serverWorker.infoSentTimeLocal

        state.hasServerId = True
        state.savedInfo = True
    End Sub

    Private Sub TryUpdateVariables()
        Dim variablesMerged As Hashtable
        Dim variablesJson As String
        Dim scannerState = serverWorker.getState()

        If Not serverData.caps.supportsVariables Then
            state.savedVariables = True
            Return
        End If

        If Not (state.hasServerId AndAlso scannerState.hasVariables) Then
            Return
        End If

        variablesMerged = serverData.variables.Clone()
        For Each infoItem In serverData.info.Keys
            variablesMerged(infoItem) = serverData.info(infoItem)
        Next

        'utt haxes:
        variablesMerged("__uttlastupdate") = UnixTime(uttServerScanTime)
        variablesMerged("queryport") = Split(serverWorker.addressQuery, ":").Last
        If serverData.caps.hasXSQ Then
            variablesMerged("__uttxserverquery") = "true"
        End If

        variablesMerged("__uttfakeplayers") = Int(serverData.caps.fakePlayers)

        variablesJson = JsonSerializer.Serialize(variablesMerged)

        serverRecord.Variables = variablesJson
        'dbCtx.Servers.Update(serverRecord)
        'dbCtx.SaveChanges()

        state.savedVariables = True

    End Sub

    Private Sub TryUpdateMatchInfo() ' serverhistory
        Dim previousMatchRecord As ServerMatch
        Dim scannerState = serverWorker.getState()
        Dim timeMatchStart As DateTime = Nothing


        If Not (state.hasServerId AndAlso scannerState.hasInfo) OrElse
            (serverData.caps.hasPropertyInterface AndAlso Not scannerState.hasInfoExtended) Then
            Return
        End If

        previousMatchRecord = GetLastMatchInfo()



        ' GameInfo.CurrentID
        ' As per UnrealWiki:
        '   used to assign unique PlayerIDs to each PlayerReplicationInfo
        ' This value is reset to 0 when a new match is started
        ' We can leverage this to detect new matches on one-map servers

        Dim thisMatchCurrentID As Int32? = Nothing, lastMatchCurrentID As Int32?
        Dim newMatchByCurrentIDChange As Boolean = False

        If scannerState.hasInfoExtended Then
            thisMatchCurrentID = Integer.Parse(serverData.info("__uttgamecurrentid"))
        End If

        If IsNothing(previousMatchRecord) Then
            state.isNewMatch = True
        Else
            lastMatchCurrentID = previousMatchRecord.ServerPlayeridCounter
            If scannerState.hasInfoExtended Then
                newMatchByCurrentIDChange =
                    serverData.info("numplayers") > 0 AndAlso
                    Not IsNothing(lastMatchCurrentID) AndAlso
                    Not IsNothing(thisMatchCurrentID) AndAlso
                    thisMatchCurrentID < lastMatchCurrentID AndAlso
                    previousMatchRecord.MapName = serverData.info("mapname")

            End If

            state.isNewMatch = state.isNewMatch OrElse newMatchByCurrentIDChange
        End If

        Dim trustingPlayerList =
            serverData.caps.timeTestPassed AndAlso
            serverData.info.ContainsKey("elapsedtime") AndAlso
            Not serverData.caps.fakePlayers


        If trustingPlayerList Then
            'timeGameStart = info("__uttgamestart")
            Dim correctElapsedTime = IsNumeric(serverData.info("elapsedtime")) AndAlso
               serverData.info("elapsedtime") > 0

            Dim correctTimeLimit = IsNumeric(serverData.info("timelimit")) AndAlso
                serverData.info("timelimit") > 0 AndAlso
                (serverData.info("timelimit") * 60) - serverData.info("remainingtime") > 60

            Dim secondsElapsed As Integer = Nothing


            If correctElapsedTime Then
                secondsElapsed = serverData.info("elapsedtime")
            ElseIf correctTimeLimit Then
            Else
                secondsElapsed = Nothing
            End If

            If Not IsNothing(secondsElapsed) Then
                timeMatchStart = serverWorker.firstTimeTestLocal.AddSeconds(-secondsElapsed)
            End If


            state.isNewMatch = state.isNewMatch OrElse
            (
                serverData.info("numplayers") > 0 AndAlso
                Not IsNothing(timeMatchStart) AndAlso
                previousMatchRecord.StartTime < timeMatchStart AndAlso
                Math.Abs((previousMatchRecord.StartTime - timeMatchStart).TotalSeconds) > 240
            ) OrElse
            previousMatchRecord.MapName <> serverData.info("mapname")
        Else
            state.isNewMatch = state.isNewMatch OrElse
            (
                serverData.info("numplayers") > 0 AndAlso
                (serverWorker.infoSentTimeLocal - previousMatchRecord.StartTime).TotalSeconds > 3600 * 4
            ) OrElse
            previousMatchRecord.MapName <> serverData.info("mapname")

            If state.isNewMatch Then
                timeMatchStart = serverWorker.infoSentTimeLocal
            Else
                timeMatchStart = previousMatchRecord.StartTime
            End If
        End If

        If state.isNewMatch Then
            If timeMatchStart = Nothing Then
                timeMatchStart = Date.UtcNow
            End If

            matchRecord = New ServerMatch With {
                .StartTime = timeMatchStart,
                .MapName = serverData.info("mapname"),
                .ServerPlayeridCounter = thisMatchCurrentID
            }
            serverRecord.ServerMatches.Add(matchRecord)
            If IsNothing(matchRecord.Id) Then
                dbCtx.SaveChanges()
            End If
        Else
            If Not IsNothing(thisMatchCurrentID) AndAlso thisMatchCurrentID > lastMatchCurrentID Then
                ' only update CurrenID in DB
                'matchRecord = dbCtx.ServerMatches.Single(Function(g) g.Id = thisMatchCurrentID)
                previousMatchRecord.ServerPlayeridCounter = thisMatchCurrentID
                'dbCtx.SaveChanges()
            End If
            matchRecord = previousMatchRecord
        End If
        uttGameId = matchRecord.Id
        state.savedGameInfo = True

    End Sub

    Private Sub TryUpdatePlayerInfo()
        Dim scannerState = serverWorker.getState()
        If scannerState.hasInfo AndAlso (serverData.info("numplayers") = 0 OrElse serverData.caps.fakePlayers) Then
            state.savedPlayers = True
            Return
        End If

        If scannerState.hasPlayers AndAlso state.savedGameInfo Then
            For Each player In serverData.players
                Dim uttPlayerSlug As String = GetPlayerSlug(player)
                Dim playerRecord As Player

                playerRecord = dbCtx.Players.SingleOrDefault(Function(p) p.Slug = uttPlayerSlug)

                player("uttSkinData") = player("mesh") & "|" & player("skin") & "|" & player("face")

                If IsNothing(playerRecord) Then
                    playerRecord = New Player With {
                                    .Name = player("name"),
                                    .Slug = uttPlayerSlug,
                                    .SkinData = player("uttSkinData")
                    }
                    'dbCtx.Players.Add(playerRecord)
                    'dbCtx.SaveChanges()
                End If

                player("uttPlayerSlug") = uttPlayerSlug

                UpdatePlayerInfoEntry(playerRecord, player)
                UpdatePlayerHistoryEntry(playerRecord, player)
            Next
            state.savedPlayers = True
            'dbCtx.SaveChanges()
        End If
    End Sub

    Private Sub UpdatePlayerInfoEntry(playerRecord As Player, playerData As Hashtable)
        Dim countryString As String = ""

        With playerRecord
            .SkinData = playerData("uttSkinData")
            If serverData.caps.hasXSQ And playerData("countryc") <> "none" Then
                countryString = playerData("countryc")
            End If
            .Country = countryString
        End With


        If IsNothing(playerRecord.Id) Then
            dbCtx.Players.Update(playerRecord)
            '    dbCtx.Players.Add(playerRecord)
            dbCtx.SaveChanges()
        End If
        playerData("uttPlayerId") = playerRecord.Id
    End Sub

    Private Sub UpdatePlayerHistoryEntry(playerRecord As Player, player As Hashtable) ' `playerhistory` table
        Dim playerTimeOffset As Integer = 0
        Dim playerLogRecord As PlayerLog
        Dim uttPlayerId As Int32 = player("uttPlayerId")

        If Not state.hasServerId Then
            Return
        End If

        If state.isNewMatch AndAlso player.ContainsKey("time") Then
            playerTimeOffset = -player("time")
        End If

        playerLogRecord = matchRecord.PlayerLogs.FirstOrDefault(
            Function(p) p.PlayerId = uttPlayerId
        )
        If IsNothing(playerLogRecord) Then
            playerLogRecord = New PlayerLog With {
                .ServerId = serverRecord.Id,
                .MatchId = matchRecord.Id,
                .FirstSeenTime = uttServerScanTime.AddSeconds(playerTimeOffset),
                .PlayerId = playerRecord.Id,
                .SeenCount = 0,
                .PingSum = 0
            }
            playerRecord.PlayerLogs.Add(playerLogRecord)
        ElseIf isnothing(playerLogRecord.Id) Then
            ' "MULTIPLE PLAYERS WITH SAME NAME"
            ' There are some weird servers where all spectators are named 'Player',
            ' which Indexer treats as one player entity.
            ' At this point, one local entity was already created a while ago,
            ' but not yet commited to DB, and further changes
            ' will break the state of Entity Framework.
            Return
        End If



        With playerLogRecord
            .LastSeenTime = uttServerScanTime
            .ScoreThisMatch = IIf(IsNumeric(player("frags")), player("frags"), "0")
            .DeathsThisMatch = IIf(serverData.caps.hasXSQ, Convert.ToInt32(player("deaths")), Nothing)
            .SeenCount += 1
            .PingSum += Integer.Parse(player("ping"))
            .Team = player("team")
        End With

        If IsNothing(playerLogRecord.Id) Then
            dbCtx.Update(playerLogRecord)
            'dbCtx.SaveChanges()
        End If



    End Sub

    Private Sub TryUpdateCumulativePlayersStats() ' update `PlayerStats` using not-finished records in PlayerLogs, then marking them Finished
        If Not state.hasServerId Then
            Return
        End If

        If state.savedPlayers AndAlso state.savedGameInfo Then
            Dim playerLogsDirty = serverRecord.PlayerLogs.Where(
                Function(l) l.Finished = False AndAlso l.MatchId <> uttGameId
            )

            'Dim playerStatsToUpdate = From stat In dbCtx.Set(Of PlayerStat)
            '                          Join log In playerLogsDirty
            '                              On stat.Player Equals log.Player And
            '                              stat.Server Equals log.Server
            '                          Select stat

            'If playerLogsDirty.Count > 0 Then Debugger.Break()
            For Each playerLog In playerLogsDirty.ToList()
                Dim playerStatRecord As PlayerStat = dbCtx.PlayerStats.SingleOrDefault(
                    Function(s) s.PlayerId = playerLog.PlayerId AndAlso
                    s.ServerId = playerLog.ServerId
                )
                'Dim playerStatRecord As PlayerStat = playerLog.Player.PlayerStats.SingleOrDefault(
                'Function(s) s.ServerId = playerLog.ServerId
                '                )

                If IsNothing(playerStatRecord) Then
                    playerStatRecord = New PlayerStat With {
                        .PlayerId = playerLog.PlayerId,
                        .ServerId = playerLog.ServerId,
                        .LastMatchId = playerLog.MatchId ' mandatory, db constraint
                    }
                    'dbCtx.PlayerStats.Add(playerStatRecord)
                    'dbCtx.SaveChanges()
                End If

                With playerStatRecord
                    Dim gameSeconds = (playerLog.LastSeenTime - playerLog.FirstSeenTime).TotalSeconds
                    If gameSeconds < 0 Then
                        Debugger.Break() ' trying to find the cause of "[UTT_ACHTUNG!Corrupted timespan]"
                        gameSeconds = 0

                    End If
                    .GameTime += gameSeconds
                    Dim deaths = playerLog.DeathsThisMatch
                    If Not IsNothing(deaths) Then
                        .Deaths += deaths
                    End If
                    .Score += playerLog.ScoreThisMatch
                    .LastMatchId = playerLog.MatchId
                End With


                playerLog.Finished = True
                dbCtx.Update(playerLog)
                If IsNothing(playerStatRecord.Id) Then
                    dbCtx.Update(playerStatRecord)
                    'dbCtx.SaveChanges()
                End If
            Next

            'dbCtx.SaveChanges()

            state.savedCumulativeStats = True
        End If
    End Sub

    Private Sub UpdateCurrentScanInfo()
        If Not state.hasServerId Then
            Return
        End If

        If state.savedCumulativeStats OrElse (state.savedInfo AndAlso serverData.info("numplayers") = 0) Then

            UpdateServerRatings()

            serverRecord.LastSuccess = DateTime.UtcNow
            serverRecord.LastCheck = serverData.lastActivity

            'dbCtx.Servers.Update(serverRecord)
            dbCtx.SaveChanges()

            state.savedScanInfo = True
        End If
    End Sub


    Private Sub UpdateServerRatings()
        Dim rfCalculator = New ServerRating(dbCtx)

        With serverRecord
            If IsNothing(.LastRatingCalculation) OrElse .LastRatingCalculation < Date.UtcNow.AddHours(-8) Then
                .RatingMonth = rfCalculator.CalculateMonthly(serverRecord)
                .LastRatingCalculation = Date.UtcNow
            End If
        End With
        serverRecord.RatingMinute = rfCalculator.CalculateMinute(serverData.info, serverRecord)
    End Sub

    Private Function GetLastMatchInfo() As ServerMatch
        Return serverRecord.ServerMatches.OrderByDescending(Function(m) m.Id).FirstOrDefault()
    End Function

    Private Shared Function GetPlayerSlug(playerInfo As Hashtable) As String
        ' For a rare events of two players having the same name, to tell them apart
        ' we append player skin name to the slug.
        ' The more complex names do not need this
        If (NameIsComplicated(playerInfo("name"))) Then
            GetPlayerSlug = LCase(playerInfo("name"))
        Else
            GetPlayerSlug = LCase(playerInfo("name") & "|" & playerInfo("mesh"))
        End If
    End Function

    Private Shared Function NameIsComplicated(pname As String) As Boolean
        Return Len(pname) >= 10 OrElse Text.RegularExpressions.Regex.IsMatch(pname, "[\[\]\(\)\{\}<>~`!@#\$%\^&\*\-=_/;:'"",\.\?]")
    End Function
End Class


Public Structure ServerScannerSaverState
    Dim hasDBRecord As Boolean
    Dim hasServerId As Boolean
    Dim savedInfo As Boolean
    Dim savedVariables As Boolean
    Dim savedGameInfo As Boolean
    Dim savedPlayers As Boolean
    Dim savedCumulativeStats As Boolean
    Dim savedScanInfo As Boolean
    Dim isNewMatch As Boolean
    Dim done As Boolean
End Structure