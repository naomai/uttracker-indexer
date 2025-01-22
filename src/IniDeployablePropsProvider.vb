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

Imports System.IO
Imports Microsoft.Extensions.FileProviders
Imports System.Reflection

Public Class IniDeployablePropsProvider
    Inherits IniPropsProvider

    Public Sub New(Optional ByVal sourceFile As String = Nothing)
        MyBase.New(sourceFile)
        If IsNothing(sourceFile) Then
            sourceFile = Assembly.GetEntryAssembly.GetName.Name & ".ini"
            If Not File.Exists(sourceFile) Then
                CreateIniTemplate(sourceFile)
            End If
        End If
        LoadFile(sourceFile)

    End Sub

    Public Property Item(index As String) As String
        Get
            Return GetProperty(index)
        End Get
        Set(value As String)
            SetProperty(index, value)
        End Set

    End Property

    Public Shared Sub CreateIniTemplate(dest As String)
        Dim bundledConfigProvider = New EmbeddedFileProvider(Assembly.GetExecutingAssembly(), "Naomai.UTT.Indexer")
        Dim bundledConfigFile = bundledConfigProvider.GetFileInfo("ConfigDist.ini")

        Dim destinationConfig = File.Create(dest)
        If bundledConfigFile.Exists Then
            Dim bundledConfigStream = bundledConfigFile.CreateReadStream()
            bundledConfigStream.CopyTo(destinationConfig)
        End If
        destinationConfig.Dispose()
    End Sub

End Class

