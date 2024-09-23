Imports System.Net
Imports System.Net.Sockets

Public Class SocketManager
    Protected socket As Socket
    Protected ignoreIps As New List(Of String)

    Protected ipPacketQueue As New Hashtable
    Protected lastProcessed As Date

    Public updateIntervalMs As Integer = 100

    Public Event PacketReceived(packet() As Byte, source As IPEndPoint)

    Public Sub New()
        socket = New Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        socket.Bind(New IPEndPoint(IPAddress.Any, 0))
        socket.ReceiveTimeout = 5000
        socket.ReceiveBufferSize = 1024 * 1024

        lastProcessed = Date.UtcNow
    End Sub

    Public Sub SendTo(endpoint As String, packet As String)
        Dim buffer = System.Text.Encoding.ASCII.GetBytes(packet)

        Dim host As String = GetHost(endpoint), port As UInt16 = GetPort(endpoint)

        Dim addr As IPAddress = Nothing, endpointObj As IPEndPoint = Nothing
        If Net.IPAddress.TryParse(host, addr) Then
            endpointObj = New IPEndPoint(addr, port)
        Else
            endpointObj = New IPEndPoint(Dns.GetHostEntry(host).AddressList(0), port)
        End If


        Try
            socket.SendTo(buffer, endpointObj)
        Catch E As Exception
            Try
                socket.Receive(Nothing, socket.Available, SocketFlags.None)
            Catch NullEx As ArgumentNullException

            End Try
        End Try
    End Sub

    Public Sub Tick()
        EnqueueIncoming()
        If (Date.UtcNow - lastProcessed).TotalMilliseconds > updateIntervalMs Then
            DequeueAll()
            lastProcessed = Date.UtcNow
        End If
    End Sub

    Private Sub EnqueueIncoming()
        Dim packet() As Byte, source As EndPoint = New IPEndPoint(IPAddress.Any, 0), sourceIp As String
        Do While socket.Available > 0
            Try

                Dim bytesRead As Integer
                ReDim packet(2000)
                bytesRead = socket.ReceiveFrom(packet, source)
                If bytesRead > 0 Then
                    ReDim Preserve packet(bytesRead)
                End If

                sourceIp = source.ToString
                If ignoreIps.Contains(sourceIp) Then
                    Continue Do
                End If
                If packet.Count > 0 Then
                    MaybeCreateQueueForIp(source)
                    ipPacketQueue(source).Enqueue(packet)
                End If
            Catch e As Exception

            End Try
        Loop
    End Sub

    Private Sub DequeueAll()
        For Each host In ipPacketQueue.Keys
            DequeueForHost(host)
        Next
    End Sub

    Private Sub DequeueForHost(host As IPEndPoint)
        Dim packetQueue As Queue(Of Byte()) = ipPacketQueue(host), packet() As Byte
        Do While packetQueue.Count > 0
            packet = packetQueue.Dequeue()
            RaiseEvent PacketReceived(packet, host)
        Loop
    End Sub

    Private Sub MaybeCreateQueueForIp(source As IPEndPoint)
        If Not ipPacketQueue.ContainsKey(source) Then
            ipPacketQueue.Add(source, New Queue(Of Byte()))
        End If
    End Sub

    Public Sub AddIgnoredIp(ip As EndPoint)
        AddIgnoredIp(ip.ToString)
    End Sub

    Public Sub AddIgnoredIp(ip As String)
        ignoreIps.Add(ip)
    End Sub

    Public Sub ClearIgnoredIps()
        ignoreIps.Clear()
    End Sub
End Class
