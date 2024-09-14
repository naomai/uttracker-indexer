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

Imports System.Runtime.InteropServices
Imports System.Text
Imports System.IO

Public Class N14INI
    Public iniName As String

    Private Declare Function WritePrivateProfileString Lib "kernel32.dll" Alias "WritePrivateProfileStringA" (ByVal section As String, ByVal key As String, ByVal val As String, ByVal filePath As String) As Long
    Private Declare Function GetPrivateProfileString Lib "kernel32.dll" Alias "GetPrivateProfileStringA" (ByVal section As String, ByVal key As String, ByVal def As String, ByVal retVal As StringBuilder, ByVal size As Integer, ByVal filePath As String) As Integer

    Public Sub New(Optional ByVal sourceFile As String = Nothing)
        If IsNothing(sourceFile) Then
            sourceFile = System.Reflection.Assembly.GetEntryAssembly.GetName.Name & ".ini"
        End If

        Dim sourceFileReal = Path.GetFullPath(sourceFile)

        If Not Directory.Exists(Path.GetDirectoryName(sourceFileReal)) Then
            Throw New N14INIException("Invalid path")
        End If

        If Not File.Exists(sourceFileReal) Then
            File.Create(sourceFileReal).Dispose()
        End If

        Me.iniName = sourceFileReal
    End Sub

    Public Property Item(index As String) As String
        Get
            Return getProperty(index)
        End Get
        Set(value As String)
            setProperty(index, value)
        End Set

    End Property

    Public Function getProperty(prop As String, Optional defaultVal As String = "")
        Dim temp As New StringBuilder(255), val As String
        Dim propSplitted = Split(prop, ".", 2)
        Dim i As Integer = GetPrivateProfileString(propSplitted(0), propSplitted(1), Nothing, temp, 255, Me.iniName)
        val = temp.ToString()
        If val = "" AndAlso defaultVal <> "" Then
            setProperty(prop, defaultVal)
            Return defaultVal
        End If
        Return val
    End Function

    Public Sub setProperty(prop As String, value As String)
        Dim propSplitted = Split(prop, ".", 2)
        WritePrivateProfileString(propSplitted(0), propSplitted(1), value, Me.iniName)
    End Sub

End Class

Public Class N14INIException
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
