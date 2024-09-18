Imports System
Imports System.Collections.Generic
Imports Microsoft.VisualBasic

Namespace Utt2Database
    Partial Public Class Player
        Public Property Id As Long

        Public Property Slug As String

        Public Property Name As String

        Public Property SkinData As String

        Public Property Country As String

        Public Overridable Property PlayerLogs As ICollection(Of PlayerLog) = New List(Of PlayerLog)()

        Public Overridable Property PlayerStats As ICollection(Of PlayerStat) = New List(Of PlayerStat)()
    End Class
End Namespace
