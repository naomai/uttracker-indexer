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
    Private Declare Sub Sleep Lib "kernel32.dll" (ByVal Milliseconds As Integer)
    Private Declare Function GetCurrentThread Lib "kernel32.dll" () As Long
    Private Declare Function GetCurrentThreadId Lib "kernel32.dll" () As Long
    Private Declare Function GetCurrentProcessId Lib "kernel32.dll" () As Long

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

    Private sprot As jnProt

    Private dataCame As Boolean
    Private isSending As Boolean

    'Private buffer() As Byte
    Private buf_ip As New ConcurrentDictionary(Of String, String) 'Hashtable()
    Private timeout_ip As New ConcurrentDictionary(Of String, ULong) 'Hashtable()

    Private timeStarted As Long
    Private timeLastData As Long
    Private remote As IPEndPoint

    Private socketx As System.Net.Sockets.Socket

    Private socketudp As System.Net.Sockets.UdpClient
    Private threadLocker As New Object
    Private lockingThreadId As Long
#End Region

    ''' <summary>Creates new socket.</summary>
    Public Sub New()
        Randomize(Date.UtcNow.Ticks)
        isSending = False
        dataCame = False
        sprot = jnProt.jnTCP
        timeout = 500
    End Sub

    Public ReadOnly Property Available As Integer
        Get
            Return socketx.Available
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
        'Dim hostEntry As System.Net.IPHostEntry = System.Net.Dns.GetHostEntry(ip)

        If Not IsNothing(socketx) Then
            disconnect()
        End If
        If Net.IPAddress.TryParse(ip, addr) Then
            remote = New IPEndPoint(addr, port)
        Else
            remote = New IPEndPoint(Dns.GetHostEntry(ip).AddressList(0), port)
        End If
        If sprot = jnProt.jnTCP Then
            socketx = New Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            'socketx.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, True)
        Else
            socketx = New Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            socketx.Bind(New IPEndPoint(IPAddress.Any, 0))
        End If
        socketx.ReceiveTimeout = timeout


        ' If socket.State <> sckClosed Then disconnect()

        'setTimeout()

        If sprot = jnProt.jnTCP Then
            'socketx.LocalPort = (Rnd() * 6000) + 1024
            socketx.Connect(ip, port)
            'Do
            'DoEvents()
            'Loop While Socket.State <> sckConnected And Not isTimedOut
        Else
            'Socket.RemoteHost = ip
            'Socket.RemotePort = port
            'socketx.Bind(New System.Net.IPEndPoint(Dns.GetHostEntry(Dns.GetHostName()).AddressList(0), 0))
            'Do
            '    DoEvents()
            'Loop While Socket.State <> sckOpen And Not isTimedOut
        End If
    End Sub
    Public Sub bind(Optional ByVal port As Integer = 0)

        Dim addr As IPAddress = Nothing
        'Dim hostEntry As System.Net.IPHostEntry = System.Net.Dns.GetHostEntry(ip)

        If Not IsNothing(socketx) Then
            disconnect()
        End If
        If sprot = jnProt.jnUDP Then
            socketx = New Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            socketx.Bind(New IPEndPoint(IPAddress.Any, port))
        End If
        socketx.ReceiveTimeout = timeout
        socketx.ReceiveBufferSize = 1024 * 1024 ' 1mb
    End Sub

    ''' <summary>Close the socket if it's connected</summary>
    Public Sub disconnect()
        If Not IsNothing(socketx) AndAlso socketx.Connected Then
            'setTimeout()

            socketx.Shutdown(SocketShutdown.Both)
            If socketx.Connected Then socketx.Disconnect(True)
            'Do
            'DoEvents()
            'Loop While Socket.State <> sckClosed And Not isTimedOut
        End If
    End Sub
    ''' <summary>
    ''' Change socket protocol before connecting 
    ''' </summary>
    ''' <param name="prot">Protocol (jnTCP or jnUDP)</param>
    ''' <remarks></remarks>
    Public Sub setProto(ByVal prot As jnProt)
        If IsNothing(socketx) Then
            sprot = prot
        End If
    End Sub

    ''' <summary>
    ''' Send packet to remote computer
    ''' </summary>
    ''' <param name="packet">Packet content</param>
    ''' <remarks></remarks>
    Public Sub swrite(ByVal packet As Byte()) '
        If IsNothing(remote) Then Exit Sub
        socketx.SendTo(packet, remote)

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
        'Dim exc As SocketException
        'dataCame = False
        Dim buffer(0) As Byte
        setTimeout()
        'Do
        'DoEvents()
        'Loop While Not dataCame And Not isTimedOut
        If IsNothing(remote) Then Return buffer
        Do
            'Application.DoEvents()
            'Thread.Sleep(15)
        Loop While socketx.Available = 0 AndAlso Not isTimedOut()
        If socketx.Available <= 0 Then Return buffer
        ReDim buffer(socketx.Available - 1)
        Try
            socketx.Receive(buffer, socketx.Available, SocketFlags.None)
            If buffer(0) <> 0 Then
                Return buffer
            Else
                'Return ""
            End If
        Catch
            'sreadNextBytes = ""
        End Try
        Return Nothing
        'Application.DoEvents()
    End Function

    Public Function sread(ByVal numbytes As Integer)
        Dim out As String = ""
        Dim bytesRead As Integer = 0, brtmp As Integer, buffer() As Byte
        setTimeout()
        'Do
        'DoEvents()
        'Loop While Len(buffer) < numbytes And Not isTimedOut
        Do


        Loop While socketx.Available = 0 And Not isTimedOut()

        ReDim buffer(numbytes)

        'setTimeout()
        Do
            'brtmp = socketx.Available
            Try
                brtmp = socketx.Receive(buffer, bytesRead, socketx.Available, SocketFlags.None)
            Catch
                Exit Do
            End Try
            bytesRead += brtmp
            'Application.DoEvents()
            'Thread.Sleep(50)
            'sread = Left(buffer, numbytes)
        Loop While bytesRead < numbytes AndAlso Not isTimedOut()
        'setTimeout()
        'Do
        out = System.Text.Encoding.ASCII.GetString(buffer)
        'Loop While Len(out) < numbytes And Not isTimedOut()
        sread = Left(out, Min(numbytes, bytesRead))
        'buffer = Mid(buffer, numbytes + 1)
    End Function
    Public Function sreadLarge()
        Dim tmp As String
        sreadLarge = ""
        Do
            tmp = sread(32768)
            sreadLarge = sreadLarge & tmp
            'Thread.Sleep(1)
            'Application.DoEvents()
        Loop While tmp <> ""
        'MsgBox(Len(sreadLarge))
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
        'Thread.Sleep(0)
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
            socketx.SendTo(packet, endpoint)
        Catch E As Exception
            'MsgBox(E.Message)
            Try
                socketx.Receive(Nothing, socketx.Available, SocketFlags.None)
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
        'Dim exc As SocketException
        Dim remotex As New IPEndPoint(IPAddress.Any, 0), rip As String, loc As String, bytesread As Integer
        Dim btmp As String, buffer(10000) As Byte, buffercopy(10000) As Byte
        sreadFrom = ""
        loc = endpoint.ToString



        If buf_ip.ContainsKey(loc) AndAlso Len(buf_ip(loc)) <> 0 Then
            'sreadFrom = buf_ip(loc)
            'buf_ip(loc) = ""
            'buf_ip.TryRemove(loc, Nothing)
            buf_ip.TryRemove(loc, sreadFrom)
            'Return buf_ip(loc)
            Exit Function
        End If
        timeout_ip(loc) = GetTickCount
        'socketx.ReceiveTimeout = 200

        Do
            Thread.Sleep(1)
            '14-07-28 i just learned a new word: MONITOR
            'we're messing with a single socket using multiple threads
            'so locking this section of code is important if we don't want to have:
            '- data loss
            '- packets from other threads getting mixed with our data
            'the per-ip buffer approach is stupid, so don't copy it to other projects.

            SyncLock threadLocker
                'lockingThreadId = GetCurrentThreadId() And &HFFFF
                If socketx.Available > 0 Then

                    'ReDim buffer(5000)
                    'ReDim buffercopy(5000)

                    'Console.WriteLine("{0}|{1}", buffer.Count, socketx.Available)
                    Try

                        ' If bytesread > 5000 Then
                        'Debugger.Break()
                        ' End If
                        bytesread = socketx.ReceiveFrom(buffer, DirectCast(remotex, EndPoint))
                        'socketx.Receive(buffer, socketx.Available, SocketFlags.None)
                        If bytesread > 0 Then

                            rip = remotex.ToString()

                            'Dim bx As Integer = 0
                            'For i = 0 To Min(bytesread, buffer.Count)
                            '    If buffer(i) Then
                            '        buffercopy(bx) = buffer(i)
                            '        bx += 1
                            '    End If
                            'Next


                            Array.Copy(buffer, buffercopy, bytesread)

                            btmp = Left(System.Text.Encoding.ASCII.GetString(buffercopy), bytesread)
                            'btmp = ASCII.GetString(buffer)
                            'Console.WriteLine("GOT: {0} WAIT: {1} | {2}", rip, loc, Left(btmp, 30))
                            If buf_ip.ContainsKey(rip) Then
                                buf_ip(rip) &= btmp
                            Else
                                buf_ip(rip) = btmp
                            End If
                        End If

                    Catch
                        'Debugger.Break()
                    End Try
                End If
            End SyncLock
            'Dim a = Len(buf_ip(loc))
            'Dim b = GetTickCount - (timeout_ip(loc) + timeout)
        Loop While GetTickCount - timeout_ip(loc) < timeout AndAlso buf_ip.ContainsKey(loc) AndAlso Len(buf_ip(loc)) = 0
        'Console.WriteLine("GOT: {0}", loc)
        If buf_ip.ContainsKey(loc) AndAlso Len(buf_ip(loc)) <> 0 Then
            'sreadFrom = buf_ip(loc)
            buf_ip.TryRemove(loc, sreadFrom)
        Else
            buf_ip.TryRemove(loc, Nothing)
            Return ""
        End If
    End Function

    Public Function recvfrom(ByRef source As EndPoint)
        Dim buffer() As Byte, bufferConv() As Byte, packetString As String = "", bytesRead As Integer
        'source = New IPEndPoint(IPAddress.Any, 0)
        If socketx.Available = 0 Then Return ""
        ReDim buffer(2000)
        bytesRead = socketx.ReceiveFrom(buffer, source)
        If bytesRead > 0 Then
            ReDim Preserve buffer(bytesRead)
            'bufferConv = Encoding.Convert(Encoding.GetEncoding(437), Encoding.Unicode, buffer)
            'packetString = ASCII.GetString(buffer)
            'packetString = Unicode.GetString(bufferConv) 
        End If
        'Return packetString
        Return buffer
    End Function

    Public Sub UdpBind(Optional ByVal port As Integer = 0)
        If sprot = jnProt.jnUDP Then
            socketudp = New UdpClient(port, AddressFamily.InterNetwork)
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
        socketudp.Send(System.Text.Encoding.ASCII.GetBytes(packet), Len(packet), remoteX)
        'Thread.Sleep(10)
    End Sub
    Public Function UdpSreadNext() As String
        Dim exc As SocketException, buffer() As Byte
        udpRemote = New IPEndPoint(IPAddress.Any, 0)
        setTimeout()
        Do
            Thread.Sleep(5)
        Loop While socketudp.Available = 0 AndAlso Not isTimedOut()
        ReDim buffer(socketudp.Available - 1)
        Try
            buffer = socketudp.Receive(udpRemote)
            UdpSreadNext = System.Text.Encoding.ASCII.GetString(buffer)
        Catch exc
            UdpSreadNext = ""
        End Try

        'Application.DoEvents()
    End Function

    ' IDisposable
    Protected Overridable Sub Dispose( _
       ByVal disposing As Boolean)
        If Not Me.disposed Then
            If disposing Then
                If Not IsNothing(socketx) Then
                    disconnect()
                    socketx.Dispose()
                    socketx = Nothing
                End If
                If Not IsNothing(socketudp) Then
                    socketudp.Close()
                    socketudp = Nothing
                End If
                buf_ip = Nothing
                timeout_ip = Nothing
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
