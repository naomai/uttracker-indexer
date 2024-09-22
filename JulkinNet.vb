' JULKINNET - me-friendly TCP/UDP interface
' 2009 NaMONaKi14
' '14-03-27 VB6->dotNet rewrite

Imports System
Imports System.Math
Imports System.Text
Imports System.Net
Imports System.Net.Sockets
Imports System.Collections.Concurrent
Imports System.Text.Encoding
Imports System.Threading

Public Class JulkinNet
    Implements IDisposable
    ''' <summary>Creates network connection using simple interface</summary>

    Private Declare Function GetTickCount Lib "kernel32" () As Long

    Public Enum jnProt
        jnTCP = 0
        jnUDP = 1
    End Enum

#Region "Public properties"
    ''' <summary>Maximum time for operation</summary>
    Public timeout As Integer
    ''' <summary>Remote endpoint after receiving data</summary>
    Public udpRemote As IPEndPoint
#End Region
#Region "Private variables"
    Private disposed As Boolean = False

    Private protocol As jnProt

    Private dataCame As Boolean
    Private isSending As Boolean

    'Private buffer() As Byte
    Private hostPackets As New ConcurrentDictionary(Of String, String) 'Hashtable()
    Private hostLastActivity As New ConcurrentDictionary(Of String, ULong) 'Hashtable()

    Private timeStarted As Long
    Private remote As IPEndPoint

    Private socketTcp As System.Net.Sockets.Socket
    Private socketUdp As System.Net.Sockets.UdpClient

    Private threadLocker As New Object
#End Region

    ''' <summary>Creates new socket.</summary>
    Public Sub New()
        Randomize(Date.UtcNow.Ticks)
        isSending = False
        dataCame = False
        protocol = jnProt.jnTCP
        timeout = 500
    End Sub

    Public ReadOnly Property Available As Integer
        Get
            Return socketTcp.Available
        End Get
    End Property

    ''' <summary>Establish connection to selected host</summary>
    ''' <param name="address">Host name followed by colon and port number (eg.: 10.0.0.2:21)</param>
    Public Sub connect(ByVal address As String)
        Me.connect(getIp(address), getPort(address))
    End Sub

    ''' <summary>Establish connection to selected host</summary>
    ''' <param name="ip">Host name</param>
    ''' <param name="port">Port number</param>
    Public Sub connect(ByVal ip As String, ByVal port As Integer)
        On Error Resume Next
        Dim addr As IPAddress = Nothing

        If Not IsNothing(socketTcp) Then
            disconnect()
        End If
        If Net.IPAddress.TryParse(ip, addr) Then
            remote = New IPEndPoint(addr, port)
        Else
            remote = New IPEndPoint(Dns.GetHostEntry(ip).AddressList(0), port)
        End If
        If protocol = jnProt.jnTCP Then
            socketTcp = New Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        Else
            socketTcp = New Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            socketTcp.Bind(New IPEndPoint(IPAddress.Any, 0))
        End If
        socketTcp.ReceiveTimeout = timeout


        If protocol = jnProt.jnTCP Then
            socketTcp.Connect(ip, port)

        Else

        End If
    End Sub
    Public Sub bind(Optional ByVal port As Integer = 0)

        Dim addr As IPAddress = Nothing

        If Not IsNothing(socketTcp) Then
            disconnect()
        End If
        If protocol = jnProt.jnUDP Then
            socketTcp = New Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            socketTcp.Bind(New IPEndPoint(IPAddress.Any, port))
        End If
        socketTcp.ReceiveTimeout = timeout
        socketTcp.ReceiveBufferSize = 1024 * 1024 ' 1mb
    End Sub

    ''' <summary>Close the socket if it's connected</summary>
    Public Sub disconnect()
        If Not IsNothing(socketTcp) AndAlso socketTcp.Connected Then
            socketTcp.Shutdown(SocketShutdown.Both)
            If socketTcp.Connected Then socketTcp.Disconnect(True)
        End If
    End Sub
    ''' <summary>
    ''' Change socket protocol before connecting 
    ''' </summary>
    ''' <param name="prot">Protocol (jnTCP or jnUDP)</param>
    ''' <remarks></remarks>
    Public Sub setProto(ByVal prot As jnProt)
        If IsNothing(socketTcp) Then
            protocol = prot
        End If
    End Sub

    ''' <summary>
    ''' Send packet to remote computer
    ''' </summary>
    ''' <param name="packet">Packet content</param>
    ''' <remarks></remarks>
    Public Sub swrite(ByVal packet As Byte()) '
        If IsNothing(remote) Then Exit Sub
        socketTcp.SendTo(packet, remote)

    End Sub
    Public Sub swrite(ByVal packet As String) '
        swrite(System.Text.Encoding.ASCII.GetBytes(packet))
    End Sub

    ''' <summary>
    ''' Reads the next incoming packet
    ''' </summary>
    ''' <returns>Packet content</returns>
    ''' <remarks></remarks>


    Public Function sreadNext() As String
        sreadNext = System.Text.Encoding.ASCII.GetString(sreadNextBytes())
    End Function

    Public Function sreadNextBytes() As Byte()
        Dim buffer(0) As Byte
        setTimeout()

        If IsNothing(remote) Then Return buffer
        Do

        Loop While socketTcp.Available = 0 AndAlso Not isTimedOut()
        If socketTcp.Available <= 0 Then Return buffer
        ReDim buffer(socketTcp.Available - 1)
        Try
            socketTcp.Receive(buffer, socketTcp.Available, SocketFlags.None)
            If buffer(0) <> 0 Then
                Return buffer
            Else

            End If
        Catch
        End Try
        Return Nothing

    End Function

    Public Function sread(ByVal numbytes As Integer)
        Dim out As String = ""
        Dim bytesRead As Integer = 0, brtmp As Integer, buffer() As Byte
        setTimeout()

        Do


        Loop While socketTcp.Available = 0 And Not isTimedOut()

        ReDim buffer(numbytes)


        Do
            Try
                brtmp = socketTcp.Receive(buffer, bytesRead, socketTcp.Available, SocketFlags.None)
            Catch
                Exit Do
            End Try
            bytesRead += brtmp

        Loop While bytesRead < numbytes AndAlso Not isTimedOut()

        out = System.Text.Encoding.ASCII.GetString(buffer)

        sread = Left(out, Min(numbytes, bytesRead))

    End Function
    Public Function sreadLarge()
        Dim tmp As String
        sreadLarge = ""
        Do
            tmp = sread(32768)
            sreadLarge = sreadLarge & tmp

        Loop While tmp <> ""
    End Function
    Protected Overrides Sub Finalize()

        Dispose(False)
        MyBase.Finalize()
    End Sub
    Private Sub setTimeout()
        timeStarted = GetTickCount
    End Sub

    Private Function isTimedOut()
        isTimedOut = (timeout <> 0 AndAlso GetTickCount() > (timeStarted + timeout))
    End Function
    Private Function getIp(ByVal addr As String)
        Dim tmpx() As String
        tmpx = Split(addr, ":", 2)
        getIp = tmpx(0)
    End Function

    Private Function getPort(ByVal addr As String)
        Dim tmpx() As String
        tmpx = Split(addr, ":", 2)
        getPort = tmpx(1)
    End Function

    Public Sub swriteTo(ByVal packet() As Byte, ByVal addr As String)
        swriteTo(packet, getIp(addr), getPort(addr))
    End Sub

    Public Sub swriteTo(ByVal packet() As Byte, ByVal ip As String, ByVal port As Integer) '
        Dim addr As IPAddress = Nothing, remotex As IPEndPoint = Nothing
        If Net.IPAddress.TryParse(ip, addr) Then
            remotex = New IPEndPoint(addr, port)
        Else
            remotex = New IPEndPoint(Dns.GetHostEntry(ip).AddressList(0), port)
        End If
        If IsNothing(remotex) Then Exit Sub
        swriteTo(packet, remotex)
    End Sub
    Public Sub swriteTo(ByVal packet() As Byte, ByVal endpoint As IPEndPoint) '
        Try
            socketTcp.SendTo(packet, endpoint)
        Catch E As Exception
            'MsgBox(E.Message)
            Try
                socketTcp.Receive(Nothing, socketTcp.Available, SocketFlags.None)
            Catch NullEx As ArgumentNullException

            End Try
        End Try
    End Sub

    Public Sub swriteTo(ByVal packet As String, ByVal addr As String)
        swriteTo(System.Text.Encoding.ASCII.GetBytes(packet), getIp(addr), getPort(addr))
    End Sub

    Public Sub swriteTo(ByVal packet As String, ByVal ip As String, ByVal port As Integer) '
        swriteTo(System.Text.Encoding.ASCII.GetBytes(packet), ip, port)
    End Sub
    Public Sub swriteTo(ByVal packet As String, ByVal endpoint As IPEndPoint) '
        swriteTo(System.Text.Encoding.ASCII.GetBytes(packet), endpoint)
    End Sub

    Public Function sreadFrom(ByVal addr As String)
        Return sreadFrom(getIp(addr), getPort(addr))
    End Function

    Public Function sreadFrom(ByVal ip As String, ByVal port As Integer) '
        Dim addr As IPAddress = Nothing, remotex As IPEndPoint = Nothing
        If Net.IPAddress.TryParse(ip, addr) Then
            remotex = New IPEndPoint(addr, port)
        Else
            remotex = New IPEndPoint(Dns.GetHostEntry(ip).AddressList(0), port)
        End If
        Return sreadFrom(remotex)
    End Function

    Public Function sreadFrom(ByVal endpoint As IPEndPoint) As String
        Dim remoteEndpoint As New IPEndPoint(IPAddress.Any, 0), remoteAddress As String
        Dim expectedAddress As String, bytesRead As Integer
        Dim btmp As String, buffer(10000) As Byte, buffercopy(10000) As Byte
        sreadFrom = ""
        expectedAddress = endpoint.ToString



        If hostPackets.ContainsKey(expectedAddress) AndAlso Len(hostPackets(expectedAddress)) <> 0 Then
            hostPackets.TryRemove(expectedAddress, sreadFrom)
            Exit Function
        End If
        hostLastActivity(expectedAddress) = GetTickCount

        Do
            Thread.Sleep(1)
            '14-07-28 i just learned a new word: MONITOR
            'we're messing with a single socket using multiple threads
            'so locking this section of code is important if we don't want to have:
            '- data loss
            '- packets from other threads getting mixed with our data
            'the per-ip buffer approach is stupid, so don't copy it to other projects.

            SyncLock threadLocker
                If socketTcp.Available > 0 Then
                    Try
                        bytesRead = socketTcp.ReceiveFrom(buffer, DirectCast(remoteEndpoint, EndPoint))
                        If bytesRead > 0 Then
                            remoteAddress = remoteEndpoint.ToString()

                            Array.Copy(buffer, buffercopy, bytesRead)

                            btmp = Left(System.Text.Encoding.ASCII.GetString(buffercopy), bytesRead)
                            If hostPackets.ContainsKey(remoteAddress) Then
                                hostPackets(remoteAddress) &= btmp
                            Else
                                hostPackets(remoteAddress) = btmp
                            End If
                        End If

                    Catch
                        'Debugger.Break()
                    End Try
                End If
            End SyncLock

        Loop While GetTickCount - hostLastActivity(expectedAddress) < timeout AndAlso hostPackets.ContainsKey(expectedAddress) AndAlso Len(hostPackets(expectedAddress)) = 0

        If hostPackets.ContainsKey(expectedAddress) AndAlso Len(hostPackets(expectedAddress)) <> 0 Then
            hostPackets.TryRemove(expectedAddress, sreadFrom)
        Else
            hostPackets.TryRemove(expectedAddress, Nothing)
            Return ""
        End If
    End Function

    Public Function recvfrom(ByRef source As EndPoint)
        Dim buffer() As Byte, bufferConv() As Byte, packetString As String = "", bytesRead As Integer
        If socketTcp.Available = 0 Then Return ""
        ReDim buffer(2000)
        bytesRead = socketTcp.ReceiveFrom(buffer, source)
        If bytesRead > 0 Then
            ReDim Preserve buffer(bytesRead)
        End If
        Return buffer
    End Function

    Public Sub UdpBind(Optional ByVal port As Integer = 0)
        If protocol = jnProt.jnUDP Then
            socketUdp = New UdpClient(port, AddressFamily.InterNetwork)
        End If
    End Sub
    Public Sub UdpSwriteTo(ByVal dest As String, ByVal packet As String) '
        Me.UdpSwriteTo(getIp(dest), getPort(dest), packet)
    End Sub
    Public Sub UdpSwriteTo(ByVal destip As String, ByVal destport As Integer, ByVal packet As String) '
        Dim addr As IPAddress = Nothing
        Dim remoteX As IPEndPoint
        If Net.IPAddress.TryParse(destip, addr) Then
            remoteX = New IPEndPoint(addr, destport)
        Else
            remoteX = New IPEndPoint(Dns.GetHostEntry(destip).AddressList(0), destport)
        End If
        socketUdp.Send(System.Text.Encoding.ASCII.GetBytes(packet), Len(packet), remoteX)
        'Thread.Sleep(10)
    End Sub
    Public Function UdpSreadNext() As String
        Dim exc As SocketException, buffer() As Byte
        udpRemote = New IPEndPoint(IPAddress.Any, 0)
        setTimeout()
        Do
            Thread.Sleep(5)
        Loop While socketUdp.Available = 0 AndAlso Not isTimedOut()
        ReDim buffer(socketUdp.Available - 1)
        Try
            buffer = socketUdp.Receive(udpRemote)
            UdpSreadNext = System.Text.Encoding.ASCII.GetString(buffer)
        Catch exc
            UdpSreadNext = ""
        End Try

        'Application.DoEvents()
    End Function

    ' IDisposable
    Protected Overridable Sub Dispose(
       ByVal disposing As Boolean)
        If Not Me.disposed Then
            If disposing Then
                If Not IsNothing(socketTcp) Then
                    disconnect()
                    socketTcp.Dispose()
                    socketTcp = Nothing
                End If
                If Not IsNothing(socketUdp) Then
                    socketUdp.Close()
                    socketUdp = Nothing
                End If
                hostPackets = Nothing
                hostLastActivity = Nothing
                'buffer = Nothing
            End If
            remote = Nothing
            udpRemote = Nothing
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
