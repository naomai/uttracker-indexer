Public MustInherit Class GamemodeSpecificQuery
    Friend server As ServerInfo
    Public MustOverride Function GetInfoRequestString() As String
    Public MustOverride Sub ParseInfoPacket(incomingPacket As Hashtable)

    Protected Property info(key As String) As String
        Get
            Return server.info(key)
        End Get
        Set(value As String)
            server.info(key) = value
        End Set
    End Property

    Public Sub New()

    End Sub

    Public Sub New(serverInfo As ServerInfo)
        Me.server = serverInfo
    End Sub

    Public Shared Function GetQueryObjectForContext(server As ServerInfo)
        Dim queryObject As GamemodeSpecificQuery = Nothing
        If server.caps.hasPropertyInterface = True Then
            Select Case server.info("gametype")
                Case "MonsterHunt"
                    queryObject = New MonsterHuntQuery()
            End Select
        End If
        If IsNothing(queryObject) Then Return Nothing
        queryObject.server = server
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