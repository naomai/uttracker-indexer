Public MustInherit Class GamemodeSpecificQuery
    Friend serverWorker As ServerQuery
    Public MustOverride Function GetInfoRequestString() As String
    Public MustOverride Sub ParseInfoPacket(incomingPacket As Hashtable)

    Protected Property info(key As String) As String
        Get
            Return serverWorker.info(key)
        End Get
        Set(value As String)
            serverWorker.info(key) = value
        End Set
    End Property

    Public Sub New()

    End Sub

    Public Sub New(serverWorker As ServerQuery)
        Me.serverWorker = serverWorker
    End Sub

    Public Shared Function GetQueryObjectForContext(serverWorker As ServerQuery)
        Dim queryObject As GamemodeSpecificQuery = Nothing
        If serverWorker.caps.hasPropertyInterface = True Then
            Select Case serverWorker.info("gametype")
                Case "MonsterHunt"
                    queryObject = New MonsterHuntQuery()
            End Select
        End If
        If IsNothing(queryObject) Then Return Nothing
        queryObject.serverWorker = serverWorker
        Return queryObject
    End Function
End Class

Public Class MonsterHuntQuery
    Inherits GamemodeSpecificQuery

    Public Overrides Function GetInfoRequestString() As String
        Return "\game_property\MonstersTotal\"
    End Function

    Public Overrides Sub ParseInfoPacket(incomingPacket As System.Collections.Hashtable)
        info("monsterstotal") = incomingPacket("monsterstotal")
    End Sub
End Class