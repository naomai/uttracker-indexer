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

    Protected ipPacketQueue As New Dictionary(Of IPEndPoint, EndpointPacketBuffer)
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
        DispatchLoop()
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

            sourceEndpoint = receiveResult.RemoteEndPoint
            sourceIp = sourceEndpoint.ToString
            If ignoreIps.Contains(sourceIp) Then
                Continue Do
            End If

            ReDim Preserve buffer(receiveResult.ReceivedBytes)
            EnqueuePacket(sourceEndpoint, buffer)


            'Tick()
        Loop
    End Sub

    Protected Async Sub DispatchLoop()
        Do
            Tick()
            Await Task.Delay(25)
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
        End SyncLock
        ipPacketQueue(sourceEndpoint).Enqueue(packet)
    End Sub

    Private Sub DequeueAll()
        Dim hosts As List(Of IPEndPoint)
        SyncLock ipPacketQueue
            If ipPacketQueue.Count = 0 Then
                Return
            End If

            hosts = ipPacketQueue.Keys.ToList()
        End SyncLock
        For Each host In hosts
            DequeueForHost(host)
        Next
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
        SyncLock packetQueue
            packetQueue.Enqueue(packet)
        End SyncLock
        newPackets = True
    End Sub

    Public Function Dequeue() As Byte()
        newPackets = False
        SyncLock packetQueue
            Return packetQueue.Dequeue()
        End SyncLock
    End Function

    Public ReadOnly Property Length As Integer
        Get
            Dim bytesTotal As Integer = 0
            SyncLock packetQueue
                For Each packet In packetQueue
                    bytesTotal += packet.Length
                Next
            End SyncLock
            Return bytesTotal
        End Get
    End Property

    Public Function PeekLast() As Byte()
        newPackets = False
        Return packetQueue.Last()
    End Function

    Public Function PeekAll() As Byte()
        'establish working length of packet stream
        Dim result(Me.Length) As Byte
        Dim offset As Integer = 0
        newPackets = False
        SyncLock packetQueue
            For Each packet In packetQueue
                ' if packet is zero-terminated, grab position of null byte and trim packet
                Dim zeroByteOffset = Array.IndexOf(packet, 0)
                If zeroByteOffset <> -1 Then
                    ReDim Preserve packet(zeroByteOffset) ' also trims null byte
                End If
                packet.CopyTo(result, offset)
                offset += packet.Length
            Next
        End SyncLock
        'adjust length to discard trimmed bytes in zero-terminated packets 
        ReDim Preserve result(offset)
        Return result
    End Function

    Public Sub Clear()
        newPackets = False
        SyncLock packetQueue
            packetQueue.Clear()
        End SyncLock
    End Sub

    Public Overrides Function ToString() As String
        Return "PB:CT=" + packetQueue.Count
    End Function
End Class