Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports System.Threading

Imports NUnit.Framework

Namespace Tests

    Public Class SocketManagerTest
        <SetUp>
        Public Sub Setup()

        End Sub

        <Test, CancelAfter(2000)>
        Public Sub ReceivePackets(token As CancellationToken)
            Dim mgr = CreateSocketManager()
            Dim hostBuffers = CreateHostBuffersForManager(mgr)
            DeployIncomingPackets(mgr.socket)
            WaitForReceiveEvents(hostBuffers, token)

            Assert.That(hostBuffers, Contains.Key("192.168.22.33:80"))
            Assert.That(hostBuffers, Contains.Key("192.168.22.34:443"))

            Assert.That(hostBuffers("192.168.22.33:80"), [Is].EqualTo("111333"))
            Assert.That(hostBuffers("192.168.22.34:443"), [Is].EqualTo("222444"))


        End Sub

        <Test>
        Public Sub SendPackets()
            Dim mgr = CreateSocketManager()
            Dim socket = mgr.socket

            mgr.SendTo("192.168.11.22:443", "555")
            mgr.SendTo("192.168.11.22:443", "666")
            mgr.SendTo("192.168.11.23:80", "777")
            mgr.SendTo("192.168.11.22:443", "")
            mgr.SendTo("192.168.11.22:443", "888")


            Assert.That(socket.SendBuffers, Contains.Key("192.168.11.22:443"))
            Assert.That(socket.SendBuffers, Contains.Key("192.168.11.23:80"))

            Assert.That(socket.SendBuffers("192.168.11.22:443"), Has.Exactly(4).Items)
            Assert.That(socket.SendBuffers("192.168.11.23:80"), Has.Exactly(1).Items)

            Assert.That(socket.SendBuffers("192.168.11.22:443"), Contains.Item("555"))
            Assert.That(socket.SendBuffers("192.168.11.22:443"), Contains.Item("666"))
            Assert.That(socket.SendBuffers("192.168.11.22:443"), Contains.Item(""))
            Assert.That(socket.SendBuffers("192.168.11.22:443"), Does.Not.Contain("777"))

            Assert.That(socket.SendBuffers("192.168.11.23:80"), Contains.Item("777"))


        End Sub

        <Test, MaxTime(2000)>
        Public Sub TestIpFilter(token As CancellationToken)
            Dim mgr = CreateSocketManager()
            Dim hostBuffers = CreateHostBuffersForManager(mgr)

            ' Adding rule
            mgr.AddIgnoredIp("192.168.22.34:443")
            DeployIncomingPackets(mgr.socket)
            WaitForReceiveEvents(hostBuffers, token)

            Assert.That(hostBuffers, Contains.Key("192.168.22.33:80"))
            Assert.That(hostBuffers, Does.Not.ContainKey("192.168.22.34:443"))

            ' Clearing rule
            hostBuffers.Clear()

            mgr.ClearIgnoredIps()
            DeployIncomingPackets(mgr.socket)
            WaitForReceiveEvents(hostBuffers, token)

            Assert.That(hostBuffers, Contains.Key("192.168.22.34:443"))

        End Sub

        Private Function CreateSocketManager() As SocketManager(Of MockSocket)
            Dim mgr = New SocketManager(Of MockSocket)()
            Return mgr
        End Function

        Private Function CreateHostBuffersForManager(mgr As SocketManager(Of MockSocket)) As Dictionary(Of String, String)
            Dim hostBuffers As New Dictionary(Of String, String)
            AddHandler mgr.NewDataReceived, Sub(buffer As EndpointPacketBuffer, source As IPEndPoint)
                                                If Not hostBuffers.ContainsKey(source.ToString()) Then
                                                    hostBuffers(source.ToString()) = ""
                                                End If
                                                hostBuffers(source.ToString()) &= BytesToString(buffer.PeekAll())
                                            End Sub
            Return hostBuffers
        End Function

        Private Sub WaitForReceiveEvents(hostBuffers As Dictionary(Of String, String), ByRef token As CancellationToken)
            Do While hostBuffers.Count = 0
                Task.Delay(100).Wait()
                token.ThrowIfCancellationRequested()
            Loop
        End Sub
        Private Sub DeployIncomingPackets(socket As MockSocket)
            socket.EnqueueReceivedString("111", "192.168.22.33", 80)
            socket.EnqueueReceivedString("222", "192.168.22.34", 443)
            socket.EnqueueReceivedString("", "192.168.22.33", 80)
            socket.EnqueueReceivedString("333", "192.168.22.33", 80)
            socket.EnqueueReceivedString("444", "192.168.22.34", 443)
        End Sub

        Private Function BytesToString(bytes As Byte()) As String
            Return Encoding.ASCII.GetString(bytes)
        End Function
    End Class

    Public Class MockSocket
        Inherits UdpSocketAdapter
        Private _receiveQueue As New Queue(Of Packet)
        Public SendBuffers As New Dictionary(Of String, List(Of String))

        Public Sub New()
            MyBase.New()

        End Sub


        Public Sub EnqueueReceivedString(data As String, ip As String, port As Long)
            Dim pck As Packet
            pck.buffer = data
            pck.remoteEP = New IPEndPoint(IPAddress.Parse(ip), port)
            _receiveQueue.Enqueue(pck)
        End Sub


        Public Overrides Async Function ReceiveFromAsync(buffer As ArraySegment(Of Byte), remoteEP As EndPoint) As Task(Of SocketReceiveFromResult)

            Do While _receiveQueue.Count = 0
                Await Task.Delay(50)
            Loop
            Dim pck = _receiveQueue.Dequeue()
            Dim receiveResult As SocketReceiveFromResult
            receiveResult.ReceivedBytes = pck.buffer.Length
            receiveResult.RemoteEndPoint = pck.remoteEP
            Encoding.ASCII.GetBytes(pck.buffer).CopyTo(buffer.AsMemory())
            Return receiveResult
        End Function

        Public Overrides Function SendTo(buffer As Byte(), remoteEP As EndPoint) As Integer
            Dim bufferStr = Encoding.ASCII.GetString(buffer)

            Dim epStr = remoteEP.ToString()
            If Not SendBuffers.ContainsKey(epStr) Then
                SendBuffers(epStr) = New List(Of String)
            End If

            SendBuffers(epStr).Add(bufferStr)

            Return bufferStr.Length
        End Function



        Private Structure Packet
            Dim buffer As String
            Dim remoteEP As EndPoint
        End Structure
    End Class



End Namespace