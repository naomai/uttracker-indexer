' DeVlog - VBNET logging class
' 2014 Namonaki14
Imports System.IO
Imports System.Environment

Public Enum LoggerLevel
    out = 1
    err = 2
    debug = 4
End Enum

Public Class Logger
    Implements IDisposable

    Private progName As String = System.Reflection.Assembly.GetEntryAssembly.GetName.Name
    Protected fileHandle As FileStream
    Public errorStream As TextWriter = Console.Error
    Public coutStream As TextWriter = Console.Out
    Protected disposed As Boolean = False
    Private benchStartTime As DateTime
    Protected lastLogItemTime As DateTime

    Public printToFile As Boolean = True
    Public autoFlush As Boolean = True
    Public fileLoggingLevel As Integer = (LoggerLevel.out Or LoggerLevel.err)
    Public consoleLoggingLevel As Integer = (LoggerLevel.out Or LoggerLevel.err)

#Region "constructor"

    Public Sub New(Optional ByVal logname As String = "")
        If (logname = "") Then logname = progName & ".log"
        fileHandle = File.Open(logname, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read)
        fileHandle.SetLength(0)
        logfile = logname
    End Sub

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
    Public Sub Write(ByVal value As String)
        ProcessLogMessage(value, LoggerLevel.out)
    End Sub

    Public Sub Write(ByVal format As String, ByVal ParamArray arg As Object())
        ProcessLogMessage(String.Format(format, arg), LoggerLevel.out)
    End Sub

    Public Sub Write(ByVal value As String, ByVal level As LoggerLevel)
        ProcessLogMessage(value, level)
    End Sub

    Public Sub ErrorWriteLine(ByVal value As String)
        ProcessLogMessage(value & NewLine, LoggerLevel.err)
    End Sub

    Public Sub ErrorWriteLine(ByVal format As String, ByVal ParamArray arg As Object())
        ProcessLogMessage(String.Format(format, arg) & NewLine, LoggerLevel.err)
    End Sub

    Public Sub DebugWriteLine(ByVal value As String)
        ProcessLogMessage(value & NewLine, LoggerLevel.debug)
    End Sub

    Public Sub DebugWriteLine(ByVal format As String, ByVal ParamArray arg As Object())
        ProcessLogMessage(String.Format(format, arg) & NewLine, LoggerLevel.debug)
    End Sub
    Public Sub WriteLine(ByVal value As String)
        ProcessLogMessage(value & NewLine, LoggerLevel.out)
    End Sub

    Public Sub WriteLine(ByVal format As String, ByVal ParamArray arg As Object())
        ProcessLogMessage(String.Format(format, arg) & NewLine, LoggerLevel.out)
    End Sub

    Public Sub Flush()
        fileHandle.Flush()
    End Sub
#End Region

#Region "private functions"
    Private Sub ProcessLogMessage(ByVal value As String, ByVal level As LoggerLevel)
        Static beginning As DateTime = DateTime.Parse("1.01.1990 00:00:00")
        Dim logLastDay = Math.Floor((lastLogItemTime - beginning).TotalDays)

        Dim dateNow As String
        If logLastDay = Math.Floor((Date.Now - beginning).TotalDays) Then
            dateNow = Now.ToString("HH:mm:ss")
        Else
            dateNow = Now.ToString("MM-dd-yyyy HH:mm:ss")
        End If
        lastLogItemTime = Date.Now

        Dim logmessage As String = String.Format("[{0}] {1}", dateNow, value)
        If (fileLoggingLevel And level) <> 0 Then
            fileHandle.Write(System.Text.Encoding.ASCII.GetBytes(logmessage), 0, Len(logmessage))
            If autoFlush Then
                fileHandle.Flush()
            End If
        End If
        Dim con_m = consoleLoggingLevel And level
        If con_m <> 0 Then
            Select Case con_m
                Case LoggerLevel.debug, LoggerLevel.out
                    coutStream.Write(logmessage)
                Case LoggerLevel.err
                    errorStream.Write(logmessage)
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
