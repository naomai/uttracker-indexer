Imports System.Net
Imports System.Net.Sockets
Imports System.Threading

Public Class SocketManager
    Private socket As System.Net.Sockets.Socket

    Dim ipQueue As New Hashtable
    Dim lastProcessed As Date

    Dim incomingEventsPerTick As Integer = 100

    Dim ignoreIps As New Hashtable

    Public Event PacketReceived(packet() As Byte, source As IPEndPoint)
    Public Sub New()
        socket = New Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        socket.Bind(New IPEndPoint(IPAddress.Any, 0))
        socket.ReceiveTimeout = 5000
        socket.ReceiveBufferSize = 1024 * 1024

        lastProcessed = Date.UtcNow
    End Sub

    Public Sub sendTo(endpoint As String, packet As String)
        Dim buffer = System.Text.Encoding.ASCII.GetBytes(packet)

        Dim ip As String = getIp(endpoint), port As UInt16 = getPort(endpoint)

        Dim addr As IPAddress = Nothing, endpointObj As IPEndPoint = Nothing
        If Net.IPAddress.TryParse(ip, addr) Then
            endpointObj = New IPEndPoint(addr, port)
        Else
            endpointObj = New IPEndPoint(Dns.GetHostEntry(ip).AddressList(0), port)
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

    Public Sub tick()
        enqueueIncomingPackets()
        If (Date.UtcNow - lastProcessed).TotalMilliseconds > 100 Then
            dequeuePacketsForAllHosts()
            lastProcessed = Date.UtcNow
        End If
    End Sub

    Private Sub enqueueIncomingPackets()
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
                If ignoreIps.ContainsKey(sourceIp) Then
                    Continue Do
                End If
                If packet.Count > 0 Then
                    maybeCreateQueueForIp(source)
                    ipQueue(source).Enqueue(packet)
                End If
            Catch e As Exception

            End Try
        Loop
    End Sub

    Private Sub dequeuePacketsForAllHosts()
        For Each host In ipQueue.Keys
            dequeuePacketsForHost(host)
        Next
    End Sub

    Private Sub dequeuePacketsForHost(host As IPEndPoint)
        Dim packetQueue As Queue(Of Byte()) = ipQueue(host), packet() As Byte
        Do While packetQueue.Count > 0
            packet = packetQueue.Dequeue()
            RaiseEvent PacketReceived(packet, host)
        Loop
    End Sub

    Private Sub maybeCreateQueueForIp(source As IPEndPoint)
        If Not ipQueue.ContainsKey(source) Then
            ipQueue.Add(source, New Queue(Of Byte()))
        End If
    End Sub

    Public Sub addIgnoredIp(ip As EndPoint)
        addIgnoredIp(ip.ToString)
    End Sub

    Public Sub addIgnoredIp(ip As String)
        ignoreIps.Add(ip, True)
    End Sub

    Public Sub clearIgnoredIps()
        ignoreIps.Clear()
    End Sub
End Class
