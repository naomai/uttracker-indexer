Imports System.Net
Imports System.Net.Sockets
Imports System.Threading

Public Class SocketMaster
    Dim julkin As New JulkinNet()

    Dim ipQueue As New Hashtable
    Dim lastProcessed As Date
    'Dim eventCallback As SocketEventHandler

    Dim incomingEventsPerTick As Integer = 100

    Dim ignoreIps As New Hashtable

    Public Event PacketReceived(packet() As Byte, source As IPEndPoint)

    Public Sub New()
        julkin.setProto(JulkinNet.jnProt.jnUDP)
        julkin.timeout = 5000
        julkin.bind()
        lastProcessed = Date.UtcNow
    End Sub

    'Public Sub setEventHandler(ByRef callback As SocketEventHandler)
    '    eventCallback = callback
    'End Sub

    Public Sub sendTo(endpoint As IPEndPoint, packet As String)
        julkin.swriteTo(packet, endpoint)
    End Sub

    Public Sub sendTo(endpoint As String, packet As String)
        julkin.swriteTo(packet, endpoint)
    End Sub

    Public Sub tick()
        enqueueIncomingPackets()
        If (Date.UtcNow - lastProcessed).TotalMilliseconds > 100 Then
            dequeuePacketsForAllHosts()
            lastProcessed = Date.UtcNow
        End If
    End Sub

    Private Sub enqueueIncomingPackets()
        Dim packet() As Byte, source As New IPEndPoint(IPAddress.Any, 0), sourceIp As String
        ' Dim events As Integer = incomingEventsPerTick
        Do While julkin.Available > 0 'And events > 0
            Try
                packet = julkin.recvfrom(source)
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
            'events -= 1
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

    'Public Sub tick()
    '    Dim packet As String, source As New IPEndPoint(IPAddress.Any, 0)
    '    Try
    '        packet = julkin.recvfrom(source)
    '        If packet <> "" Then
    '            RaiseEvent PacketReceived(packet, source)
    '        End If
    '    Catch e As Exception

    '    End Try
    'End Sub

    'Public Delegate Sub SocketEventHandler(packet As String, source As IPEndPoint)

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
