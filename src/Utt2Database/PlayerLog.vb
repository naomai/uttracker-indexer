Imports System
Imports System.Collections.Generic
Imports Microsoft.VisualBasic

Namespace Utt2Database
    Partial Public Class PlayerLog
        Public Property Id As Long?

        Public Property PlayerId As Long

        Public Property ServerId As Long

        Public Property MatchId As Long

        Public Property SeenCount As Integer

        Public Property LastSeenTime As Date

        Public Property FirstSeenTime As Date

        Public Property ScoreThisMatch As Long

        Public Property DeathsThisMatch As Integer?

        Public Property PingSum As Integer

        Public Property Team As Integer

        Public Property Finished As Boolean

        Public Overridable Property Match As ServerMatch

        Public Overridable Property Player As Player

        Public Overridable Property Server As Server

        Public Overrides Function ToString() As String
            Return Player.Name & "#M" & MatchId & "@S" & ServerId
        End Function
    End Class
End Namespace
