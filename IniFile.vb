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
Imports Microsoft.Extensions.Configuration.Ini

Public Class IniFile
    Public iniName As String
    Public iniProvider As IniConfigurationProvider

    Public Sub New(Optional ByVal sourceFile As String = Nothing)
        If IsNothing(sourceFile) Then
            sourceFile = System.Reflection.Assembly.GetEntryAssembly.GetName.Name & ".ini"
            If Not File.Exists(sourceFile) Then
                CreateIniTemplate(sourceFile)
            End If
        End If

        Dim sourceFileReal = Path.GetFullPath(sourceFile)
        Dim sourceFileDir = Path.GetDirectoryName(sourceFileReal)
        Dim sourceFileName = Path.GetFileName(sourceFileReal)

        Dim iniSrc = New IniConfigurationSource() With {
            .FileProvider = New PhysicalFileProvider(sourceFileDir),
            .Path = sourceFileName
        }
        iniProvider = New IniConfigurationProvider(iniSrc)
        iniProvider.Load()

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
        Dim result As String = Nothing

        Dim propertyAccessor = GetPropertyAccessorString(prop)
        Dim hasValue As Boolean = iniProvider.TryGet(propertyAccessor, result)

        If Not hasValue AndAlso defaultVal <> "" Then
            SetProperty(prop, defaultVal)
            Return defaultVal
        End If
        Return result
    End Function

    Public Sub SetProperty(prop As String, value As String)
        Dim propertyAccessor = GetPropertyAccessorString(prop)
        iniProvider.Set(propertyAccessor, value)
    End Sub

    Public Function PropertyExists(prop As String) As Boolean
        Dim propertyAccessor = GetPropertyAccessorString(prop)
        Dim hasValue As Boolean = iniProvider.TryGet(propertyAccessor, Nothing)
        Return hasValue
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

    Protected Shared Function GetPropertyAccessorString(propertyString As String) As String
        Dim propertyChunks = Split(propertyString, ".", 2)
        Dim sectionName = propertyChunks(0).Replace("|", ".")
        Dim propertyName = propertyChunks(1)
        Return sectionName & ":" & propertyName
    End Function

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
