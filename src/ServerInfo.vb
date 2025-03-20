
Public Class ServerInfo
    Public Info As New Dictionary(Of String, String)
    Public Players As New List(Of Dictionary(Of String, String))
    Public Variables As New Dictionary(Of String, String)
    Public Capabilities As ServerCapabilities

    Public LastActivityTime As DateTime?
    Public LastValidationTime As DateTime?

    Public PropsRequestTime, InfoRequestTime As DateTime?


    Public Sub New()

    End Sub

End Class


Public Structure ServerCapabilities
    Dim version As String
    Dim gameName As String
    Dim hasXSQ As Boolean
    Dim XSQVersion As Integer
    Dim hasPropertyInterface As Boolean
    Dim timeTestPassed As Boolean
    Dim gameSpeed As Single
    Dim supportsVariables As Boolean
    Dim gamemodeExtendedInfo As Boolean
    Dim fakePlayers As Boolean
    Dim hasUtf8PlayerList As Boolean ' UT 469+
    Dim hasCp437Info As Boolean ' Unreal
    Dim quickNumPlayers As Boolean ' depends on hasPropertyInterface

    Public Overrides Function ToString() As String
        ToString = "ServerCapabilities{ "
        If gameName <> "" Then ToString &= "isOnline gameName=" & gameName & " version=" & version & " "
        If hasXSQ Then ToString &= "hasXSQ=" & XSQVersion & " "
        If hasPropertyInterface Then ToString &= "hasPropertyInterface "
        ToString &= "}"
    End Function
End Structure

