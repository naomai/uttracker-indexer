Imports System.Threading
Imports System.Data
Imports System.Text.Json
Imports Naomai.UTT.Indexer.Utt2Database
Imports Org.BouncyCastle.Asn1.Cms
Imports Microsoft.EntityFrameworkCore.Internal
Imports Naomai.UTT.Indexer.ServerQuery

Public Class ServerInfo
    Public info As New Hashtable
    Public players As New List(Of Hashtable)
    Public variables As New Hashtable
    Public caps As ServerCapabilities

    Public uttServerId As Int32
    Public uttGameId As UInt32
    Public uttServerScanTime As DateTime?
    Public lastActivity As DateTime?
    Public lastValidation As DateTime?


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

