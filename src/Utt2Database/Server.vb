Imports System
Imports System.Collections.Generic
Imports Microsoft.VisualBasic

Namespace Utt2Database
    Partial Public Class Server
        Public Property Id As Long?

        Public Property Address As String

        Public Property Name As String

        Public Property Rules As String

        Public Property LastCheck As Date?

        Public Property LastSuccess As Date?

        Public Property LastValidation As Date?

        Public Property LastRankCalculation As Date?

        Public Property RfScore As Integer

        Public Property Country As String

        Public Property GameName As String

        Public Overridable Property PlayerLogs As ICollection(Of PlayerLog) = New List(Of PlayerLog)()

        Public Overridable Property PlayerStats As ICollection(Of PlayerStat) = New List(Of PlayerStat)()

        Public Overridable Property ServerMatches As ICollection(Of ServerMatch) = New List(Of ServerMatch)()
    End Class
End Namespace
