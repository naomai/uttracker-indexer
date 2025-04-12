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
        Dim correctElapsedTime = IsNumeric(server.Info("elapsedtime")) AndAlso
               server.Info("elapsedtime") > 0

        Dim correctTimeLimit = IsNumeric(server.Info("timelimit")) AndAlso
            server.Info("timelimit") > 0 AndAlso
            (server.Info("timelimit") * 60) - server.Info("remainingtime") > 0

        Dim secondsElapsed As Integer = Nothing


        If correctElapsedTime Then
            secondsElapsed = server.Info("elapsedtime")
        ElseIf correctTimeLimit Then
            secondsElapsed = (server.Info("timelimit") * 60) - server.Info("remainingtime")
        Else
            secondsElapsed = Nothing
        End If

        Dim isNotStarted = (secondsElapsed = 0)
        If isNotStarted Then
            Return Date.UtcNow.AddYears(1)
        End If

        If Not IsNothing(secondsElapsed) Then
            Return server.PropsRequestTime.AddSeconds(-secondsElapsed)
        End If

        Return Nothing

    End Function
End Module
