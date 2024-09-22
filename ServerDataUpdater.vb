Imports System.Threading
Imports System.Data
Imports System.Text.Json
Imports Naomai.UTT.ScannerV2.Utt2Database

Public Class ServerDataUpdater
    Protected serverWorker As ServerQuery
    Public dbCtx As Utt2Context
    Dim uttServerId As Int32
    Dim uttGameId As UInt32
    Dim uttServerScanTime As DateTime
    Public state As ServerScannerSaverState
    Private serverRecord As Server

    Public Sub New(serverWorker As ServerQuery)
        Me.serverWorker = serverWorker
        dbCtx = serverWorker.scannerMaster.dbCtx
    End Sub

    Public Sub tick()
        If state.done Then
            Return
        End If
        If Not state.hasDBRecord Then prepareServerRecord()
        If Not state.savedInfo Then tryUpdateInfo()
        If Not state.savedRules Then tryUpdateRules()
        If Not state.savedGameInfo Then TryUpdateMatchInfo()
        If Not state.savedPlayers Then tryUpdatePlayerInfo()
        If Not state.savedCumulativeStats Then tryUpdateCumulativePlayersStats()
        If Not state.savedScanInfo Then updateCurrentScanInfo()
        If state.savedInfo AndAlso state.savedRules AndAlso state.savedGameInfo And state.savedPlayers And state.savedCumulativeStats And state.savedScanInfo Then
            state.done = True
        End If
    End Sub


    Private Sub prepareServerRecord()
        serverRecord = dbCtx.Servers.SingleOrDefault(Function(s) s.Address = serverWorker.addressQuery)
        If IsNothing(serverRecord) Then
            serverRecord = New Server() With {
                .Address = serverWorker.addressQuery
            }
        End If
        state.hasDBRecord = True
    End Sub

    Private Sub tryUpdateInfo()
        Dim scannerState = serverWorker.getState()

        If Not (state.hasDBRecord AndAlso scannerState.hasBasic AndAlso scannerState.hasInfo) Then
            Return
        End If

        With serverRecord
            .Name = serverWorker.info("hostname")
            .GameName = serverWorker.info("gamename")
        End With

        dbCtx.Servers.Update(serverRecord)
        dbCtx.SaveChanges()

        uttServerId = serverRecord.Id
        uttServerScanTime = serverWorker.infoSentTimeLocal

        state.hasServerId = True
        state.savedInfo = True
    End Sub

    Private Sub tryUpdateRules()
        Dim rulesJoined As Hashtable
        Dim rulesJson As String
        Dim scannerState = serverWorker.getState()

        If Not serverWorker.caps.supportsRules Then
            state.savedRules = True
            Return
        End If

        If Not (state.hasServerId AndAlso scannerState.hasRules) Then
            Return
        End If

        rulesJoined = serverWorker.rules.Clone()
        For Each infoItem In serverWorker.info.Keys
            rulesJoined(infoItem) = serverWorker.info(infoItem)
        Next

        'utt haxes:
        rulesJoined("__uttlastupdate") = unixTime(uttServerScanTime)
        rulesJoined("queryport") = Split(serverWorker.addressQuery, ":").Last
        If serverWorker.caps.hasXSQ Then
            rulesJoined("__uttxserverquery") = "true"
        End If

        rulesJson = JsonSerializer.Serialize(rulesJoined)

        serverRecord.Rules = rulesJson
        dbCtx.Servers.Update(serverRecord)
        dbCtx.SaveChanges()

        state.savedRules = True

    End Sub

    Private Sub TryUpdateMatchInfo() ' serverhistory
        Dim previousMatchRecord As ServerMatch
        Dim scannerState = serverWorker.getState()
        Dim timeMatchStart As DateTime = Nothing


        If Not (state.hasServerId AndAlso scannerState.hasInfo) OrElse
            (serverWorker.caps.hasPropertyInterface AndAlso Not scannerState.hasInfoExtended) Then
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
            thisMatchCurrentID = Integer.Parse(serverWorker.info("__uttgamecurrentid"))
        End If

        If IsNothing(previousMatchRecord) Then
            state.isNewMatch = True
        Else
            lastMatchCurrentID = previousMatchRecord.ServerPlayeridCounter
            If scannerState.hasInfoExtended Then
                newMatchByCurrentIDChange =
                    serverWorker.info("numplayers") > 0 AndAlso
                    Not IsNothing(lastMatchCurrentID) AndAlso
                    Not IsNothing(thisMatchCurrentID) AndAlso
                    thisMatchCurrentID < lastMatchCurrentID AndAlso
                    previousMatchRecord.MapName = serverWorker.info("mapname")

            End If

            state.isNewMatch = state.isNewMatch OrElse newMatchByCurrentIDChange
        End If

        Dim trustingPlayerList =
            serverWorker.caps.timeTestPassed AndAlso
            serverWorker.info.ContainsKey("elapsedtime") AndAlso
            Not serverWorker.caps.fakePlayers


        If trustingPlayerList Then
            'timeGameStart = serverWorker.info("__uttgamestart")
            Dim correctElapsedTime = IsNumeric(serverWorker.info("elapsedtime")) AndAlso
                serverWorker.info("elapsedtime") > 0

            Dim correctTimeLimit = IsNumeric(serverWorker.info("timelimit")) AndAlso
                serverWorker.info("timelimit") > 0 AndAlso
                (serverWorker.info("timelimit") * 60) - serverWorker.info("remainingtime") > 60

            Dim secondsElapsed As Integer = Nothing


            If correctElapsedTime Then
                secondsElapsed = serverWorker.info("elapsedtime")
            ElseIf correctTimeLimit Then
                secondsElapsed = serverWorker.info("timelimit") * 60 - serverWorker.info("remainingtime")
            Else
                secondsElapsed = Nothing
            End If

            If Not IsNothing(secondsElapsed) Then
                timeMatchStart = serverWorker.firstTimeTestLocal.AddSeconds(-secondsElapsed)
            End If


            state.isNewMatch = state.isNewMatch OrElse
            (
                serverWorker.info("numplayers") > 0 AndAlso
                Not IsNothing(timeMatchStart) AndAlso
                previousMatchRecord.StartTime < timeMatchStart AndAlso
                Math.Abs((previousMatchRecord.StartTime - timeMatchStart).TotalSeconds) > 240
            ) OrElse
            previousMatchRecord.MapName <> serverWorker.info("mapname")
        Else
            state.isNewMatch = state.isNewMatch OrElse
            (
                serverWorker.info("numplayers") > 0 AndAlso
                (serverWorker.infoSentTimeLocal - previousMatchRecord.StartTime).TotalSeconds > 3600 * 4
            ) OrElse
            previousMatchRecord.MapName <> serverWorker.info("mapname")

            If state.isNewMatch Then
                timeMatchStart = serverWorker.infoSentTimeLocal
            Else
                timeMatchStart = previousMatchRecord.StartTime
            End If
        End If

        Dim matchRecord As ServerMatch

        If state.isNewMatch Then
            If timeMatchStart = Nothing Then
                timeMatchStart = Date.UtcNow
            End If

            matchRecord = New ServerMatch With {
            .ServerId = uttServerId,
            .StartTime = timeMatchStart,
            .MapName = serverWorker.info("mapname"),
            .ServerPlayeridCounter = thisMatchCurrentID
        }
            dbCtx.ServerMatches.Add(matchRecord)
            dbCtx.SaveChanges()
            uttGameId = matchRecord.Id
        Else
            If Not IsNothing(thisMatchCurrentID) AndAlso thisMatchCurrentID > lastMatchCurrentID Then
                ' only update CurrenID in DB
                'matchRecord = dbCtx.ServerMatches.Single(Function(g) g.Id = thisMatchCurrentID)
                previousMatchRecord.ServerPlayeridCounter = thisMatchCurrentID
                dbCtx.SaveChanges()
            End If
            uttGameId = previousMatchRecord.Id
        End If
        state.savedGameInfo = True

    End Sub

    Private Sub tryUpdatePlayerInfo()
        Dim scannerState = serverWorker.getState()
        If scannerState.hasInfo AndAlso (serverWorker.info("numplayers") = 0 OrElse serverWorker.caps.fakePlayers) Then
            state.savedPlayers = True
        End If

        If scannerState.hasPlayers AndAlso state.savedGameInfo Then
            For Each player In serverWorker.players
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
            If serverWorker.caps.hasXSQ And playerData("countryc") <> "none" Then
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

        If state.isNewMatch AndAlso player.ContainsKey("time") Then
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
            .DeathsThisMatch = IIf(serverWorker.caps.hasXSQ, Convert.ToInt32(player("deaths")), Nothing)
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

        If state.savedCumulativeStats OrElse (state.savedInfo AndAlso serverWorker.info("numplayers") = 0) Then
            serverRecord.LastScan = DateTime.UtcNow
            dbCtx.Servers.Update(serverRecord)
            dbCtx.SaveChanges()

            state.savedScanInfo = True
        End If
    End Sub

    Private Function GetLastMatchInfo() As ServerMatch
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


Public Structure ServerScannerSaverState
    Dim hasDBRecord As Boolean
    Dim hasServerId As Boolean
    Dim savedInfo As Boolean
    Dim savedRules As Boolean
    Dim savedGameInfo As Boolean
    Dim savedPlayers As Boolean
    Dim savedCumulativeStats As Boolean
    Dim savedScanInfo As Boolean
    Dim isNewMatch As Boolean
    Dim done As Boolean
End Structure