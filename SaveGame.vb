Imports System.Threading
Imports System.Data
Imports System.Text.Json
Imports Naomai.UTT.ScannerV2.Utt2Database

Public Class SaveGame
    Protected scannerSlave As ServerScannerWorker
    Public dbCtx As Utt2Context
    Dim uttServerId As Int32
    Dim uttGameId As UInt32
    Dim uttServerScanTime As DateTime
    Public state As SaveGameWorkerState
    Private serverRecord As Server

    Public Sub New(scannerSlave As ServerScannerWorker)
        Me.scannerSlave = scannerSlave
        dbCtx = scannerSlave.scannerMaster.dbCtx
    End Sub

    Public Sub tick()
        Dim scannerState = scannerSlave.getState()
        If Not state.done Then
            If Not state.hasDBRecord Then prepareServerRecord()
            If Not state.savedInfo Then tryUpdateInfo()
            If Not state.savedRules Then tryUpdateRules()
            If Not state.savedGameInfo Then tryUpdateGameInfo()
            If Not state.savedPlayers Then tryUpdatePlayerInfo()
            If Not state.savedCumulativeStats Then tryUpdateCumulativePlayersStats()
            If Not state.savedScanInfo Then updateCurrentScanInfo()
            If state.savedInfo AndAlso state.savedRules AndAlso state.savedGameInfo And state.savedPlayers And state.savedCumulativeStats And state.savedScanInfo Then
                state.done = True
            End If
        End If
    End Sub

    Private Sub prepareServerRecord()
        If Not IsNothing(serverRecord) Then
            Return
        End If

        Try
            serverRecord = dbCtx.Servers.Single(Function(s) s.Address = scannerSlave.address)
        Catch e As InvalidOperationException
            serverRecord = New Server() With {
                .Address = scannerSlave.address
            }
        End Try
        state.hasDBRecord = True
    End Sub

    Private Sub tryUpdateInfo()
        Dim scannerState = scannerSlave.getState()

        If state.hasDBRecord AndAlso scannerState.hasBasic AndAlso scannerState.hasInfo Then
            uttServerScanTime = scannerSlave.infoSentTimeLocal

            With serverRecord
                .Name = scannerSlave.info("hostname")
                .GameName = scannerSlave.info("gamename")
            End With

            dbCtx.Servers.Update(serverRecord)
            dbCtx.SaveChanges()

            uttServerId = serverRecord.Id
            state.hasServerId = True

            state.savedInfo = True
        End If
    End Sub

    Private Sub tryUpdateRules()
        Dim rulesJoined As Hashtable
        Dim rulesJson As String
        Dim scannerState = scannerSlave.getState()

        If Not state.hasServerId Then
            Return
        End If

        If Not scannerSlave.caps.supportsRules Then
            state.savedRules = True
            Return
        End If

        If scannerState.hasRules Then
            rulesJoined = scannerSlave.rules.Clone()
            For Each infoItem In scannerSlave.info.Keys
                rulesJoined(infoItem) = scannerSlave.info(infoItem)
            Next

            'utt haxes:
            rulesJoined("__uttlastupdate") = unixTime(uttServerScanTime)
            rulesJoined("queryport") = Split(scannerSlave.address, ":").Last
            If scannerSlave.caps.hasXSQ Then
                rulesJoined("__uttxserverquery") = "true"
            End If

            rulesJson = JsonSerializer.Serialize(rulesJoined)

            serverRecord.Rules = rulesJson
            dbCtx.Servers.Update(serverRecord)
            dbCtx.SaveChanges()

            state.savedRules = True
        End If
    End Sub

    Private Sub tryUpdateGameInfo() ' serverhistory
        Dim lastGameRecord As ServerMatch

        If Not state.hasServerId Then
            Return
        End If

        Dim scannerState = scannerSlave.getState()
        If scannerState.hasInfo Then
            lastGameRecord = getLastGameInfo()

            Dim timeGameStart As DateTime, thisGameCurrentID As Int32, lastGameCurrentID As Int32
            If IsNothing(lastGameRecord) Then
                state.isNewGame = True
            Else
                lastGameCurrentID = lastGameRecord.InternalMatchId
                If scannerState.hasInfoExtended Then
                    thisGameCurrentID = scannerSlave.info("__uttgamecurrentid")
                Else
                    thisGameCurrentID = -1
                End If

                state.isNewGame =
                    state.isNewGame OrElse
                    (
                        scannerSlave.info("numplayers") > 0 AndAlso
                        lastGameCurrentID <> -1 AndAlso
                        thisGameCurrentID <> -1 AndAlso
                        thisGameCurrentID < lastGameCurrentID AndAlso
                        lastGameRecord.MapName = scannerSlave.info("mapname")
                    )
            End If


            If scannerSlave.caps.timeTestPassed AndAlso scannerSlave.info.ContainsKey("elapsedtime") AndAlso Not scannerSlave.caps.fakePlayers Then
                'timeGameStart = scannerSlave.info("__uttgamestart")
                If IsNumeric(scannerSlave.info("elapsedtime")) AndAlso
                    (
                        scannerSlave.info("elapsedtime") > 0 OrElse
                        (scannerSlave.info("timelimit") * 60) - scannerSlave.info("remainingtime") < 60
                    ) Then
                    timeGameStart = scannerSlave.firstTimeTestLocal.AddSeconds(-scannerSlave.info("elapsedtime"))
                ElseIf IsNumeric(scannerSlave.info("timelimit")) AndAlso
                    scannerSlave.info("timelimit") > 0 AndAlso
                    (scannerSlave.info("timelimit") * 60) - scannerSlave.info("remainingtime") > 60 Then ' elapsed time not implemented in gamemode?
                    timeGameStart = scannerSlave.firstTimeTestLocal.AddSeconds(-((scannerSlave.info("timelimit") * 60) - scannerSlave.info("remainingtime")))
                Else
                    timeGameStart = Nothing
                End If
                state.isNewGame = state.isNewGame OrElse
                    (
                        scannerSlave.info("numplayers") > 0 AndAlso
                        Not IsNothing(timeGameStart) AndAlso
                        lastGameRecord.StartTime < timeGameStart AndAlso
                        Math.Abs((lastGameRecord.StartTime - timeGameStart).TotalSeconds) > 240
                    ) OrElse
                    lastGameRecord.MapName <> scannerSlave.info("mapname")
            Else
                state.isNewGame = state.isNewGame OrElse
                    (
                        scannerSlave.info("numplayers") > 0 AndAlso
                        (scannerSlave.infoSentTimeLocal - lastGameRecord.StartTime).TotalSeconds > 3600 * 4
                    ) OrElse
                    lastGameRecord.MapName <> scannerSlave.info("mapname")

                If state.isNewGame Then
                    timeGameStart = scannerSlave.infoSentTimeLocal
                Else
                    timeGameStart = lastGameRecord.StartTime
                End If
            End If

            Dim matchRecord As ServerMatch

            If state.isNewGame Then
                If timeGameStart = Nothing Then timeGameStart = Date.UtcNow

                matchRecord = New ServerMatch With {
                    .ServerId = uttServerId,
                    .StartTime = timeGameStart,
                    .MapName = scannerSlave.info("mapname"),
                    .InternalMatchId = thisGameCurrentID
                }
                dbCtx.ServerMatches.Add(matchRecord)
                dbCtx.SaveChanges()
                uttGameId = matchRecord.Id
            Else
                If thisGameCurrentID <> -1 AndAlso thisGameCurrentID > lastGameCurrentID Then
                    matchRecord = dbCtx.ServerMatches.Single(Function(g) g.Id = thisGameCurrentID)

                    matchRecord.InternalMatchId = thisGameCurrentID
                    dbCtx.SaveChanges()
                End If
                uttGameId = lastGameRecord.Id
            End If
            state.savedGameInfo = True
        End If
    End Sub

    Private Sub tryUpdatePlayerInfo()
        Dim scannerState = scannerSlave.getState()
        If scannerState.hasInfo AndAlso (scannerSlave.info("numplayers") = 0 OrElse scannerSlave.caps.fakePlayers) Then
            state.savedPlayers = True
        End If

        If scannerState.hasPlayers AndAlso state.savedGameInfo Then
            For Each player In scannerSlave.players
                Dim uttPlayerSlug As String = getPlayerSlug(player)
                Dim playerRecord As Player

                playerRecord = dbCtx.Players.SingleOrDefault(Function(p) p.Slug = uttPlayerSlug)
                If IsNothing(playerRecord) Then
                    playerRecord = New Player
                End If

                player("uttPlayerSlug") = uttPlayerSlug

                updatePlayerInfoEntry(playerRecord, player)
                updatePlayerHistoryEntry(player)
            Next
            state.savedPlayers = True
        End If
    End Sub

    Private Sub updatePlayerInfoEntry(playerRecord As Player, playerData As Hashtable)
        Dim countryString As String = ""

        With playerRecord
            .Name = playerData("name")
            .Slug = playerData("uttPlayerSlug")
            .SkinData = playerData("mesh") & "|" & playerData("skin") & "|" & playerData("face")
            If scannerSlave.caps.hasXSQ And playerData("countryc") <> "none" Then
                countryString = playerData("countryc")
            End If
            .Country = countryString
        End With

        dbCtx.Players.Update(playerRecord)
        dbCtx.SaveChanges()

        playerData("uttPlayerId") = playerRecord.Id
    End Sub

    Private Sub updatePlayerHistoryEntry(player As Hashtable) ' `playerhistory` table
        Dim playerTimeOffset As Integer = 0
        Dim playerLogRecord As PlayerLog
        Dim uttPlayerId As Int32 = player("uttPlayerId")

        If Not state.hasServerId Then
            Return
        End If

        If state.isNewGame AndAlso player.ContainsKey("time") Then
            playerTimeOffset = -player("time")
        End If

        playerLogRecord = dbCtx.PlayerLogs.SingleOrDefault(
            Function(p) p.PlayerId = uttPlayerId And p.MatchId = uttGameId
        )
        If IsNothing(playerLogRecord) Then
            playerLogRecord = New PlayerLog With {
                .PlayerId = player("uttPlayerId"),
                .ServerId = uttServerId,
                .MatchId = uttGameId,
                .FirstSeenTime = uttServerScanTime.AddSeconds(playerTimeOffset),
                .SeenCount = 0,
                .PingSum = 0
            }

        End If

        With playerLogRecord
            .LastSeenTime = uttServerScanTime
            .ScoreThisMatch = IIf(IsNumeric(player("frags")), player("frags"), "0")
            .DeathsThisMatch = IIf(scannerSlave.caps.hasXSQ, Convert.ToInt32(player("deaths")), Nothing)
            .SeenCount += 1
            .PingSum += player("ping")
            .Team = player("team")
        End With

        dbCtx.PlayerLogs.Update(playerLogRecord)
        dbCtx.SaveChanges()

    End Sub

    Private Sub tryUpdateCumulativePlayersStats() ' update `PlayerStats` using not-finished records in PlayerLogs, then marking them Finished
        If Not state.hasServerId Then
            Return
        End If

        If state.savedPlayers AndAlso state.savedGameInfo Then
            Dim playerLogsDirty = dbCtx.PlayerLogs.Where(
                Function(l) l.Finished = False AndAlso l.ServerId = uttServerId AndAlso l.MatchId <> uttGameId
            ).ToList()

            For Each playerLog In playerLogsDirty
                Dim playerStatRecord As PlayerStat = dbCtx.PlayerStats.SingleOrDefault(
                    Function(s) s.PlayerId = playerLog.PlayerId AndAlso s.ServerId = playerLog.ServerId
                )

                If IsNothing(playerStatRecord) Then
                    playerStatRecord = New PlayerStat With {
                        .PlayerId = playerLog.PlayerId,
                        .ServerId = playerLog.ServerId
                    }
                End If

                With playerStatRecord
                    Dim gameSeconds = (playerLog.LastSeenTime - playerLog.FirstSeenTime).TotalSeconds
                    If gameSeconds < 0 Then
                        Debugger.Break() ' trying to find the cause of "[UTT_ACHTUNG!Corrupted timespan]"
                        gameSeconds = 0

                    End If
                    .GameTime = gameSeconds
                    Dim deaths = playerLog.DeathsThisMatch
                    If Not IsNothing(deaths) Then
                        .Deaths += deaths
                    End If
                    .Score += playerLog.ScoreThisMatch
                    .LastMatchId = playerLog.MatchId
                End With

                dbCtx.PlayerStats.Update(playerStatRecord)
                playerLog.Finished = True
            Next
            dbCtx.SaveChanges()

            state.savedCumulativeStats = True
        End If
    End Sub

    Private Sub updateCurrentScanInfo()
        If Not state.hasServerId Then
            Return
        End If

        If state.savedCumulativeStats OrElse (state.savedInfo AndAlso scannerSlave.info("numplayers") = 0) Then
            serverRecord.LastScan = DateTime.UtcNow
            dbCtx.Servers.Update(serverRecord)
            dbCtx.SaveChanges()

            state.savedScanInfo = True
        End If
    End Sub

    Private Function getLastGameInfo() As ServerMatch
        Return dbCtx.ServerMatches.OrderByDescending(Function(m) m.Id).FirstOrDefault(
            Function(m) m.ServerId = uttServerId)
    End Function

    Private Shared Function getPlayerSlug(playerInfo As Hashtable) As String
        ' For a rare events of two players having the same name, to tell them apart
        ' we append player skin name to the slug.
        ' The more complex names do not need this
        If (nameIsComplicated(playerInfo("name"))) Then
            getPlayerSlug = LCase(playerInfo("name"))
        Else
            getPlayerSlug = LCase(playerInfo("name") & "|" & playerInfo("mesh"))
        End If
    End Function

    Private Shared Function nameIsComplicated(pname As String) As Boolean
        Return Len(pname) >= 10 OrElse Text.RegularExpressions.Regex.IsMatch(pname, "[\[\]\(\)\{\}<>~`!@#\$%\^&\*\-=_/;:'"",\.\?]")
    End Function
End Class


Public Structure SaveGameWorkerState
    Dim hasDBRecord As Boolean
    Dim hasServerId As Boolean
    Dim savedInfo As Boolean
    Dim savedRules As Boolean
    Dim savedGameInfo As Boolean
    Dim savedPlayers As Boolean
    Dim savedCumulativeStats As Boolean
    Dim savedScanInfo As Boolean
    Dim isNewGame As Boolean
    Dim done As Boolean
End Structure