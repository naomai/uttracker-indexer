Imports System.Data
Imports System.Text.Json
Imports Naomai.UTT.Indexer.Utt2Database

Public Class ServerInfoSync
    Protected serverWorker As ServerQuery
    Public dbCtx As Utt2Context

    Private serverData As ServerInfo

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
            If Not state.savedInfo Then TryUpdateInfo()
            If Not state.savedVariables Then TryUpdateVariables()
            If Not state.savedGameInfo Then TryUpdateMatchInfo()
            If Not state.savedPlayers Then TryUpdatePlayerInfo()
            If Not state.savedCumulativeStats Then TryUpdateCumulativePlayersStats()
            If Not state.savedScanInfo Then UpdateCurrentScanInfo()
        Catch e As Exception
            serverWorker.abortScan("Tick Exception: " & e.Message)
        End Try
        If state.savedInfo AndAlso state.savedVariables AndAlso state.savedGameInfo And state.savedPlayers And state.savedCumulativeStats And state.savedScanInfo Then
            state.done = True
        End If
    End Sub


    Public Function GetServerRecord() As Server
        If Not IsNothing(serverRecord) AndAlso state.hasDBRecord Then
            Return serverRecord
        End If

        Dim records = serverWorker.scannerMaster.serverRecords

        If Not records.ContainsKey(serverWorker.addressQuery) Then
            serverRecord = New Server() With {
                .AddressQuery = serverWorker.addressQuery,
                .AddressGame = serverWorker.addressGame
            }
            records.Add(serverWorker.addressQuery, serverRecord)
        Else
            serverRecord = records(serverWorker.addressQuery)
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
            If IsNothing(serverRecord.Id) Then
                dbCtx.SaveChanges() ' UpdateInfo
            End If
        Catch e As DbUpdateException
            ' conflict of AddressGame - one server, many QueryPorts
            Dim reason As String = "Database update fail - " & e.Message

            If e.InnerException.GetType() = GetType(MySqlException) Then
                Dim dbEx As MySqlException = e.InnerException
                If dbEx.Number = 1062 AndAlso dbEx.Message.Contains("address_game") Then
                    reason = "One server-multiple query ports: " & dbEx.Message
                    serverWorker.isActive = False
                End If
            End If
            serverWorker.abortScan(reason)
            dbCtx.Entry(serverRecord).State = EntityState.Detached
            Return
        End Try

        uttServerScanTime = serverWorker.infoSentTimeLocal

        state.hasServerId = True
        state.savedInfo = True
        state.savedScanInfo = False
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

        variablesMerged = New Hashtable(serverData.variables)
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

        state.savedVariables = True
        state.savedScanInfo = False
    End Sub

    Private Sub TryUpdateMatchInfo() ' serverhistory
        Dim previousMatchRecord As ServerMatch
        Dim scannerState = serverWorker.getState()
        Dim timeMatchStart As DateTime? = Nothing

        state.isNewMatch = False
        state.isPreMatch = False

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

        Dim changedMapName = False
        Dim hasPlayers = serverData.info("__uttrealplayers") > 0

        If scannerState.hasInfoExtended Then
            thisMatchCurrentID = Integer.Parse(serverData.info("__uttgamecurrentid"))
        End If

        If IsNothing(previousMatchRecord) Then
            state.isNewMatch = True
        Else
            lastMatchCurrentID = previousMatchRecord.ServerPlayeridCounter
            changedMapName = previousMatchRecord.MapName <> serverData.info("mapname")
            If scannerState.hasInfoExtended Then
                newMatchByCurrentIDChange =
                    Not IsNothing(lastMatchCurrentID) AndAlso
                    Not IsNothing(thisMatchCurrentID) AndAlso
                    thisMatchCurrentID < lastMatchCurrentID AndAlso
                    changedMapName

            End If

            state.isNewMatch = state.isNewMatch OrElse newMatchByCurrentIDChange
        End If

        Dim trustingPlayerList =
            serverData.info.ContainsKey("elapsedtime") AndAlso
            Not serverData.caps.fakePlayers


        If trustingPlayerList Then
            timeMatchStart = DetectMatchStart()

            state.isPreMatch = timeMatchStart.HasValue AndAlso timeMatchStart.Value > Date.UtcNow

            Dim isPreMatchContinuing = Not state.isNewMatch _
                    AndAlso state.isPreMatch _
                    AndAlso previousMatchRecord.StartTime > Date.UtcNow

            Dim hasPreMatchEnded As Boolean = Not state.isNewMatch _
                AndAlso timeMatchStart.HasValue _
                AndAlso previousMatchRecord.StartTime > Date.UtcNow _
                AndAlso Not state.isPreMatch

            ' if server provides the game times, we'll take them into account
            Dim newMatchByReportedTime As Boolean = Not state.isNewMatch _
                AndAlso timeMatchStart.HasValue _
                AndAlso hasPlayers _
                AndAlso previousMatchRecord.StartTime < timeMatchStart.Value _
                AndAlso Not isPreMatchContinuing _
                AndAlso Math.Abs((previousMatchRecord.StartTime - timeMatchStart.Value).TotalSeconds) > 600 ' jitter

            ' if not, we'll assume the longest match time on one map of 6 hours
            Dim newMatchByEstimatedlTimeout As Boolean = Not state.isNewMatch _
                AndAlso Not timeMatchStart.HasValue _
                AndAlso hasPlayers _
                AndAlso Not changedMapName _
                AndAlso previousMatchRecord.StartTime.AddHours(6) < serverWorker.infoSentTimeLocal

            state.isNewMatch = state.isNewMatch _
                OrElse newMatchByReportedTime _
                OrElse newMatchByEstimatedlTimeout _
                OrElse changedMapName
        Else
            state.isNewMatch = state.isNewMatch OrElse
            (
                hasPlayers AndAlso
                previousMatchRecord.StartTime.AddHours(6) < serverWorker.infoSentTimeLocal
            ) OrElse
            changedMapName

            If state.isNewMatch Then
                timeMatchStart = serverWorker.infoSentTimeLocal
            Else
                timeMatchStart = previousMatchRecord.StartTime
            End If
        End If

        If state.isNewMatch Then
            If Not timeMatchStart.HasValue Then
                timeMatchStart = Date.UtcNow
            End If

            Dim previousMatchHadPlayers = Not IsNothing(previousMatchRecord) AndAlso previousMatchRecord.PlayerLogs.Count <> 0

            If IsNothing(previousMatchRecord) OrElse previousMatchHadPlayers Then
                ' Create new match record
                matchRecord = New ServerMatch
                serverRecord.ServerMatches.Add(matchRecord)
            Else
                ' Repurpose previously created record, we don't want to keep
                ' empty server runs
                matchRecord = previousMatchRecord
            End If



            With matchRecord
                .StartTime = timeMatchStart.Value
                .MapName = serverData.info("mapname")
                .ServerPlayeridCounter = thisMatchCurrentID
            End With

        Else
            matchRecord = previousMatchRecord

            If Not IsNothing(thisMatchCurrentID) AndAlso thisMatchCurrentID > lastMatchCurrentID Then
                ' only update CurrenID in DB
                matchRecord.ServerPlayeridCounter = thisMatchCurrentID
            End If

            If Math.Abs((matchRecord.StartTime - timeMatchStart.Value).TotalMinutes) > 10 Then
                ' update match start time if changed significantly (pre-match ended)
                matchRecord.StartTime = timeMatchStart.Value
            End If
        End If

        state.savedGameInfo = True
        state.savedScanInfo = False

    End Sub

    ''' <summary>
    ''' Estimate match start time from server data 
    ''' </summary>
    ''' <returns>
    ''' On success: Date object in the past representing beginning of the match
    ''' When match is not yet started: Date object one year into the future
    ''' When beginning cannot be estimated: null
    ''' </returns>
    Private Function DetectMatchStart() As Date?
        Dim correctElapsedTime = IsNumeric(serverData.info("elapsedtime")) AndAlso
               serverData.info("elapsedtime") > 0

        Dim correctTimeLimit = IsNumeric(serverData.info("timelimit")) AndAlso
            serverData.info("timelimit") > 0 AndAlso
            (serverData.info("timelimit") * 60) - serverData.info("remainingtime") > 0

        Dim secondsElapsed As Integer = Nothing


        If correctElapsedTime Then
            secondsElapsed = serverData.info("elapsedtime")
        ElseIf correctTimeLimit Then
            secondsElapsed = (serverData.info("timelimit") * 60) - serverData.info("remainingtime")
        Else
            secondsElapsed = Nothing
        End If

        Dim isNotStarted = (secondsElapsed = 0)
        If isNotStarted Then
            Return Date.UtcNow.AddYears(1)
        End If

        If Not IsNothing(secondsElapsed) Then
            Return serverWorker.firstTimeTestLocal.AddSeconds(-secondsElapsed)
        End If

        Return Nothing

    End Function

    Private Sub TryUpdatePlayerInfo()
        Dim scannerState = serverWorker.getState()
        If scannerState.hasInfo AndAlso (serverData.info("__uttrealplayers") = 0 OrElse serverData.caps.fakePlayers) Then
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
            state.savedScanInfo = False
            'dbCtx.SaveChanges()
        End If
    End Sub

    Private Sub UpdatePlayerInfoEntry(playerRecord As Player, playerData As Dictionary(Of String, String))
        Dim countryString As String = ""

        With playerRecord
            .SkinData = playerData("uttSkinData")
            If serverData.caps.hasXSQ And playerData("countryc") <> "none" Then
                countryString = playerData("countryc")
            End If
            .Country = countryString
        End With


        If IsNothing(playerRecord.Id) Then
            dbCtx.Players.Add(playerRecord)
            dbCtx.SaveChanges() ' PlayerInfo
        End If
        playerData("uttPlayerId") = playerRecord.Id
    End Sub

    Private Sub UpdatePlayerHistoryEntry(playerRecord As Player, player As Dictionary(Of String, String)) ' `playerhistory` table
        Dim playerTimeOffset As Integer = 0
        Dim playerLogRecord As PlayerLog

        If Not state.hasServerId Then
            Return
        End If

        If state.isNewMatch AndAlso player.ContainsKey("time") Then
            playerTimeOffset = -player("time")
        End If

        playerLogRecord = matchRecord.PlayerLogs.FirstOrDefault(
            Function(p) p.PlayerId.Equals(playerRecord.Id) OrElse
                (Not IsNothing(p.Player) AndAlso p.Player.Equals(playerRecord))
        )
        If IsNothing(playerLogRecord) Then
            playerLogRecord = New PlayerLog With {
                .Server = serverRecord,
                .Match = matchRecord,
                .FirstSeenTime = uttServerScanTime.AddSeconds(playerTimeOffset),
                .Player = playerRecord,
                .SeenCount = 0,
                .PingSum = 0
            }
            playerRecord.PlayerLogs.Add(playerLogRecord)
        ElseIf IsNothing(playerLogRecord.Id) Then
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
        End If
    End Sub

    Private Sub TryUpdateCumulativePlayersStats() ' update `PlayerStats` using not-finished records in PlayerLogs, then marking them Finished
        If Not state.hasServerId Then
            Return
        End If

        If state.savedPlayers AndAlso state.savedGameInfo Then

            Dim playerLogsDirty As IEnumerable(Of PlayerLog)

            If Not IsNothing(matchRecord.Id) Then
                playerLogsDirty = dbCtx.Entry(serverRecord) _
                    .Collection(Function(r) r.PlayerLogs) _
                    .Query() _
                    .Where(
                        Function(l) l.Finished = False AndAlso l.MatchId <> matchRecord.Id
                    ) _
                    .OrderBy(Function(l) l.PlayerId) _
                    .ToList()
            Else
                playerLogsDirty = dbCtx.Entry(serverRecord) _
                    .Collection(Function(r) r.PlayerLogs) _
                    .Query() _
                    .Where(
                        Function(l) l.Finished = False
                    ) _
                    .OrderBy(Function(l) l.PlayerId) _
                    .ToList()
            End If

            If playerLogsDirty.Count = 0 Then
                state.savedCumulativeStats = True
                Return
            End If

            Dim logPlayers = playerLogsDirty.Select(Of Integer)(Function(l) l.PlayerId).Distinct().ToList()

            dbCtx.PlayerStats.Where(Function(s) logPlayers.Contains(s.PlayerId)).Load()


            For Each playerLog In playerLogsDirty
                Dim playerStatRecord As PlayerStat = dbCtx.PlayerStats.Local.SingleOrDefault(
                    Function(s) s.PlayerId = playerLog.PlayerId AndAlso
                    s.ServerId = playerLog.ServerId
                )

                If IsNothing(playerStatRecord) Then
                    playerStatRecord = New PlayerStat With {
                        .Player = playerLog.Player,
                        .PlayerId = playerLog.PlayerId,
                        .Server = playerLog.Server,
                        .ServerId = playerLog.ServerId,
                        .LastMatch = playerLog.Match, ' mandatory, db constraint
                        .LastMatchId = playerLog.MatchId
                    }
                    dbCtx.PlayerStats.Add(playerStatRecord)
                    dbCtx.PlayerStats.Local.Add(playerStatRecord)
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
            Next

            Try
                dbCtx.SaveChanges() 'PlayerStats
            Catch e As Exception
            End Try

            state.savedCumulativeStats = True
        End If
    End Sub

    Private Sub UpdateCurrentScanInfo()
        If Not state.hasServerId Then
            Return
        End If

        If state.savedCumulativeStats OrElse (state.savedInfo AndAlso serverData.info("__uttrealplayers") = 0) Then

            UpdateServerRatings()

            serverRecord.LastSuccess = DateTime.UtcNow
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
        Dim match = serverRecord.ServerMatches.OrderByDescending(Function(m) m.Id).FirstOrDefault()
        If IsNothing(match) Then
            Return Nothing
        End If
        dbCtx.Entry(match) _
            .Collection(Function(m) m.PlayerLogs) _
            .Load()
        Return match
    End Function

    Private Shared Function GetPlayerSlug(playerInfo As Dictionary(Of String, String)) As String
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
    Dim isPreMatch As Boolean
    Dim done As Boolean
End Structure