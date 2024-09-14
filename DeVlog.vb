' DeVlog - VBNET logging class
' 2014 Namonaki14
Imports System.IO

Public Enum DeVlogLoggingLevel
    out = 1
    err = 2
    debug = 4
End Enum

Public Class DeVlog
    Implements IDisposable

    Private progName As String = System.Reflection.Assembly.GetEntryAssembly.GetName.Name
    Private fs As Integer
    Public errorStream As TextWriter = Console.Error
    Public coutStream As TextWriter = Console.Out
    Protected disposed As Boolean = False
    Private logfile As String
    Private benchStartTime As DateTime
    Protected lastLogItemTime As DateTime

    Public printToFile As Boolean = True
    Public fileLoggingLevel As Integer = (DeVlogLoggingLevel.out Or DeVlogLoggingLevel.err)
    Public consoleLoggingLevel As Integer = (DeVlogLoggingLevel.out Or DeVlogLoggingLevel.err)

#Region "constructor"

    Public Sub New(Optional ByVal logname As String = "")
        If (logname = "") Then logname = progName & ".log"
        fs = fopen(logname, "w")
        logfile = logname
    End Sub

    Protected Overridable Sub Dispose(ByVal disposing As Boolean)
        If Not Me.disposed Then
            If disposing Then
                fclose(fs)
                errorStream.Dispose()
                coutStream.Dispose()
            End If
        End If
        Me.disposed = True
    End Sub

#End Region

#Region "writing functions"
    Public Sub Write(ByVal value As String)
        dWrite(value, DeVlogLoggingLevel.out)
    End Sub

    Public Sub Write(ByVal format As String, ByVal ParamArray arg As Object())
        dWrite(String.Format(format, arg), DeVlogLoggingLevel.out)
    End Sub

    Public Sub Write(ByVal value As String, ByVal level As DeVlogLoggingLevel)
        dWrite(value, level)
    End Sub

    Public Sub ErrorWriteLine(ByVal value As String)
        dWrite(value & vbNewLine, DeVlogLoggingLevel.err)
    End Sub

    Public Sub ErrorWriteLine(ByVal format As String, ByVal ParamArray arg As Object())
        dWrite(String.Format(format, arg) & vbNewLine, DeVlogLoggingLevel.err)
    End Sub

    Public Sub DebugWriteLine(ByVal value As String)
        dWrite(value & vbNewLine, DeVlogLoggingLevel.debug)
    End Sub

    Public Sub DebugWriteLine(ByVal format As String, ByVal ParamArray arg As Object())
        dWrite(String.Format(format, arg) & vbNewLine, DeVlogLoggingLevel.debug)
    End Sub
    Public Sub WriteLine(ByVal value As String)
        dWrite(value & vbNewLine, DeVlogLoggingLevel.out)
    End Sub

    Public Sub WriteLine(ByVal format As String, ByVal ParamArray arg As Object())
        dWrite(String.Format(format, arg) & vbNewLine, DeVlogLoggingLevel.out)
    End Sub

    Public Sub LogWriteToFile()
        fclose(fs)
        fs = fopen(logfile, "a")
    End Sub
#End Region

#Region "private functions"
    Private Sub dWrite(ByVal value As String, ByVal level As DeVlogLoggingLevel)
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
            fwrite(fs, logmessage)
        End If
        Dim con_m = consoleLoggingLevel And level
        If con_m <> 0 Then
            Select Case con_m
                Case DeVlogLoggingLevel.debug, DeVlogLoggingLevel.out
                    coutStream.Write(logmessage)
                Case DeVlogLoggingLevel.err
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
