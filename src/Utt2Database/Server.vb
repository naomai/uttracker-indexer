Imports System
Imports System.Collections.Generic
Imports Microsoft.VisualBasic

Namespace Utt2Database
    Partial Public Class Server
        Public Property Id As Long?

        Public Property AddressQuery As String

        Public Property AddressGame As String

        Public Property Name As String

        Public Property Variables As String

        Public Property LastCheck As Date?

        Public Property LastSuccess As Date?

        Public Property LastValidation As Date?

        Public Property LastRatingCalculation As Date?

        Public Property RatingMonth As Integer

        Public Property RatingMinute As Integer

        Public Property Country As String

        Public Property GameName As String

        Public Overridable Property PlayerLogs As ICollection(Of PlayerLog) = New List(Of PlayerLog)()

        Public Overridable Property PlayerStats As ICollection(Of PlayerStat) = New List(Of PlayerStat)()

        Public Overridable Property ServerMatches As ICollection(Of ServerMatch) = New List(Of ServerMatch)()
    End Class
End Namespace
