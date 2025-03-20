
Public Class ServerInfo
    Public Info As New Dictionary(Of String, String)
    Public Players As New List(Of Dictionary(Of String, String))
    Public Variables As New Dictionary(Of String, String)
    Public Capabilities As ServerCapabilities

    Public LastActivityTime As DateTime?
    Public LastValidationTime As DateTime?

    Public PropsRequestTime, InfoRequestTime As DateTime


    Public Sub New()

    End Sub

End Class


Public Structure ServerCapabilities
    Dim GameVersion As String
    Dim GameName As String
    Dim HasXsq As Boolean
    Dim XsqVersion As Integer
    Dim HasPropertyInterface As Boolean
    Dim TimeTestPassed As Boolean
    Dim GameSpeed As Single
    Dim SupportsVariables As Boolean
    Dim GamemodeExtendedInfo As Boolean
    Dim FakePlayers As Boolean
    Dim HasUtf8PlayerList As Boolean ' UT 469+
    Dim HasCp437Info As Boolean ' Unreal
    Dim QuickNumPlayers As Boolean ' depends on hasPropertyInterface

    Public Overrides Function ToString() As String
        ToString = "ServerCapabilities{ "
        If GameName <> "" Then ToString &= "isOnline gameName=" & GameName & " version=" & GameVersion & " "
        If HasXsq Then ToString &= "hasXSQ=" & XsqVersion & " "
        If HasPropertyInterface Then ToString &= "hasPropertyInterface "
        ToString &= "}"
    End Function
End Structure

