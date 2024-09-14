Imports System
Imports System.Collections.Generic
Imports Microsoft.VisualBasic

Namespace Utt2Database
    Partial Public Class PlayerLog
        Public Property Id As Long

        Public Property PlayerId As Integer

        Public Property ServerId As Integer

        Public Property MatchId As Integer

        Public Property SeenCount As Integer

        Public Property LastSeenTime As Date

        Public Property FirstSeenTime As Date

        Public Property ScoreThisMatch As Long

        Public Property DeathsThisMatch As Integer?

        Public Property PingSum As Integer

        Public Property Team As Integer
    End Class
End Namespace
