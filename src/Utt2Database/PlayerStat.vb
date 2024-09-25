Imports System
Imports System.Collections.Generic
Imports Microsoft.VisualBasic

Namespace Utt2Database
    Partial Public Class PlayerStat
        Public Property Id As Long

        Public Property PlayerId As Long

        Public Property ServerId As Long

        Public Property GameTime As Integer

        Public Property Score As Long

        Public Property Deaths As Integer

        Public Property LastMatchId As Long

        Public Overridable Property LastMatch As ServerMatch

        Public Overridable Property Player As Player

        Public Overridable Property Server As Server
    End Class
End Namespace
