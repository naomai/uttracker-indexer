Public MustInherit Class GamemodeSpecificQuery
    Friend serverDto As ServerInfo
    Public MustOverride Sub Request(request As ServerRequest)
    Public MustOverride Sub ParseInfoPacket(incomingPacket As Hashtable)

    Protected Property info(key As String) As String
        Get
            Return serverDto.Info(key)
        End Get
        Set(value As String)
            serverDto.Info(key) = value
        End Set
    End Property

    Public Sub New()

    End Sub

    Public Sub New(serverInfo As ServerInfo)
        Me.serverDto = serverInfo
    End Sub

    Public Shared Function GetQueryObjectForContext(server As ServerInfo) As GamemodeSpecificQuery
        Dim queryObject As GamemodeSpecificQuery = Nothing
        If server.Capabilities.HasPropertyInterface = True Then
            Select Case server.Info("gametype")
                Case "MonsterHunt"
                    queryObject = New MonsterHuntQuery()
            End Select
        End If
        If IsNothing(queryObject) Then Return Nothing
        queryObject.serverDto = server
        Return queryObject
    End Function
End Class

Public Class MonsterHuntQuery
    Inherits GamemodeSpecificQuery

    Public Overrides Sub Request(request As ServerRequest)
        request.Add("game_property", "MonstersTotal")
    End Sub

    Public Overrides Sub ParseInfoPacket(incomingPacket As System.Collections.Hashtable)
        info("monsterstotal") = incomingPacket("monsterstotal")
    End Sub
End Class