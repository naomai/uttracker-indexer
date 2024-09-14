Imports System
Imports System.Collections.Generic
Imports Microsoft.VisualBasic

Namespace Utt2Database
    Partial Public Class Server
        Public Property Id As Long

        Public Property Address As String

        Public Property Name As String

        Public Property Rules As String

        Public Property LastScan As Date?

        Public Property LastRankCalculation As Date?

        Public Property RfScore As Integer

        Public Property Country As String

        Public Property GameName As String
    End Class
End Namespace
