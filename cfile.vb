Imports System.Threading

Module cfile

    'Private ptr(100) As Long
    Private openfiles(256) As IO.FileStream
    Private isopen(256) As Boolean
    Private filesbinary(256) As Boolean

    Public openFilesNum As Integer = 0

    Const INVALID_FILE_ID = -1

    Public Enum seekmode
        SEEK_SET = 0
        SEEK_CUR = 1
        SEEK_END = 2
    End Enum

    Public Function fopen(ByVal fname As String, ByVal MS As String)
        On Error Resume Next
        Dim ret_fn As Integer
        If Len(fname) > 250 Then Return False 'Exit Function
        For i = 1 To UBound(openfiles)
            If Not isopen(i) Then
                If Not IsNothing(openfiles(i)) Then fclose(i)
                ret_fn = i
                Exit For
            End If
        Next i
        If ret_fn = INVALID_FILE_ID Then
            Throw New CFileNoFreeHandlesException()
            Return INVALID_FILE_ID
        End If

        If Not fexists(fname) AndAlso (MS = "r" Or MS = "r+" Or MS = "rb" Or MS = "r+b") Then
            fopen = INVALID_FILE_ID
            'Err.Raise(660, "cfile.bas", "fopen(" & fname & "," & MS & "): file not found")
            Throw New CFileNotFoundException(fname, MS)
            Exit Function
        End If
        filesbinary(ret_fn) = False
        Select Case MS
            ' binary
            Case "wb", "w"
                If fexists(fname) Then
                    openfiles(ret_fn) = IO.File.Open(fname, IO.FileMode.Truncate, IO.FileAccess.Write, IO.FileShare.Read)
                Else
                    openfiles(ret_fn) = IO.File.Open(fname, IO.FileMode.OpenOrCreate, IO.FileAccess.Write, IO.FileShare.Read)
                End If

                'Open name For Output As #fn
                'Close #fn
                'Open name For Binary Access Write As #fn
                filesbinary(ret_fn) = True
            Case "w+b", "w+"
                If fexists(fname) Then IO.File.Open(fname, IO.FileMode.Truncate, IO.FileAccess.Write).Close()

                openfiles(ret_fn) = IO.File.Open(fname, IO.FileMode.OpenOrCreate, IO.FileAccess.ReadWrite, IO.FileShare.Read)
                'Open name For Output As #fn
                'Close #fn
                'Open name For Binary Access Read Write As #fn
                filesbinary(ret_fn) = True
            Case "ab", "a"
                If Not fexists(fname) Then IO.File.Open(fname, IO.FileMode.CreateNew, IO.FileAccess.Write).Close()
                openfiles(ret_fn) = IO.File.Open(fname, IO.FileMode.Append, IO.FileAccess.Write, IO.FileShare.Read)
                'Open name For Binary Access Write As #fn
                ptrset(ret_fn, flen(ret_fn))
                filesbinary(ret_fn) = True
            Case "a+b", "a+"
                'MsgBox name
                'Open name For Binary Access Read Write As #fn
                If Not fexists(fname) Then IO.File.Open(fname, IO.FileMode.CreateNew, IO.FileAccess.Write).Close()
                openfiles(ret_fn) = IO.File.Open(fname, IO.FileMode.Append, IO.FileAccess.Write, IO.FileShare.Read)
                'If fexists(fname) Then ptrset(ret_fn, flen(ret_fn) + 1) Else ptrset(ret_fn, 1)
                ptrset(ret_fn, openfiles(ret_fn).Position)
                filesbinary(ret_fn) = True
            Case "rb", "r"
                'Open name For Binary Access Read As #fn
                openfiles(ret_fn) = IO.File.Open(fname, IO.FileMode.Open, IO.FileAccess.Read, IO.FileShare.ReadWrite)
                filesbinary(ret_fn) = True
            Case "r+b", "r+"
                'Open name For Binary Access Read Write As #fn
                openfiles(ret_fn) = IO.File.Open(fname, IO.FileMode.Open, IO.FileAccess.ReadWrite, IO.FileShare.Read)
                filesbinary(ret_fn) = True

                ' text
                '    Case "w"
                '    Open name For Output As #fn
                '    Case "a"
                '    Open name For Output As #fn
                '        ptr(ret_fn) = FileLen(name)
                'Case "r": Open name For Input As #fn

                '    Case "w+"
                '    Open name For Output As #fn
                '    Case "a+"
                '    Open name For Output As #fn
                '        ptr(ret_fn) = FileLen(fn)
                '    Case "r+"
                '    Open name For Input As #fn

        End Select
        If IsNothing(openfiles(ret_fn)) Then
            isopen(ret_fn) = False
            Throw New CFileOpenException()
            Return INVALID_FILE_ID
        Else
            isopen(ret_fn) = True
            openFilesNum += 1
            Return ret_fn
        End If
    End Function

    Public Function fread(ByVal fn As Integer, ByVal rlen As Long) As String
        Dim tmp As String, tmpbyte() As Byte, e As Exception

        tmp = ""
        If fn <> INVALID_FILE_ID And Not IsNothing(openfiles(fn)) Then
            SyncLock openfiles(fn)
                Try
                    If filesbinary(fn) Then
                        'For i = 0 To rlen - 1
                        'If openfiles(fn).Position = openfiles(fn).Length Then Exit For
                        If openfiles(fn).Position + rlen > openfiles(fn).Length Then
                            rlen = openfiles(fn).Length - openfiles(fn).Position
                        End If
                        ReDim tmpbyte(rlen)
                        'fread = fread & Chr(fgetc(fn))
                        openfiles(fn).Read(tmpbyte, 0, rlen)
                        tmp = System.Text.ASCIIEncoding.ASCII.GetString(tmpbyte)
                        ' Get #fn, ptr(fn) + i, tmp
                        'fread = fread & tmp

                        'Next i
                    Else
                        'fread = Input(rlen, #openfiles(fn))
                    End If
                Catch e
                    'Throw New CFileAccessException(e.Message)
                Finally

                End Try
            End SyncLock
        Else
            Throw New CFileInvalidHandleException()
        End If
        'ptrset(fn, ptr(fn) + i)
        fread = tmp
    End Function

    Public Sub fwrite(ByVal fn As Integer, ByVal str As String)
        Dim e As Exception
        If fn <> INVALID_FILE_ID And Not IsNothing(openfiles(fn)) Then
            SyncLock openfiles(fn)
                Try
                    openfiles(fn).Write(System.Text.Encoding.ASCII.GetBytes(str), 0, Len(str))
                    openfiles(fn).Flush(True)
                Catch e
                    'Throw New CFileAccessException(e.Message)
                Finally

                End Try
            End SyncLock
        Else
            Throw New CFileInvalidHandleException()
        End If

    End Sub

    Public Sub fclose(ByVal fn)
        'Close #openfiles(fn)
        Dim e As Exception
        If fn <> INVALID_FILE_ID And Not IsNothing(openfiles(fn)) Then
            Try
                openfiles(fn).Close()
                openfiles(fn) = Nothing
                isopen(fn) = False
                openFilesNum -= 1
            Catch e
                'Throw New CFileAccessException(e.Message)
            End Try
        Else
            Throw New CFileInvalidHandleException()
        End If
    End Sub

    Private Function ptr(ByVal fn)
        Return openfiles(fn).Position
    End Function

    Private Sub ptrset(ByVal fn, ByVal newptr)
        openfiles(fn).Position = newptr
    End Sub

    Public Function fexists(ByVal fname As String)
        fexists = IO.File.Exists(fname)
    End Function

    Public Function flen(ByVal fn)
        flen = openfiles(fn).Length
    End Function

    Public Class CFileException
        Inherits Exception
        Public Sub New(message As String)
            MyBase.New(message)
        End Sub
        Public Sub New()
            MyBase.New()
        End Sub
    End Class

    Public Class CFileNoFreeHandlesException
        Inherits CFileException
        Public Sub New()
            MyBase.New("No more free file handles to allocate")
        End Sub
    End Class
    Public Class CFileNotFoundException
        Inherits CFileException
        Public Sub New(fname As String, access As String)
            MyBase.New("File not found: " & fname)
        End Sub
    End Class
    Public Class CFileOpenException
        Inherits CFileException
        Public Sub New()
            MyBase.New("Error opening the file")
        End Sub
    End Class
    Public Class CFileAccessException
        Inherits CFileException
        Public Sub New(message As String)
            MyBase.New("Error accessing the file: " & Message)
        End Sub
    End Class
    Public Class CFileInvalidHandleException
        Inherits CFileException
        Public Sub New()
            MyBase.New("Invalid file handle")
        End Sub
    End Class
End Module
