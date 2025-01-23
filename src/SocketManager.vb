Imports System.Net
Imports System.Net.Sockets
Imports System.Threading
Imports Naomai.UTT.Indexer.JulkinNet

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

    Public Event NewDataReceived(packetBuffer As EndpointPacketBuffer, source As IPEndPoint)

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
        Dim buffer() As Byte, bindEndpoint As EndPoint = New IPEndPoint(IPAddress.Any, 0)
        Dim sourceEndpoint As IPEndPoint, sourceIp As String
        Dim receiveResult As SocketReceiveFromResult
        Do
            ReDim buffer(2000)
            receiveResult = Await socket.ReceiveFromAsync(buffer, bindEndpoint)
            If receiveResult.ReceivedBytes > 0 Then
                ReDim Preserve buffer(receiveResult.ReceivedBytes)
            End If

            sourceEndpoint = receiveResult.RemoteEndPoint
            sourceIp = sourceEndpoint.ToString
            If ignoreIps.Contains(sourceIp) Then
                Continue Do
            End If
            If buffer.Length > 0 Then
                EnqueuePacket(sourceEndpoint, buffer)
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
                ipPacketQueue.Add(sourceEndpoint, New EndpointPacketBuffer)
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
        Dim packetQueue As EndpointPacketBuffer = ipPacketQueue(host)
        If packetQueue.newPackets Then
            RaiseEvent NewDataReceived(packetQueue, host)
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


Public Class EndpointPacketBuffer
    Private packetQueue As New Queue(Of Byte())
    Public newPackets As Boolean = False

    Public Sub Enqueue(packet As Byte())
        packetQueue.Enqueue(packet)
        newPackets = True
    End Sub

    Public Function Dequeue() As Byte()
        newPackets = False
        Return packetQueue.Dequeue()
    End Function


    Public Function PeekLast() As Byte()
        newPackets = False
        Return packetQueue.Last()
    End Function

    Public Function PeekAll() As Byte()
        Dim result As Byte()
        Dim bytesTotal As Integer = 0
        newPackets = False
        For Each packet In packetQueue
            bytesTotal += packet.Length
        Next
        ReDim result(bytesTotal)
        Dim offset As Integer = 0
        For Each packet In packetQueue
            Dim zeroByteOffset = Array.IndexOf(packet, 0)
            If zeroByteOffset <> -1 Then
                ReDim Preserve packet(zeroByteOffset)
            End If
            packet.CopyTo(result, offset)
            offset += packet.Length
        Next
        ReDim Preserve result(offset)
        Return result
    End Function

    Public Sub Clear()
        newPackets = False
        packetQueue.Clear()
    End Sub
End Class