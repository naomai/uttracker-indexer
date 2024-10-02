Imports System.Collections.Immutable
Imports Naomai.UTT.Indexer.Utt2Database

Public Class ServerRating
	Private dbCtx As Utt2Context

	Public Sub New(ctx As Utt2Context)
		dbCtx = ctx
	End Sub

	Public Function CalculateMonthly(serverRecord As Server) As Integer
		Dim timeRange = Date.UtcNow.AddDays(-30)

		Dim totalPlayers = serverRecord.PlayerStats.Count ' pwuplayers
		Dim logs = serverRecord.PlayerLogs.
			Where(Function(l) l.FirstSeenTime > timeRange)

		Dim seenCount = logs.Sum(Function(s) s.SeenCount) ' records

		Dim rfScore As Single = Math.Log(1 + totalPlayers * (Math.Pow(seenCount, 2)) / 250) * 150.0

		Return Math.Round(rfScore)
	End Function

	Public Function CalculateMinute(serverVariables As Hashtable, serverRecord As Server)
		'$s['rfcombo']=round(pow($s['rfscore'],1.6)*($s['realnum']+1));

		Dim playerNum = Integer.Parse(serverVariables("numplayers"))

		If serverVariables.ContainsKey("__uttrealplayers") Then
			playerNum = Integer.Parse(serverVariables("__uttrealplayers"))
		End If
		Dim rfScore = serverRecord.RatingMonth

		Dim rfCombo As Single = Math.Pow(rfScore, 1.6) * (playerNum + 1)

		Return Math.Round(rfCombo)
	End Function
End Class
