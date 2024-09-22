Imports System
Imports System.Collections.Generic
Imports Microsoft.VisualBasic

Namespace Utt2Database
    Partial Public Class ServerMatch
        Public Property Id As Long

        Public Property ServerId As Long

        Public Property StartTime As Date

        Public Property MapName As String

        Public Property ServerPlayeridCounter As Integer?

        Public Overridable Property PlayerLogs As ICollection(Of PlayerLog) = New List(Of PlayerLog)()

        Public Overridable Property PlayerStats As ICollection(Of PlayerStat) = New List(Of PlayerStat)()

        Public Overridable Property Server As Server
    End Class
End Namespace
