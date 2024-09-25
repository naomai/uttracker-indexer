Imports Microsoft.EntityFrameworkCore.DbLoggerCategory
Imports Mysqlx.Crud
Imports System.ComponentModel.DataAnnotations
Imports Naomai.UTT.Indexer.UTQueryPacket
Imports NUnit.Framework
Imports Mysqlx.XDevAPI.Common

Namespace Tests

    Public Class UTTQueryTest
        <SetUp>
        Public Sub Setup()

        End Sub

        <TestCase("\ip\201.137.1.56:5534\ip\201.137.1.56:7778\ip\201.137.1.56:5512\final\")>
        Public Sub MasterListResponseParse(packetString As String)
            Dim packet As UTQueryPacket
            packet = New UTQueryPacket(packetString, UTQueryPacketFlags.UTQP_MasterServerIpList)

            Assert.That(packet.Count, [Is].EqualTo(3))
            For Each ipEntry As UTQueryKeyValuePair In packet
                Assert.That(ipEntry.key, [Is].EqualTo("ip"))
            Next
            Assert.That(packet("final"), [Is].Null)
        End Sub


        <TestCase("ut", "\gamename\ut\gamever\451\minnetver\432\location\0\validate\XnaNeH9u\queryid\10.1\final\")>
        <TestCase("unreal", "\gamename\unreal\gamever\227k\gamesubver\11\mingamever\224\location\3\validate\jm96Voe7\final\\queryid\97.1")>
        Public Sub ServerResponseParseSinglePacket(expectedGameName As String, packetString As String)
            Dim packet As UTQueryPacket

            packet = New UTQueryPacket(packetString)
            Assert.That(packet("gamename"), [Is].EqualTo(expectedGameName))
            Assert.That(packet("queryid"), [Is].Null)
            Assert.That(packet("final"), [Is].Null)
        End Sub

        <Test>
        Public Sub ServerResponseParseMultiPacket()
            Dim packet As UTQueryPacket

            packet = New UTQueryPacket("\NumPlayers\0\queryid\70.1\NumSpectators\0\queryid\70.2\GameSpeed\1.000000\queryid\70.3\CurrentID\3\queryid\70.4\bGameEnded\False\queryid\70.5\bOvertime\False\queryid\70.6\ElapsedTime\\queryid\70.7\RemainingTime\\queryid\70.8\TimeLimit\\final\\queryid\70.9")
            Assert.That(packet("numplayers"), [Is].EqualTo("0"))
            Assert.That(packet("gamespeed"), [Is].EqualTo("1.000000"))
            Assert.That(packet("remainingtime"), [Is].EqualTo(""))
        End Sub

        <TestCase("\validate\gYL/pHpg\final\\queryid\68.2")>
        <TestCase("\gamename\unreal\gamever\227k\gamesubver\11\mingamever\224\location\0\queryid\68.1")>
        Public Sub ServerResponseParseMultiPacketIncomplete(packetString As String)
            Assert.Throws(Of UTQueryResponseIncompleteException)(
                Function()
                    Return New UTQueryPacket(packetString)
                End Function
                )
        End Sub

        <Test>
        Public Sub QueryCreatePacket()
            Dim packet As UTQueryPacket
            packet = New UTQueryPacket(UTQueryPacketFlags.UTQP_SimpleRequest)
            packet("info") = ""
            packet("players") = ""
            packet("rules") = "XServerQuery"

            Dim packetString = packet.ToString()

            StringAssert.Contains("\info\", packetString)
            StringAssert.Contains("\players\", packetString)
            StringAssert.Contains("\rules\XServerQuery", packetString)
            StringAssert.DoesNotContain("queryid", packetString)
            StringAssert.DoesNotContain("final", packetString)
        End Sub

        <Test>
        Public Sub ServerResponseCreatePacket()
            Dim packet As UTQueryPacket
            packet = New UTQueryPacket()
            packet("gamename") = "ut"
            packet("gamever") = "451"
            packet("location") = ""

            Dim packetString = packet.ToString()

            StringAssert.Contains("\gamename\ut", packetString)
            StringAssert.Contains("\gamever\451", packetString)
            StringAssert.Contains("\location\\", packetString)
            StringAssert.Contains("\queryid\", packetString)
            StringAssert.Contains(".1\final", packetString)

        End Sub
    End Class

End Namespace