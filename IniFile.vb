' Nemo VBNET INI Class
' 
' 2014 namonaki14
' 
' [insert licensing trash here]
' 
' Changelog:
' 
' '15-02-26 Created
' 
' loosely based on N14\INI class from UTT PHP
' maybe todo: CONFIG_LOCK

Imports System.Text
Imports System.IO
Imports Microsoft.Extensions.FileProviders
Imports System.Reflection
Imports System.Diagnostics.Eventing

Public Class IniFile
    Public iniName As String

    Private Declare Function WritePrivateProfileString Lib "kernel32.dll" Alias "WritePrivateProfileStringA" (ByVal section As String, ByVal key As String, ByVal val As String, ByVal filePath As String) As Long
    Private Declare Function GetPrivateProfileString Lib "kernel32.dll" Alias "GetPrivateProfileStringA" (ByVal section As String, ByVal key As String, ByVal def As String, ByVal retVal As StringBuilder, ByVal size As Integer, ByVal filePath As String) As Integer

    Public Sub New(Optional ByVal sourceFile As String = Nothing)
        If IsNothing(sourceFile) Then
            sourceFile = System.Reflection.Assembly.GetEntryAssembly.GetName.Name & ".ini"
        End If

        Dim sourceFileReal = Path.GetFullPath(sourceFile)

        If Not Directory.Exists(Path.GetDirectoryName(sourceFileReal)) Then
            Throw New IniFileException("Invalid path")
        End If

        If Not File.Exists(sourceFileReal) Then
            CreateIniTemplate(sourceFileReal)
        End If

        Me.iniName = sourceFileReal
    End Sub

    Public Property Item(index As String) As String
        Get
            Return GetProperty(index)
        End Get
        Set(value As String)
            SetProperty(index, value)
        End Set

    End Property

    Public Function GetProperty(prop As String, Optional defaultVal As String = "")
        Dim temp As New StringBuilder(255), val As String
        Dim propSplitted = Split(prop, ".", 2)
        Dim section = propSplitted(0).Replace("|", ".")
        Dim i As Integer = GetPrivateProfileString(section, propSplitted(1), Nothing, temp, 255, Me.iniName)
        val = temp.ToString()
        If val = "" AndAlso defaultVal <> "" Then
            SetProperty(prop, defaultVal)
            Return defaultVal
        End If
        Return val
    End Function

    Public Sub SetProperty(prop As String, value As String)
        Dim propSplitted = Split(prop, ".", 2)
        Dim section = propSplitted(0).Replace("|", ".")
        WritePrivateProfileString(section, propSplitted(1), value, Me.iniName)
    End Sub

    Public Function PropertyExists(prop As String) As Boolean
        Dim temp As New StringBuilder(255), val As String
        Dim propSplitted = Split(prop, ".", 2)
        Dim section = propSplitted(0).Replace("|", ".")
        Dim i As Integer = GetPrivateProfileString(section, propSplitted(1), "__UTTNONEXISTINGIDX__", temp, 255, Me.iniName)
        val = temp.ToString()
        Return val <> "__UTTNONEXISTINGIDX__"
    End Function

    Private Sub CreateIniTemplate(dest As String)
        Dim bundledConfigProvider = New EmbeddedFileProvider(Assembly.GetExecutingAssembly(), "Naomai.UTT.ScannerV2")
        Dim bundledConfigFile = bundledConfigProvider.GetFileInfo("ConfigDist.ini")

        Dim destinationConfig = File.Create(dest)
        If bundledConfigFile.Exists Then
            Dim bundledConfigStream = bundledConfigFile.CreateReadStream()
            bundledConfigStream.CopyTo(destinationConfig)
        End If
        destinationConfig.Dispose()
    End Sub

End Class

Public Class IniFileException
    Inherits Exception

    Public Sub New()
    End Sub

    Public Sub New(message As String)
        MyBase.New(message)
    End Sub

    Public Sub New(message As String, inner As Exception)
        MyBase.New(message, inner)
    End Sub
End Class
