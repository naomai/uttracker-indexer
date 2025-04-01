
Public Class ServerInfo
    Public Info As New Dictionary(Of String, String)
    Public Players As New List(Of Dictionary(Of String, String))
    Public Variables As New Dictionary(Of String, String)
    Public Capabilities As ServerCapabilities

    Public LastActivityTime As DateTime?
    Public LastValidationTime As DateTime?
    Public EstimatedMatchStart As DateTime?

    Public PropsRequestTime, InfoRequestTime As DateTime

    Public AddressQuery As String
    Public AddressGame As String
    Public GameVersion As String
    Public GameName As String

    Public State As ServerInfoState


    Public Sub New()

    End Sub

End Class

Public Structure ServerInfoState

    Dim HasBasic As Boolean
    Dim HasInfo As Boolean
    Dim HasNumPlayers As Boolean
    Dim HasTeams As Boolean
    Dim HasInfoExtended As Boolean

    Dim HasPlayers As Boolean
    Dim HasVariables As Boolean
End Structure

Public Structure ServerCapabilities

    Dim HasXsq As Boolean
    Dim XsqVersion As Integer
    Dim HasPropertyInterface As Boolean
    Dim SupportsVariables As Boolean
    Dim GamemodeExtendedInfo As Boolean
    Dim FakePlayers As Boolean
    Dim HasUtf8PlayerList As Boolean ' UT 469+
    Dim HasCp437Info As Boolean ' Unreal
    Dim QuickNumPlayers As Boolean ' depends on hasPropertyInterface
    Dim CompoundRequest As Boolean ' chaining multiple requests like \info\\players\

    Public Overrides Function ToString() As String
        ToString = "ServerCapabilities{ "
        If HasXsq Then ToString &= "hasXSQ=" & XsqVersion & " "
        If HasPropertyInterface Then ToString &= "hasPropertyInterface "
        ToString &= "}"
    End Function
End Structure

