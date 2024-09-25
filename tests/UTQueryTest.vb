Imports NUnit.Framework

Namespace Tests

    Public Class UTTQueryTest
        <SetUp>
        Public Sub Setup()

        End Sub

        <Test>
        Public Sub ServerParseSinglePacket()
            Dim packet As UTQueryPacket

            packet = New UTQueryPacket("\gamename\ut\gamever\451\minnetver\432\location\0\validate\XnaNeH9u\queryid\10.1\final\")
            Assert.That(packet("gamename"), [Is].EqualTo("ut"))
            Assert.That(packet("queryid"), [Is].Null)
            Assert.That(packet("final"), [Is].Null)

        End Sub

    End Class

End Namespace