' DeVlog - VBNET logging class
' 2014 Namonaki14
Imports System.IO
Imports System.Environment
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options
Imports Google.Protobuf.Reflection
Imports Microsoft.Extensions.Logging.Console
Imports Microsoft.Extensions.Logging.Abstractions
Imports Google.Protobuf.WellKnownTypes
Imports System.Runtime.Intrinsics
Imports System.Runtime.InteropServices

Public Enum LoggerLevel
    out = 1
    err = 2
    debug = 4
End Enum

Public Class LoggingAdapter
    Implements IDisposable

    Private progName As String = System.Reflection.Assembly.GetEntryAssembly.GetName.Name
    Protected fileHandle As FileStream
    Public errorStream As TextWriter = System.Console.Error
    Public coutStream As TextWriter = System.Console.Out
    Protected disposed As Boolean = False
    Private benchStartTime As DateTime

    Public printToFile As Boolean = True
    Public autoFlush As Boolean = True
    Public fileLoggingLevel As Integer = (LoggerLevel.out Or LoggerLevel.err)
    Public consoleLoggingLevel As Integer = (LoggerLevel.out Or LoggerLevel.err)

    Private _loggerLegacy As ILogger

    Private _logFactory As ILoggerFactory


#Region "constructor"

    Public Sub New(Optional ByVal logname As String = "")
        If (logname = "") Then logname = progName & ".log"
        fileHandle = File.Open(logname, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read)
        fileHandle.SetLength(0)

        _logFactory =
            LoggerFactory.Create(Function(builder)
                                     Return builder.AddConsole(Sub(options)
                                                                   options.FormatterName = "UTT1LogFormatter"
                                                               End Sub
                                      ).AddConsoleFormatter(Of UTTLegacyFormatter, ConsoleFormatterOptions)() _
                                     .SetMinimumLevel(LogLevel.Debug)
                                 End Function)
        _loggerLegacy = _logFactory.CreateLogger("Program")
    End Sub

    Public Function CreateLogger(loggerName As String) As ILogger
        Return _logFactory.CreateLogger(loggerName)
    End Function

    Protected Overridable Sub Dispose(ByVal disposing As Boolean)
        If Not Me.disposed Then
            If disposing Then
                fileHandle.Flush()
                fileHandle.Close()
                errorStream.Dispose()
                coutStream.Dispose()
            End If
        End If
        Me.disposed = True
    End Sub

#End Region

#Region "writing functions"
    Public Sub ErrorWriteLine(ByVal value As String)
        _loggerLegacy.LogError(value)
    End Sub

    Public Sub ErrorWriteLine(ByVal format As String, ByVal ParamArray arg As Object())
        _loggerLegacy.LogError(format, arg)
    End Sub

    Public Sub DebugWriteLine(ByVal value As String)
        _loggerLegacy.LogDebug(value)
    End Sub

    Public Sub DebugWriteLine(ByVal format As String, ByVal ParamArray arg As Object())
        _loggerLegacy.LogDebug(format, arg)
    End Sub
    Public Sub WriteLine(ByVal value As String)
        _loggerLegacy.LogInformation(value)
    End Sub

    Public Sub WriteLine(ByVal format As String, ByVal ParamArray arg As Object())
        _loggerLegacy.LogInformation(format, arg)
    End Sub

    Public Sub Flush()
        fileHandle.Flush()
    End Sub
#End Region

#Region "private functions"
    Private Sub ProcessLogMessage(ByVal value As String, ByVal level As LoggerLevel)

        If (fileLoggingLevel And level) <> 0 Then
            fileHandle.Write(System.Text.Encoding.ASCII.GetBytes(value), 0, Len(value))
            If autoFlush Then
                fileHandle.Flush()
            End If
        End If
        Dim con_m = consoleLoggingLevel And level
        If con_m <> 0 Then
            Select Case con_m
                Case LoggerLevel.debug, LoggerLevel.out
                    coutStream.Write(value)
                Case LoggerLevel.err
                    errorStream.Write(value)
            End Select
        End If
    End Sub
#End Region

#Region "functions for debugging"
    Public Sub BenchmarkStart()
        benchStartTime = Date.UtcNow
    End Sub
    Public Sub BenchmarkEnd()
        Dim ts = Date.UtcNow.Subtract(benchStartTime).Milliseconds
        Me.DebugWriteLine(ts & " ms")
    End Sub
#End Region

#Region " IDisposable Support "
    ' Do not change or add Overridable to these methods. 
    ' Put cleanup code in Dispose(ByVal disposing As Boolean). 
    Public Sub Dispose() Implements IDisposable.Dispose
        Dispose(True)
        GC.SuppressFinalize(Me)
    End Sub
    Protected Overrides Sub Finalize()
        Dispose(False)
        MyBase.Finalize()
    End Sub
#End Region

End Class


