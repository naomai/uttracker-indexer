Imports System.Data
Imports System.Text.Json
Imports Naomai.UTT.Indexer.Utt2Database

Public Class ServerDataPersistence
    ''' <summary>
    ''' Transfers obtained server info into database entities
    ''' </summary>
    Private dbCtx As Utt2Context
    Private serverRepo As ServerRepository
    Private serverDto As ServerInfo

    Dim uttServerScanTime As DateTime

    Private state As ServerDataPersistenceState
    Private serverRecord As Server
    Private matchRecord As ServerMatch

    Public Event OnSyncComplete()

    Public Sub New(serverData As ServerInfo, context As Utt2Context, repo As ServerRepository)
        Me.serverDto = serverData
        dbCtx = context
        serverRepo = repo
        GetServerRecord()
        serverRecord.LastCheck = Date.UtcNow
    End Sub

    ''' <summary>
    ''' Performs pending sync operations 
    ''' </summary>
    Public Sub Tick()
        If state.Done Then
            Return
        End If
        If Not state.SavedInfo Then TryUpdateInfo()
        If Not state.SavedVariables Then TryUpdateVariables()
        If Not state.SavedMatchInfo Then TryUpdateMatchInfo()
        If Not state.SavedPlayers Then TryUpdatePlayerInfo()
        If Not state.SavedCumulativeStats Then TryUpdateCumulativePlayersStats()
        If Not state.SavedScanInfo Then UpdateCurrentScanInfo()

        If state.SavedInfo AndAlso state.SavedVariables AndAlso state.SavedMatchInfo And state.SavedPlayers And state.SavedCumulativeStats And state.SavedScanInfo Then
            FinishSync()
        End If
    End Sub


    Public Function GetServerRecord() As Server
        If Not IsNothing(serverRecord) AndAlso state.HasDbRecord Then
            Return serverRecord
        End If

        serverRecord = serverRepo.GetServerByQueryAddress(serverDto.AddressQuery)

        If IsNothing(serverRecord) Then
            serverRecord = New Server() With {
                .AddressQuery = serverDto.AddressQuery,
                .AddressGame = serverDto.AddressGame
            }
            serverRepo.AddServer(serverRecord)
        End If
        state.HasDbRecord = True

        FillServerDtoFromRecord()

        Return serverRecord
    End Function

    Protected Sub FillServerDtoFromRecord()
        serverDto.LastValidationTime = serverRecord.LastValidation
    End Sub

    Public Sub InvalidateServerRecord()
        state.HasDbRecord = False
    End Sub

    Public Sub InvalidateInfo()
        state.SavedInfo = False
        state.SavedMatchInfo = False
        ResumeSync()
    End Sub
    Public Sub InvalidatePlayers()
        state.SavedPlayers = False
        state.SavedCumulativeStats = False
        ResumeSync()
    End Sub
    Public Sub InvalidateVariables()
        state.SavedVariables = False
        ResumeSync()
    End Sub

    Protected Sub ResumeSync()
        state.Done = False
    End Sub
    Public Sub FinishSync()
        state.Done = True
        RaiseEvent OnSyncComplete()
    End Sub

    Public Function IsSyncInProgress()
        Return Not state.Done
    End Function

#Region "Sync logic"


    Private Sub TryUpdateInfo()
        Dim dataState = serverDto.State

        If Not (state.HasDbRecord AndAlso dataState.HasBasic AndAlso dataState.HasInfo) Then
            Return
        End If

        With serverRecord
            .Name = serverDto.Info("hostname")
            .GameName = serverDto.Info("gamename")
            .AddressGame = serverDto.AddressGame
            .LastValidation = serverDto.LastValidationTime
            .LastCheck = serverDto.LastActivityTime
        End With

        dbCtx.Servers.Update(serverRecord)
        Try
            If IsNothing(serverRecord.Id) Then
                dbCtx.SaveChanges() ' UpdateInfo
            End If
        Catch e As DbUpdateException
            ' conflict of AddressGame - one server, many QueryPorts
            Dim reason As String = "Database update failed - " & e.Message
            Dim fatal As Boolean = False

            If e.InnerException.GetType() = GetType(MySqlException) Then
                Dim dbEx As MySqlException = e.InnerException
                If dbEx.Number = 1062 AndAlso dbEx.Message.Contains("address_game") Then
                    reason = "One server-multiple query ports: " & dbEx.Message
                    fatal = True
                End If
            End If
            dbCtx.Entry(serverRecord).State = EntityState.Detached
            Throw New ScanException(reason, fatal, inner:=e)
            Return
        End Try

        uttServerScanTime = serverDto.InfoRequestTime

        state.HasServerId = True
        state.SavedInfo = True
        state.SavedScanInfo = False
    End Sub

    Private Sub TryUpdateVariables()
        Dim variablesMerged As Hashtable
        Dim variablesJson As String
        Dim dataState = serverDto.State

        If Not serverDto.Capabilities.SupportsVariables Then
            state.SavedVariables = True
            Return
        End If

        If Not (state.HasServerId AndAlso dataState.HasVariables) Then
            Return
        End If

        variablesMerged = New Hashtable(serverDto.Variables)
        For Each infoItem In serverDto.Info.Keys
            variablesMerged(infoItem) = serverDto.Info(infoItem)
        Next

        'utt haxes:
        variablesMerged("__uttlastupdate") = UnixTime(uttServerScanTime)
        variablesMerged("queryport") = Split(serverDto.AddressQuery, ":").Last
        If serverDto.Capabilities.HasXsq Then
            variablesMerged("__uttxserverquery") = "true"
        End If

        variablesMerged("__uttfakeplayers") = Int(serverDto.Capabilities.FakePlayers)

        variablesJson = JsonSerializer.Serialize(variablesMerged)

        serverRecord.Variables = variablesJson

        state.SavedVariables = True
        state.SavedScanInfo = False
    End Sub

    Private Sub TryUpdateMatchInfo() ' serverhistory
        Dim previousMatchRecord As ServerMatch
        Dim dataState = serverDto.State
        Dim timeMatchStart As DateTime? = Nothing

        state.IsNewMatch = False
        state.IsPreMatch = False

        If Not (state.HasServerId AndAlso dataState.HasInfo) OrElse
            (serverDto.Capabilities.HasPropertyInterface AndAlso Not dataState.HasInfoExtended) Then
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
        Dim hasPlayers = serverDto.Info("__uttrealplayers") > 0

        If dataState.HasInfoExtended Then
            thisMatchCurrentID = Integer.Parse(serverDto.Info("__uttgamecurrentid"))
        End If

        If IsNothing(previousMatchRecord) Then
            state.IsNewMatch = True
        Else
            lastMatchCurrentID = previousMatchRecord.ServerPlayeridCounter
            changedMapName = previousMatchRecord.MapName <> serverDto.Info("mapname")
            If dataState.HasInfoExtended Then
                newMatchByCurrentIDChange =
                    Not IsNothing(lastMatchCurrentID) AndAlso
                    Not IsNothing(thisMatchCurrentID) AndAlso
                    thisMatchCurrentID < lastMatchCurrentID AndAlso
                    changedMapName

            End If

            state.IsNewMatch = state.IsNewMatch OrElse newMatchByCurrentIDChange
        End If

        Dim trustingPlayerList =
            serverDto.Info.ContainsKey("elapsedtime") AndAlso
            Not serverDto.Capabilities.FakePlayers


        If trustingPlayerList Then
            timeMatchStart = serverDto.EstimatedMatchStart

            state.IsPreMatch = timeMatchStart.HasValue AndAlso timeMatchStart.Value > Date.UtcNow

            Dim isPreMatchContinuing = Not state.IsNewMatch _
                    AndAlso state.IsPreMatch _
                    AndAlso previousMatchRecord.StartTime > Date.UtcNow

            Dim hasPreMatchEnded As Boolean = Not state.IsNewMatch _
                AndAlso timeMatchStart.HasValue _
                AndAlso previousMatchRecord.StartTime > Date.UtcNow _
                AndAlso Not state.IsPreMatch

            ' if server provides the game times, we'll take them into account
            Dim newMatchByReportedTime As Boolean = Not state.IsNewMatch _
                AndAlso timeMatchStart.HasValue _
                AndAlso hasPlayers _
                AndAlso previousMatchRecord.StartTime < timeMatchStart.Value _
                AndAlso Not isPreMatchContinuing _
                AndAlso Math.Abs((previousMatchRecord.StartTime - timeMatchStart.Value).TotalSeconds) > 600 ' jitter

            ' if not, we'll assume the longest match time on one map of 6 hours
            Dim newMatchByEstimatedlTimeout As Boolean = Not state.IsNewMatch _
                AndAlso Not timeMatchStart.HasValue _
                AndAlso hasPlayers _
                AndAlso Not changedMapName _
                AndAlso previousMatchRecord.StartTime.AddHours(6) < serverDto.InfoRequestTime

            state.IsNewMatch = state.IsNewMatch _
                OrElse newMatchByReportedTime _
                OrElse newMatchByEstimatedlTimeout _
                OrElse changedMapName
        Else
            state.IsNewMatch = state.IsNewMatch OrElse
            (
                hasPlayers AndAlso
                previousMatchRecord.StartTime.AddHours(6) < serverDto.InfoRequestTime
            ) OrElse
            changedMapName

            If state.IsNewMatch Then
                timeMatchStart = serverDto.InfoRequestTime
            Else
                timeMatchStart = previousMatchRecord.StartTime
            End If
        End If

        If state.IsNewMatch Then
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
                .MapName = serverDto.Info("mapname")
                .ServerPlayeridCounter = thisMatchCurrentID
            End With

        Else
            matchRecord = previousMatchRecord

            If Not IsNothing(thisMatchCurrentID) AndAlso thisMatchCurrentID > lastMatchCurrentID Then
                ' only update CurrenID in DB
                matchRecord.ServerPlayeridCounter = thisMatchCurrentID
            End If

            If timeMatchStart.HasValue AndAlso
                Math.Abs((matchRecord.StartTime - timeMatchStart.Value).TotalMinutes) > 10 Then
                ' update match start time if changed significantly (pre-match ended)
                matchRecord.StartTime = timeMatchStart.Value
            End If
        End If

        If Not serverDto.EstimatedMatchStart.HasValue Then
            serverDto.EstimatedMatchStart = matchRecord.StartTime
        End If

        state.SavedMatchInfo = True
        state.SavedScanInfo = False

    End Sub


    Private Sub TryUpdatePlayerInfo()
        Dim dataState = serverDto.State
        If dataState.HasInfo AndAlso (serverDto.Info("__uttrealplayers") = 0 OrElse serverDto.Capabilities.FakePlayers) Then
            state.SavedPlayers = True
            Return
        End If

        If dataState.HasPlayers AndAlso state.SavedMatchInfo Then
            For Each player In serverDto.Players
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
            state.SavedPlayers = True
            state.SavedScanInfo = False
            'dbCtx.SaveChanges()
        End If
    End Sub

    Private Sub UpdatePlayerInfoEntry(playerRecord As Player, playerData As Dictionary(Of String, String))
        Dim countryString As String = ""

        With playerRecord
            .SkinData = playerData("uttSkinData")
            If serverDto.Capabilities.HasXsq AndAlso Not IsNothing(playerData("countryc")) AndAlso playerData("countryc") <> "none" Then
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

        If Not state.HasServerId Then
            Return
        End If

        If state.IsNewMatch AndAlso player.ContainsKey("time") Then
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
            .ScoreThisMatch = player("frags")
            .DeathsThisMatch = player("deaths")
            .SeenCount += 1
            .PingSum += Integer.Parse(player("ping"))
            .Team = player("team")
        End With

        If IsNothing(playerLogRecord.Id) Then
            dbCtx.Update(playerLogRecord)
        End If
    End Sub

    Private Sub TryUpdateCumulativePlayersStats() ' update `PlayerStats` using not-finished records in PlayerLogs, then marking them Finished
        If Not state.HasServerId Then
            Return
        End If

        If state.SavedPlayers AndAlso state.SavedMatchInfo Then

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
                state.SavedCumulativeStats = True
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

            state.SavedCumulativeStats = True
        End If
    End Sub

    Private Sub UpdateCurrentScanInfo()
        If Not state.HasServerId Then
            Return
        End If

        If state.SavedCumulativeStats OrElse (state.SavedInfo AndAlso serverDto.Info("__uttrealplayers") = 0) Then

            UpdateServerRatings()

            serverRecord.LastSuccess = DateTime.UtcNow
            state.SavedScanInfo = True
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
        serverRecord.RatingMinute = rfCalculator.CalculateMinute(serverDto.Info, serverRecord)
    End Sub

#End Region

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

    Private Structure ServerDataPersistenceState
        Dim HasDbRecord As Boolean
        Dim HasServerId As Boolean
        Dim SavedInfo As Boolean
        Dim SavedVariables As Boolean
        Dim SavedMatchInfo As Boolean
        Dim SavedPlayers As Boolean
        Dim SavedCumulativeStats As Boolean
        Dim SavedScanInfo As Boolean
        Dim IsNewMatch As Boolean
        Dim IsPreMatch As Boolean
        Dim Done As Boolean
    End Structure
End Class
