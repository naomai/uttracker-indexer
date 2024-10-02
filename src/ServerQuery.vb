﻿Imports System.Net
Imports System.Globalization
Imports System.Text
Imports System.Runtime.InteropServices

Public Class ServerQuery
    Dim socket As SocketManager
    Public scannerMaster As ServerScanner

    Public info As Hashtable
    Public players As List(Of Hashtable)
    Public variables As Hashtable

    Public deployTimeOffsetMs As Integer
    Public firstTimeTest, secondTimeTest As Single
    Public firstTimeTestLocal, secondTimeTestLocal, infoSentTimeLocal As DateTime

    Public packetsSent As Integer = 0
    Public packetsReceived As Integer = 0
    Protected challenge As String

    Private state As ServerQueryState
    Public caps As ServerCapabilities
    Public addressQuery As String
    'Public addressQuery2 As String
    Public addressGame As String
    Public incomingPacket As Hashtable
    Friend incomingPacketObj As UTQueryPacket
    Private resendAttempts As Integer = 0
    Friend lastActivity As Date
    Protected saver As ServerDataUpdater
    Protected gamemodeQuery As GamemodeSpecificQuery

    Private formatProvider = CultureInfo.InvariantCulture

    Protected packetCharset As Encoding = Encoding.GetEncoding(1252)

    Public Sub New(master As ServerScanner, serverAddress As String)
        addressQuery = serverAddress
        'addressQuery2 = serverAddress
        scannerMaster = master
        With caps
            .hasPropertyInterface = True
            .supportsVariables = True
        End With

        state.starting = True
        state.started = False
        saver = New ServerDataUpdater(Me)

        challenge = generateChallenge()
    End Sub

    Public Sub tick()
        If Not state.done Then
            If state.starting Then
                state.started = True
                state.starting = False
                sendRequest()
            Else

                If Not IsNothing(incomingPacket) Then ' we received a full response from server
                    packetReceived()
                    sendRequest()
                Else ' haven't got anything, just checking for timeouts
                    If (Date.UtcNow - lastActivity).TotalSeconds > 10 AndAlso (Date.UtcNow - scannerMaster.scanLastTouchAll).TotalSeconds < 5 Then
                        If Not skipStepIfOptional() Then
                            abortScan()
                        End If
                    End If
                End If
            End If
        End If
        If Not saver.state.done Then
            saver.Tick()
        End If
    End Sub

    Public Function isDone()
        Return state.done
    End Function

    Public Function getState() As ServerQueryState
        Return state
    End Function

    Public Sub setSocket(ByRef master As SocketManager)
        socket = master
    End Sub

    Private Sub sendRequest()
        Const xsqSuffix = "XServerQuery"
        Dim serverRecord = saver.GetServerRecord()

        If isInRequestState() Then Return ' remove this when implementing resend feature

        With state
            If Not .hasBasic Then
                .hasValidated = Not IsNothing(serverRecord.LastValidation) AndAlso serverRecord.LastValidation > Date.UtcNow.AddHours(-24)
                Dim challengeSuffix = ""
                If Not .hasValidated Then
                    challengeSuffix = "\secure\" & challenge
                End If
                serverSend("\basic\" & challengeSuffix)
                .requestingBasic = True

            ElseIf Not .hasInfo Then
                serverSend("\info\" & IIf(caps.hasXSQ, xsqSuffix, ""))
                .requestingInfo = True
                infoSentTimeLocal = Date.UtcNow
            ElseIf Not .hasInfoExtended AndAlso Not .hasTimeTest AndAlso caps.hasPropertyInterface Then
                firstTimeTestLocal = Date.UtcNow ' AKA timestamp of sending the extended info request
                gamemodeQuery = GamemodeSpecificQuery.GetQueryObjectForContext(Me)
                Dim gamemodeAdditionalRequests As String = "", otherAdditionalRequests As String = ""
                If Not IsNothing(gamemodeQuery) Then
                    gamemodeAdditionalRequests = gamemodeQuery.GetInfoRequestString()
                    caps.gamemodeExtendedInfo = True
                End If
                If Not info.ContainsKey("timelimit") Then
                    otherAdditionalRequests &= "\game_property\TimeLimit\"
                End If

                serverSend("\game_property\NumPlayers\\game_property\NumSpectators\" _
                           & "\game_property\GameSpeed\\game_property\CurrentID\" _
                           & "\game_property\bGameEnded\\game_property\bOvertime\" _
                           & "\game_property\ElapsedTime\\game_property\RemainingTime\" _
                           & otherAdditionalRequests _
                           & gamemodeAdditionalRequests)
                .requestingInfoExtended = True
            ElseIf Not .hasPlayers AndAlso info("numplayers") <> 0 AndAlso Not caps.fakePlayers Then
                If caps.hasUtf8PlayerList Then
                    packetCharset = Encoding.UTF8
                End If
                serverSend("\players\" & IIf(caps.hasXSQ, xsqSuffix, ""))
                .requestingPlayers = True
            ElseIf Not .hasVariables AndAlso caps.supportsVariables Then
                serverSend("\rules\" & IIf(caps.hasXSQ, xsqSuffix, ""))
                .requestingVariables = True

            Else
                .done = True
            End If


        End With
        lastActivity = Date.UtcNow
        serverRecord.LastCheck = lastActivity
    End Sub

    Private Sub serverSend(packet As String)
        Try
            socket.SendTo(addressQuery, packet)
            packetsSent += 1
        Catch e As Sockets.SocketException
            logDbg("ServerSendExc: " & e.Message)
            abortScan()
        End Try
    End Sub

    Private Sub packetReceived()
        With state
            If .requestingBasic Then
                parseBasic()
            ElseIf .requestingInfo Then
                parseInfo()
            ElseIf .requestingInfoExtended Then
                parseInfoExtended()
            ElseIf .requestingPlayers Then
                parsePlayers()
            ElseIf .requestingVariables Then
                parseVariables()
            Else
                'Debugger.Break()
            End If
        End With
        lastActivity = Date.UtcNow
        incomingPacket = Nothing
        resetRequestFlags()
        packetsReceived += 1
    End Sub

    Private Sub parseBasic()
        If Not incomingPacket.ContainsKey("gamename") Then
            logDbg("NoGamename: " & incomingPacketObj.ToString)
            abortScan()
            Return
        End If

        Dim serverRecord = saver.GetServerRecord()
        Dim gameName = incomingPacket("gamename").ToString().ToLower()
        Dim validServer As Boolean = False

        ' validate
        validServer = ValidateServer(gameName)

        If Not validServer Then
            logDbg("InvalidServer: " & incomingPacketObj.ToString)
            abortScan()
        End If

        If Not state.hasValidated Then
            serverRecord.LastValidation = Date.UtcNow
            state.hasValidated = True
        End If

        info = New Hashtable
        info("gamename") = incomingPacket("gamename")
        info("gamever") = incomingPacket("gamever")
        If incomingPacket.ContainsKey("minnetver") Then
            info("minnetver") = incomingPacket("minnetver")
        ElseIf incomingPacket.ContainsKey("mingamever") Then
            info("minnetver") = incomingPacket("mingamever")
        End If
        info("location") = incomingPacket("location")
        state.hasBasic = True
        caps.isOnline = True
        caps.version = info("gamever")
        caps.gameName = info("gamename")

        If gameName = "ut" Then
            caps.hasXSQ = True ' set this flag for initial polling with XSQ suffix
            caps.hasUtf8PlayerList = Integer.Parse(caps.version) >= 469
        End If

    End Sub

    Private Function ValidateServer(gameName As String) As Boolean
        If state.hasValidated Then
            Return True
        End If

        Dim validServer As Boolean

        If Not incomingPacket.ContainsKey("validate") Then
            validServer = False
        ElseIf incomingPacket("validate") = "Orange" Then ' dunno where does this come from
            validServer = True
        ElseIf Len(incomingPacket("validate")) <> 8 OrElse Not MasterServerManager.gamespyKeys.ContainsKey(gameName) Then
            validServer = False
        Else
            Dim expectedResponse = GsGetChallengeResponse(challenge, MasterServerManager.gamespyKeys(gameName).encKey)
            validServer = (expectedResponse = incomingPacket("validate"))
        End If

        Return validServer
    End Function

    Private Sub parseInfo()
        If Not incomingPacket.ContainsKey("hostname") Then
            logDbg("NoHostname: " & incomingPacket.ToString)
            abortScan()
        Else
            For Each packetKey As String In incomingPacket.Keys
                If packetKey.Substring(0, 2) = "__" Then
                    Continue For
                End If
                info(packetKey) = incomingPacket(packetKey)
            Next
            'If info.ContainsKey("hostport") AndAlso IsNumeric(info("hostport")) Then
            'addressQuery2 = getIp(addressQuery) & ":" & (Integer.Parse(info("hostport")) + 1)
            'End If
            caps.hasXSQ = info.ContainsKey("xserverquery")
            If caps.hasXSQ Then
                Integer.TryParse(Replace(info("xserverquery"), ".", ""), formatProvider, caps.XSQVersion)
                caps.hasPropertyInterface = False
                caps.timeTestPassed = False
            End If
            state.hasInfo = True
        End If
    End Sub

    Private Sub parseInfoExtended()
        Try
            If Not incomingPacket.ContainsKey("gamespeed") OrElse incomingPacket("numplayers") = "*Private*" Then
                Throw New Exception("Incorrect extended info (gamespeed/numplayers)")
            End If
            Single.TryParse(incomingPacket("gamespeed"), formatProvider, caps.gameSpeed)
            If caps.gameSpeed = 0 Then
                Throw New Exception("Incorrect extended info (gamespeed=0)")
            End If

            info("__uttrealplayers") = incomingPacket("numplayers")
            info("__uttspectators") = incomingPacket("numspectators")
            info("__uttgamespeed") = incomingPacket("gamespeed")
            info("__uttgamecurrentid") = incomingPacket("currentid")
            info("bgameended") = incomingPacket("bgameended")
            info("bovertime") = incomingPacket("bovertime")
            info("elapsedtime") = incomingPacket("elapsedtime")
            info("remainingtime") = incomingPacket("remainingtime")
            If incomingPacket.ContainsKey("timelimit") Then
                info("timelimit") = incomingPacket("timelimit")
            End If

            state.hasInfoExtended = True
            state.hasTimeTest = True
            caps.timeTestPassed = True

            ' fake players detection
            If info("numplayers") > incomingPacket("numplayers") + incomingPacket("numspectators") Then
                caps.fakePlayers = True
            End If

            If caps.gamemodeExtendedInfo Then
                gamemodeQuery.ParseInfoPacket(incomingPacket)
            End If
        Catch e As Exception
            state.hasTimeTest = True
            caps.timeTestPassed = False
            caps.hasPropertyInterface = False
        End Try
    End Sub

    Private Sub parsePlayers()
        Dim playerid As Integer = 0, suffix As String, playerinfo As Hashtable
        Dim buggedPingCount As Integer = 0 ' 2016-03-18: skip scanning of broken servers (all players with ping 9999)
        players = New List(Of Hashtable)
        Try
            Do While incomingPacket.ContainsKey("player_" & playerid)
                suffix = "_" & playerid
                playerinfo = New Hashtable
                playerinfo("name") = incomingPacket("player" & suffix)
                playerinfo("frags") = incomingPacket("frags" & suffix)
                playerinfo("mesh") = incomingPacket("mesh" & suffix)
                playerinfo("skin") = incomingPacket("skin" & suffix)
                playerinfo("face") = incomingPacket("face" & suffix)
                playerinfo("team") = incomingPacket("team" & suffix)

                Dim ping As Integer
                Dim pingString As String = incomingPacket("ping" & suffix)

                If IsNothing(pingString) OrElse Not Integer.TryParse(pingString, ping) Then
                    ping = 0
                End If

                playerinfo("ping") = ping

                If (ping > 100000) Then
                    buggedPingCount += 1
                End If

                If caps.hasXSQ Then
                    playerinfo("countryc") = incomingPacket("countryc" & suffix)
                    playerinfo("deaths") = incomingPacket("deaths" & suffix)
                    If caps.XSQVersion >= 200 Then
                        playerinfo("time") = incomingPacket("time" & suffix)
                    Else
                        playerinfo("time") = incomingPacket("time" & suffix) * 60
                    End If
                End If
                players.Add(playerinfo)
                playerid += 1
            Loop
            If buggedPingCount > players.Count / 2 Then
                abortScan()
            End If
        Catch e As Exception
            logDbg("ParsePlayersExc: " & e.Message)
        End Try
        state.hasPlayers = True
    End Sub

    Private Sub parseVariables()
        variables = incomingPacket.Clone()

        info("__utthaspropertyinterface") = caps.hasPropertyInterface
        info("__utttimetestpassed") = caps.timeTestPassed

        state.hasVariables = True
    End Sub

    Private Function skipStepIfOptional()
        With state
            If .requestingInfoExtended Then
                .requestingInfoExtended = False
                .hasTimeTest = True
                caps.hasPropertyInterface = False
                caps.timeTestPassed = False
                lastActivity = Date.UtcNow
                sendRequest()
                Return True
            ElseIf .requestingTimeTest Then
                .requestingTimeTest = False
                .hasTimeTest = True
                caps.timeTestPassed = False
                lastActivity = Date.UtcNow
                sendRequest()
                Return True

                ' workaround: XServerQuery not responding
            ElseIf .requestingInfo AndAlso caps.hasXSQ Then
                .requestingInfo = False
                caps.hasXSQ = False
                sendRequest()
                Return True
            ElseIf .requestingPlayers AndAlso caps.hasXSQ Then
                .requestingPlayers = False
                caps.hasXSQ = False
                sendRequest()
                Return True
            ElseIf .requestingVariables AndAlso caps.hasXSQ Then
                .requestingVariables = False
                caps.hasXSQ = False
                sendRequest()
                Return True

            ElseIf .requestingVariables Then
                .requestingVariables = False
                caps.supportsVariables = False
                .hasVariables = False
                sendRequest()
                Return True
            End If
        End With

        Return False
    End Function

    Private Sub resetRequestFlags()
        With state
            .requestingBasic = False
            .requestingInfo = False
            .requestingInfoExtended = False
            .requestingPlayers = False
            .requestingVariables = False
            .requestingTimeTest = False
        End With
    End Sub

    Private Function isInRequestState()
        With state
            Return .requestingBasic Or
            .requestingInfo Or
            .requestingInfoExtended Or
            .requestingPlayers Or
            .requestingVariables Or
            .requestingTimeTest
        End With
    End Function

    Friend Sub abortScan()
        If Not state.done Then
            state.done = True
            saver.state.done = True
            caps.isOnline = False
            If Not state.requestingBasic Then
                logDbg("Aborting scan (" & state.ToString & ")")
            End If
        End If
    End Sub

    Public Function GetPacketCharset() As Encoding
        Return packetCharset
    End Function

    Public Overrides Function ToString() As String
        Return "ServerQuery#" & addressQuery & "#"
    End Function

    Private Shared Function generateChallenge() As String
        Static allowedChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"
        Static allowedCharsLen = Len(allowedChars)
        Return allowedChars(rand(1, allowedCharsLen)) & allowedChars(rand(1, allowedCharsLen)) & allowedChars(rand(1, allowedCharsLen)) &
            allowedChars(rand(1, allowedCharsLen)) & allowedChars(rand(1, allowedCharsLen)) & allowedChars(rand(1, allowedCharsLen))
    End Function

    Private Shared Function rand(min As UInt32, max As UInt32) As UInt32
        Static randomGen = New System.Random()
        Return randomGen.next(min, max)
    End Function

    Protected Friend Sub logDbg(msg As String)
        scannerMaster.log.DebugWriteLine("ServerQuery[{0}]: {1}", addressQuery, msg)
    End Sub


    Public Structure ServerCapabilities
        Dim isOnline As Boolean
        Dim version As String
        Dim gameName As String
        Dim hasXSQ As Boolean
        Dim XSQVersion As Integer
        Dim hasPropertyInterface As Boolean
        Dim timeTestPassed As Boolean
        Dim gameSpeed As Single
        Dim supportsVariables As Boolean
        Dim gamemodeExtendedInfo As Boolean
        Dim fakePlayers As Boolean
        Dim hasUtf8PlayerList As Boolean

        Public Overrides Function ToString() As String
            ToString = "ServerCapabilities{ "
            If isOnline Then ToString &= "isOnline gameName=" & gameName & " version=" & version & " "
            If hasXSQ Then ToString &= "hasXSQ=" & XSQVersion & " "
            If hasPropertyInterface Then ToString &= "hasPropertyInterface "
            If timeTestPassed Then ToString &= "timeTestPassed gameSpeed=" & gameSpeed & " "
            ToString &= "}"
        End Function
    End Structure

End Class


Public Structure ServerQueryState
    Dim started As Boolean
    Dim starting As Boolean
    Dim hasValidated As Boolean
    Dim hasBasic As Boolean
    Dim hasInfo As Boolean
    Dim hasInfoExtended As Boolean
    Dim hasTimeTest As Boolean
    Dim hasPlayers As Boolean
    Dim hasVariables As Boolean

    Dim requestingBasic As Boolean
    Dim requestingInfo As Boolean
    Dim requestingInfoExtended As Boolean
    Dim requestingTimeTest As Boolean
    Dim requestingPlayers As Boolean
    Dim requestingVariables As Boolean

    Dim done As Boolean

    Public Overrides Function ToString() As String
        ToString = "ServerQueryState#"
        If requestingVariables Then
            ToString &= "requestingVariables"
        ElseIf requestingPlayers Then
            ToString &= "requestingPlayers"
        ElseIf requestingTimeTest Then
            ToString &= "requestingTimeTest"
        ElseIf requestingInfoExtended Then
            ToString &= "requestingInfoExtended"
        ElseIf requestingInfo Then
            ToString &= "requestingInfo"
        ElseIf requestingBasic Then
            ToString &= "requestingBasic"
        ElseIf done Then
            ToString &= "done"
        Else
            ToString &= "???"
        End If
        ToString &= "#"
    End Function
End Structure