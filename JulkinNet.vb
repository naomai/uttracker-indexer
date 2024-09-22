﻿' JULKINNET - me-friendly TCP interface
' 2009 NaMONaKi14
' '14-03-27 VB6->dotNet rewrite

Imports System.Math
Imports System.Net
Imports System.Net.Sockets

Public Class JulkinNet
    Implements IDisposable
    ''' <summary>Creates network connection using simple interface</summary>

    Private Declare Function GetTickCount Lib "kernel32" () As Long

#Region "Public properties"
    ''' <summary>Maximum time for operation</summary>
    Public timeout As Integer
#End Region
#Region "Private variables"

    Private timeStarted As Long
    Private socketObj As Socket
    Private remote As IPEndPoint
    Private disposed As Boolean = False
#End Region

    ''' <summary>Creates new socket.</summary>
    Public Sub New()
        timeout = 500
    End Sub

    Public ReadOnly Property Available As Integer
        Get
            Return socketObj.Available
        End Get
    End Property

    ''' <summary>Establish connection to selected host</summary>
    ''' <param name="address">Host name followed by colon and port number (eg.: 10.0.0.2:21)</param>
    Public Sub Connect(ByVal address As String)
        Me.Connect(GetIp(address), GetPort(address))
    End Sub

    ''' <summary>Establish connection to selected host</summary>
    ''' <param name="ip">Host name</param>
    ''' <param name="port">Port number</param>
    Public Sub Connect(ByVal ip As String, ByVal port As Integer)
        Dim addr As IPAddress = Nothing

        If Not IsNothing(socketObj) Then
            Disconnect()
        End If
        If Net.IPAddress.TryParse(ip, addr) Then
            remote = New IPEndPoint(addr, port)
        Else
            remote = New IPEndPoint(Dns.GetHostEntry(ip).AddressList(0), port)
        End If

        socketObj = New Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        socketObj.ReceiveTimeout = timeout
        socketObj.Connect(ip, port)
    End Sub

    ''' <summary>Close the socket if it's connected</summary>
    Public Sub Disconnect()
        If Not IsNothing(socketObj) AndAlso socketObj.Connected Then
            socketObj.Shutdown(SocketShutdown.Both)
            If socketObj.Connected Then
                socketObj.Disconnect(True)
            End If
        End If
    End Sub

    ''' <summary>
    ''' Send packet to remote computer
    ''' </summary>
    ''' <param name="packet">Packet content</param>
    ''' <remarks></remarks>
    Public Sub Write(ByVal packet As Byte()) '
        If IsNothing(remote) Then
            Exit Sub
        End If
        socketObj.SendTo(packet, remote)
    End Sub
    Public Sub Write(ByVal packet As String) '
        Write(System.Text.Encoding.ASCII.GetBytes(packet))
    End Sub

    ''' <summary>
    ''' Reads the next incoming packet
    ''' </summary>
    ''' <returns>Packet content</returns>
    ''' <remarks></remarks>


    Public Function ReadNext() As String
        Return System.Text.Encoding.ASCII.GetString(ReadNextAsBytes())
    End Function

    Public Function ReadNextAsBytes() As Byte()
        Dim buffer(0) As Byte
        SetTimeout()

        If IsNothing(remote) Then Return buffer
        Do

        Loop While socketObj.Available = 0 AndAlso Not IsTimedOut()
        If socketObj.Available <= 0 Then Return buffer
        ReDim buffer(socketObj.Available - 1)
        Try
            socketObj.Receive(buffer, socketObj.Available, SocketFlags.None)
            If buffer(0) <> 0 Then
                Return buffer
            Else

            End If
        Catch
        End Try
        Return Nothing

    End Function

    Public Function Read(ByVal bytesToRead As Integer) As String
        Dim outputString As String
        Dim bytesRead As Integer = 0, brtmp As Integer, buffer() As Byte
        SetTimeout()

        Do

        Loop While socketObj.Available = 0 And Not IsTimedOut()

        ReDim buffer(bytesToRead)
        Do
            Try
                brtmp = socketObj.Receive(buffer, bytesRead, socketObj.Available, SocketFlags.None)
            Catch
                Exit Do
            End Try
            bytesRead += brtmp

        Loop While bytesRead < bytesToRead AndAlso Not IsTimedOut()

        outputString = System.Text.Encoding.ASCII.GetString(buffer)

        Return Left(outputString, Min(bytesToRead, bytesRead))

    End Function

    Private Sub SetTimeout()
        timeStarted = GetTickCount
    End Sub

    Private Function IsTimedOut() As Boolean
        Return timeout <> 0 AndAlso GetTickCount() > (timeStarted + timeout)
    End Function
    Private Function GetIp(ByVal addr As String) As String
        Dim tmpx() As String
        tmpx = Split(addr, ":", 2)
        Return tmpx(0)
    End Function

    Private Function GetPort(ByVal addr As String) As UInt16
        Dim tmpx() As String
        tmpx = Split(addr, ":", 2)
        Return tmpx(1)
    End Function


    Protected Overrides Sub Finalize()
        Dispose(False)
        MyBase.Finalize()
    End Sub

    ' IDisposable
    Protected Overridable Sub Dispose(
       ByVal disposing As Boolean)
        If Not Me.disposed Then
            If disposing Then
                If Not IsNothing(socketObj) Then
                    Disconnect()
                    socketObj.Dispose()
                    socketObj = Nothing
                End If
            End If
            remote = Nothing
            ' Free your own state (unmanaged objects).
            ' Set large fields to null.
        End If
        Me.disposed = True
    End Sub

#Region " IDisposable Support "
    ' This code added by Visual Basic to 
    ' correctly implement the disposable pattern.
    Public Sub Dispose() Implements IDisposable.Dispose
        ' Do not change this code. 
        ' Put cleanup code in
        ' Dispose(ByVal disposing As Boolean) above.
        Dispose(True)
        GC.SuppressFinalize(Me)
    End Sub
#End Region
End Class
