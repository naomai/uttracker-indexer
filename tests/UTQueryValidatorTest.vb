Imports System.ComponentModel.DataAnnotations
Imports Naomai.UTT.Indexer.UTQueryPacket
Imports NUnit.Framework
Imports NUnit.Framework.Legacy

Namespace Tests

    Public Class UTQueryValidatorTest
        <SetUp>
        Public Sub Setup()

        End Sub

        <Test>
        Public Sub ValidatorReturnsCorrectFields()
            Dim packet As UTQueryPacket
            Dim rules = New Dictionary(Of String, String) From {
                {"gamename", "required|string"},
                {"gamesubver", "integer"},
                {"nonexistingfield", "integer"}
            }

            packet = New UTQueryPacket("\gamename\unreal\gamever\227k\gamesubver\11\mingamever\224\location\0\queryid\68.1\final\")
            Dim validated = packet.Validate(rules)
            Assert.That(validated, Contains.Key("gamename"))
            Assert.That(validated, Contains.Key("gamesubver"))
            Assert.That(validated, Does.Not.ContainKey("location"))
            Assert.That(validated, Does.Not.ContainKey("nonexistingfield"))

        End Sub

        <Test>
        Public Sub ValidatorTestRequiredField()
            Dim packet As UTQueryPacket
            Dim rules = New Dictionary(Of String, String) From {
                {"gamename", "required|string"}
            }

            packet = New UTQueryPacket("\gamename\unreal\gamever\227k\gamesubver\11\mingamever\224\location\0\queryid\68.1\final\")
            Dim validated = packet.Validate(rules)
            Assert.That(validated, Contains.Key("gamename"))

            packet.Remove("gamename")
            Assert.Throws(Of UTQueryValidationException)(
                Sub()
                    packet.Validate(rules)
                End Sub
                )

        End Sub

        <Test>
        Public Sub ValidatorGetString()
            Dim packet = New UTQueryPacket("\gamename\unreal\gamever\227k\gamesubver\11\mingamever\224\location\0\queryid\68.1\final\")
            Dim rules = New Dictionary(Of String, String) From {
                {"gamename", "string"}
            }

            Dim validated = packet.Validate(rules)
            Assert.That(validated, Contains.Key("gamename"))
            Assert.That(validated("gamename"), [Is].EqualTo("unreal"))
        End Sub

        <Test>
        Public Sub ValidatorStringRange()
            Dim packet = New UTQueryPacket("\testfield\123456\queryid\68.1\final\")
            Dim val

            val = TestFieldAgainstRule(packet, "string|gte:5|lte:7")
            Assert.That(val, [Is].EqualTo("123456"))

            val = TestFieldAgainstRule(packet, "string|gte:6|lte:7")
            Assert.That(val, [Is].EqualTo("123456"))

            val = TestFieldAgainstRule(packet, "string|gte:5|lte:6")
            Assert.That(val, [Is].EqualTo("123456"))

            val = TestFieldAgainstRule(packet, "string|gt:5|lt:7")
            Assert.That(val, [Is].EqualTo("123456"))

            Assert.Throws(Of UTQueryValidationException)(
                Sub()
                    val = TestFieldAgainstRule(packet, "string|gte:7")
                End Sub
                )

            Assert.Throws(Of UTQueryValidationException)(
                Sub()
                    val = TestFieldAgainstRule(packet, "string|lte:5")
                End Sub
                )

            Assert.Throws(Of UTQueryValidationException)(
                Sub()
                    val = TestFieldAgainstRule(packet, "string|lt:6")
                End Sub
            )

            Assert.Throws(Of UTQueryValidationException)(
                Sub()
                    val = TestFieldAgainstRule(packet, "string|gt:6")
                End Sub
            )
        End Sub

        <Test>
        Public Sub ValidatorGetInt()
            Dim packet = New UTQueryPacket("\gamename\unreal\gamever\227k\gamesubver\11\mingamever\224\location\0\queryid\68.1\final\")
            Dim rules = New Dictionary(Of String, String) From {
                {"gamesubver", "integer"}
            }

            Dim validated = packet.Validate(rules)
            Assert.That(validated, Contains.Key("gamesubver"))
            Assert.That(validated("gamesubver"), [Is].EqualTo(11))
        End Sub


        <Test>
        Public Sub ValidatorIntRange()
            Dim packet = New UTQueryPacket("\testfield\500\queryid\68.1\final\")
            Dim val

            val = TestFieldAgainstRule(packet, "integer|gte:499|lte:501")
            Assert.That(val, [Is].EqualTo(500))

            val = TestFieldAgainstRule(packet, "integer|gte:499|lte:500")
            Assert.That(val, [Is].EqualTo(500))

            val = TestFieldAgainstRule(packet, "integer|gte:500|lte:501")
            Assert.That(val, [Is].EqualTo(500))

            val = TestFieldAgainstRule(packet, "integer|gt:499|lt:501")
            Assert.That(val, [Is].EqualTo(500))

            Assert.Throws(Of UTQueryValidationException)(
                Sub()
                    val = TestFieldAgainstRule(packet, "integer|gte:501")
                End Sub
                )

            Assert.Throws(Of UTQueryValidationException)(
                Sub()
                    val = TestFieldAgainstRule(packet, "integer|lte:499")
                End Sub
                )

            Assert.Throws(Of UTQueryValidationException)(
                Sub()
                    val = TestFieldAgainstRule(packet, "integer|lt:500")
                End Sub
            )

            Assert.Throws(Of UTQueryValidationException)(
                Sub()
                    val = TestFieldAgainstRule(packet, "integer|gt:500")
                End Sub
            )
        End Sub

        <Test>
        Public Sub ValidatorGetBool()
            Dim packet = New UTQueryPacket("\bIsOvertime\True\bGameEnded\False\queryid\68.1\final\")
            Dim rules = New Dictionary(Of String, String) From {
                {"bisovertime", "boolean"},
                {"bgameended", "boolean"}
            }

            Dim validated = packet.Validate(rules)
            Assert.That(validated, Contains.Key("bisovertime"))
            Assert.That(validated("bisovertime"), [Is].EqualTo(True))
            Assert.That(validated, Contains.Key("bgameended"))
            Assert.That(validated("bgameended"), [Is].EqualTo(False))
        End Sub

        <Test>
        Public Sub ValidatorGetDefaultValue()
            Dim packet = New UTQueryPacket("\bIsOvertime\\GameName\\ElapsedTime\\RemainingTime\163\queryid\68.1\final\")
            Dim rules = New Dictionary(Of String, String) From {
                {"bisovertime", "boolean|default:true"},
                {"gamename", "string|default:ut"},
                {"elapsedtime", "integer|default:437"},
                {"remainingtime", "integer|default:11"}
            }

            Dim validated = packet.Validate(rules)
            Assert.That(validated, Contains.Key("bisovertime"))
            Assert.That(validated("bisovertime"), [Is].EqualTo(True))
            Assert.That(validated, Contains.Key("gamename"))
            Assert.That(validated("gamename"), [Is].EqualTo("ut"))
            Assert.That(validated, Contains.Key("elapsedtime"))
            Assert.That(validated("elapsedtime"), [Is].EqualTo(437))
            Assert.That(validated, Contains.Key("remainingtime"))
            Assert.That(validated("remainingtime"), [Is].EqualTo(163))
        End Sub


        <Test>
        Public Sub ValidatorGetArray()
            Dim packet = New UTQueryPacket("\name_0\dean\name_1\eddie\name_2\cindy\name_5\laura\queryid\68.1\final\")
            Dim rules = New Dictionary(Of String, String) From {
                {"name", "array:string"}
            }

            Dim validated = packet.Validate(rules)
            Assert.That(validated, Contains.Key("name"))
            Assert.That(validated("name"), [Is].InstanceOf(Of Dictionary(Of Integer, Object))())
            Assert.That(validated("name"), Has.Exactly(4).Items)
            Assert.That(validated("name")(0), [Is].EqualTo("dean"))
            Assert.That(validated("name")(1), [Is].EqualTo("eddie"))
            Assert.That(validated("name")(2), [Is].EqualTo("cindy"))
            Assert.That(validated("name")(5), [Is].EqualTo("laura"))

        End Sub


        Private Function TestFieldAgainstRule(packet As UTQueryPacket, rule As String)
            Dim rules = New Dictionary(Of String, String) From {
                {"testfield", rule}
}
            Dim validated = packet.Validate(rules)
            Assert.That(validated, Contains.Key("testfield"))
            Return validated("testfield")
        End Function


    End Class
End Namespace