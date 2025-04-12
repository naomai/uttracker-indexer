Option Compare Text

Module MatchTimeEstimator


    ''' <summary>
    ''' Estimate match start time from server data 
    ''' </summary>
    ''' <returns>
    ''' On success: Date object in the past representing beginning of the match
    ''' When match is not yet started: Date object one year into the future
    ''' When beginning cannot be estimated: null
    ''' </returns>
    Public Function GetEstimatedMatchStartTime(server As ServerInfo) As Date?
        Dim secondsElapsed As Integer? = GetElapsedTime(server)

        Dim isNotStarted = secondsElapsed.HasValue AndAlso (secondsElapsed.Value = 0)
        If isNotStarted Then
            Return Date.UtcNow.AddYears(1)
        End If

        If Not IsNothing(secondsElapsed) Then
            Return server.PropsRequestTime.AddSeconds(-secondsElapsed)
        End If

        Return Nothing

    End Function

    Private Function GetElapsedTime(server As ServerInfo) As Integer?
        Dim secondsElapsed As Integer? = Nothing


        If HasValidElapsedTime(server) Then
            secondsElapsed = server.Info("elapsedtime")
        ElseIf HasValidTimeLimit(server) Then
            secondsElapsed = (server.Info("timelimit") * 60) - server.Info("remainingtime")
        ElseIf server.EstimatedMatchStart.HasValue AndAlso server.EstimatedMatchStart.Value < server.InfoRequestTime Then
            Return (server.InfoRequestTime - server.EstimatedMatchStart.Value).TotalSeconds
        Else
            secondsElapsed = Nothing
        End If

        Return secondsElapsed
    End Function

    Public Function GetEstimatedMatchEndTime(server As ServerInfo) As Date?
        Try
            Dim endTimes As New List(Of DateTime)
            Dim gameType As String = server.Info("gametype")
            Dim gameModeEnd As Date?

            If server.Players.Count = 0 Then
                Return Nothing
            End If


            If server.Info.ContainsKey("bgameended") AndAlso server.Info("bgameended") = "true" Then
                Return Nothing
            End If

            If gameType Like "*ctf*" Then
                gameModeEnd = EstimateCtfEnd(server)
            ElseIf gameType = "deathmatchplus" OrElse gameType = "idm" Then
                gameModeEnd = EstimateDmEnd(server)
            End If

            If gameModeEnd.HasValue Then endTimes.Add(gameModeEnd.Value)

            Dim timeoutEnd = EstimateTimeout(server)
            If timeoutEnd.HasValue Then endTimes.Add(timeoutEnd.Value)


            If endTimes.Count = 0 Then
                Return Nothing
            End If

            Return endTimes.Min()
        Catch e As Exception
            Return Nothing
        End Try
    End Function

    Private Function EstimateTimeout(server As ServerInfo) As Date?
        If HasValidTimeLimit(server) Then
            Dim remainingTime As Integer = Integer.Parse(server.Info("remainingtime"))
            Return server.PropsRequestTime.AddSeconds(remainingTime)
        End If
        Return Nothing
    End Function

    Private Function EstimateDmEnd(server As ServerInfo) As Date?

        Dim validator = UTQueryValidator.FromRuleDict(New Dictionary(Of String, String) From {
                     {"fraglimit", "required|integer"}
                    })
        Dim validated = validator.Validate(server.Info)

        Dim scoreGoal = validated("fraglimit")

        If scoreGoal <= 0 Then
            ' unlimited frags, skip
            Return Nothing
        End If

        Dim elapsedTime = GetElapsedTime(server)
        If Not elapsedTime.HasValue Then
            ' cannot estimate without any match time reference
            Return Nothing
        End If

        Dim scoreTop = server.Players.Max(Function(p) p("frags"))
        Dim progress = scoreTop / scoreGoal

        Dim estimatedMatchDuration = elapsedTime / progress
        If estimatedMatchDuration <= 0 Then
            Return Nothing
        End If
        Return server.EstimatedMatchStart.Value.AddSeconds(estimatedMatchDuration)
    End Function

    Private Function EstimateCtfEnd(server As ServerInfo) As Date?
        Try
            Dim validator = UTQueryValidator.FromRuleDict(New Dictionary(Of String, String) From {
                         {"goalteamscore", "required|integer"},
                         {"teamscore", "array:integer|default:0"},
                         {"score", "array:float|default:0"}
                        })

            Dim validated = validator.Validate(server.Info)
            Dim scoreGoal = validated("goalteamscore")
            If scoreGoal <= 0 Then
                ' unlimited flag captures, skip
                Return Nothing
            End If

            Dim elapsedTime = GetElapsedTime(server)
            If Not elapsedTime.HasValue Then
                ' cannot estimate without any match time reference
                Return Nothing
            End If

            Dim scoreList As UTQueryValidatorArray

            If validated("teamscore").Count > 0 Then
                scoreList = validated("teamscore")
            ElseIf validated("score").Count > 0 Then
                scoreList = validated("score")
            Else
                Return Nothing
            End If

            Dim scoreHighest = scoreList.Values.Max()
            Dim progress As Double = scoreHighest / scoreGoal

            Dim estimatedMatchDuration = elapsedTime / progress
            If estimatedMatchDuration <= 0 Then
                Return Nothing
            End If
            Return server.EstimatedMatchStart.Value.AddSeconds(estimatedMatchDuration)

        Catch e As UTQueryValidationException
            Return Nothing
        End Try
    End Function

    Private Function HasValidElapsedTime(server As ServerInfo) As Boolean
        Return server.Info.ContainsKey("elapsedtime") AndAlso
                IsNumeric(server.Info("elapsedtime")) AndAlso
                server.Info("elapsedtime") > 0

    End Function

    Private Function HasValidTimeLimit(server As ServerInfo) As Boolean
        Return server.Info.ContainsKey("timelimit") AndAlso
            server.Info.ContainsKey("remainingtime") AndAlso
            IsNumeric(server.Info("timelimit")) AndAlso
            IsNumeric(server.Info("remainingtime")) AndAlso
            server.Info("timelimit") > 0 AndAlso
            server.Info("remainingtime") > 0 AndAlso
            (server.Info("timelimit") * 60) - server.Info("remainingtime") > 0
    End Function

End Module
