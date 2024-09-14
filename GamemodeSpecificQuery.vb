Public MustInherit Class GamemodeSpecificQuery
    Friend scannerSlave As ServerScannerWorker
    Public MustOverride Function getInfoRequestString() As String
    Public MustOverride Sub parseInfoPacket(incomingPacket As Hashtable)

    Protected Property info(key As String) As String
        Get
            Return scannerSlave.info(key)
        End Get
        Set(value As String)
            scannerSlave.info(key) = value
        End Set
    End Property

    Public Sub New()

    End Sub

    Public Sub New(scannerWorker As ServerScannerWorker)
        Me.scannerSlave = scannerWorker
    End Sub

    Public Shared Function getQueryObjectForContext(scannerSlave As ServerScannerWorker)
        Dim queryObject As GamemodeSpecificQuery = Nothing
        If scannerSlave.caps.hasPropertyInterface = True Then
            Select Case scannerSlave.info("gametype")
                Case "MonsterHunt"
                    queryObject = New MonsterHuntQuery()
            End Select
        End If
        If IsNothing(queryObject) Then Return Nothing
        queryObject.scannerSlave = scannerSlave
        Return queryObject
    End Function
End Class

Public Class MonsterHuntQuery
    Inherits GamemodeSpecificQuery

    Public Overrides Function getInfoRequestString() As String
        Return "\game_property\MonstersTotal\"
    End Function

    Public Overrides Sub parseInfoPacket(incomingPacket As System.Collections.Hashtable)
        info("monsterstotal") = incomingPacket("monsterstotal")
    End Sub
End Class