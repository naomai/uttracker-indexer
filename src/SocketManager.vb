Imports System.Net
Imports System.Net.Sockets
Imports System.Threading
Imports Naomai.UTT.Indexer.JulkinNet
Imports Org.BouncyCastle.Bcpg

''' <summary>
''' UDP socket abstraction and host filter
''' </summary>
Public Class SocketManager
    Protected socket As Socket
    Protected ignoreIps As New List(Of String)

    Protected ipPacketQueue As New Hashtable
    Protected lastProcessed As Date

    Public updateIntervalMs As Integer = 100
    Public packetReceiveThread As Thread

    Public Event PacketReceived(packet() As Byte, source As IPEndPoint)

    Public Sub New()
        Dim bindEndpoint As New IPEndPoint(IPAddress.Any, 0)
        socket = New Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        socket.Bind(bindEndpoint)
        socket.ReceiveTimeout = 5000
        socket.ReceiveBufferSize = 1024 * 1024

        lastProcessed = Date.UtcNow

        ReceiveLoop()
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

    Protected Async Sub ReceiveLoop()
        Dim packet() As Byte, bindEndpoint As EndPoint = New IPEndPoint(IPAddress.Any, 0)
        Dim sourceEndpoint As IPEndPoint, sourceIp As String
        Dim receiveResult As SocketReceiveFromResult
        Do
            ReDim Packet(2000)
            receiveResult = Await socket.ReceiveFromAsync(packet, bindEndpoint)
            If receiveResult.ReceivedBytes > 0 Then
                ReDim Preserve packet(receiveResult.ReceivedBytes)
            End If

            sourceEndpoint = receiveResult.RemoteEndPoint
            sourceIp = sourceEndpoint.ToString
            If ignoreIps.Contains(sourceIp) Then
                Continue Do
            End If
            If packet.Count > 0 Then
                EnqueuePacket(sourceEndpoint, packet)
            End If
        Loop
    End Sub

    Public Sub Tick()
        If (Date.UtcNow - lastProcessed).TotalMilliseconds > updateIntervalMs Then
            DequeueAll()
            lastProcessed = Date.UtcNow
        End If
    End Sub

    Private Sub EnqueuePacket(sourceEndpoint As IPEndPoint, packet As Byte())
        SyncLock ipPacketQueue
            If Not ipPacketQueue.ContainsKey(sourceEndpoint) Then
                ipPacketQueue.Add(sourceEndpoint, New Queue(Of Byte()))
            End If
            ipPacketQueue(sourceEndpoint).Enqueue(packet)
        End SyncLock
    End Sub

    Private Sub DequeueAll()
        SyncLock ipPacketQueue
            For Each host In ipPacketQueue.Keys
                DequeueForHost(host)
            Next
        End SyncLock
    End Sub

    Private Sub DequeueForHost(host As IPEndPoint)
        Dim packetQueue As Queue(Of Byte()) = ipPacketQueue(host), packet() As Byte
        Do While packetQueue.Count > 0
            packet = packetQueue.Dequeue()
            RaiseEvent PacketReceived(packet, host)
        Loop
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
