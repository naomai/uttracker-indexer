Imports System
Imports System.Collections.Generic
Imports Microsoft.VisualBasic

Namespace Utt2Database
    Partial Public Class ConfigProp
        Public Property Key As String

        Public Property Data As String

        Public Property [Private] As Boolean

        Public Overrides Function ToString() As String
            Return Me.Key
        End Function
    End Class
End Namespace
