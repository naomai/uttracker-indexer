Imports System.Net
Imports System.Globalization
Imports System.Text
Imports Naomai.UTT.Indexer.Utt2Database

Public Class ServerQuery
    Dim socket As SocketManager
    Public scannerMaster As Scanner

    Public firstTimeTest, secondTimeTest As Single
    Public firstTimeTestLocal, secondTimeTestLocal, infoSentTimeLocal As DateTime

    Protected nextInfoDeadline As Date = Date.UtcNow
    Protected nextGameStateDeadline As Date = Date.UtcNow
    Protected nextVerifyDeadline As Date = Date.UtcNow

    Public packetsSent As Integer = 0
    Public packetsReceived As Integer = 0
    Protected challenge As String

    Private state As ServerQueryState

    Public isOnline As Boolean
    Public isActive As Boolean = False
    Public addressQuery As String
    Public addressGame As String
    Public incomingPacket As Hashtable
    Friend incomingPacketObj As UTQueryPacket
    Private resendAttempts As Integer = 0

    Protected protocolFailures As Integer = 0
    Friend lastActivity As Date

    Protected server As ServerInfo
    Protected sync As ServerInfoSync
    Protected gamemodeQuery As GamemodeSpecificQuery

    Private formatProvider = CultureInfo.InvariantCulture

    Protected packetCharset As Encoding = Encoding.GetEncoding(1252)
    Const INTERVAL_INFO As Integer = 10 * 60
    Const INTERVAL_STATE As Integer = 2 * 60
    Const INTERVAL_VERIFY As Integer = 24 * 60 * 60

    Public Sub New(master As Scanner, serverAddress As String)
        addressQuery = serverAddress
        addressGame = JulkinNet.GetHost(serverAddress) & ":" &
            (JulkinNet.GetPort(serverAddress) - 1)

        scannerMaster = master
        server = New ServerInfo()
        sync = New ServerInfoSync(server, Me)

        With server.caps
            .hasPropertyInterface = True
            .supportsVariables = True
        End With

        state.starting = False
        state.started = False


    End Sub

    Public Sub Update()
        CheckScanDeadlines()
        Tick()
    End Sub

    Protected Sub CheckScanDeadlines()
        Dim now = Date.UtcNow

        If protocolFailures >= 3 AndAlso now >= lastActivity.AddMinutes(30) Then
            protocolFailures = 0
        End If

        If state.starting OrElse isInRequestState() OrElse protocolFailures >= 3 Then
            Return
        End If

        Dim actionNeeded = False
        If nextVerifyDeadline <= now Then
            state.hasValidated = False
            nextVerifyDeadline = Date.UtcNow.AddSeconds(INTERVAL_VERIFY)
            actionNeeded = True
        End If
        If nextInfoDeadline <= now Then
            With state
                .hasBasic = False
                .hasInfo = False
                .hasInfoExtended = False
                .hasVariables = False
            End With
            nextInfoDeadline = now.AddSeconds(INTERVAL_INFO)
            actionNeeded = True
        End If
        If nextGameStateDeadline <= now Then
            With state
                .hasInfo = False
                .hasInfoExtended = False
                .hasVariables = False
                .hasPlayers = False
            End With
            nextGameStateDeadline = now.AddSeconds(INTERVAL_STATE)
            actionNeeded = True
        End If

        If actionNeeded Then
            'logDbg("Wakeup STA=" & state.ToString())
            state.started = False
            state.done = False
            state.starting = True
            resetRequestFlags()
        End If
    End Sub

    Public Sub Tick()
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
                    If (Date.UtcNow - lastActivity).TotalSeconds > 20 AndAlso (Date.UtcNow - scannerMaster.scanLastTouchAll).TotalSeconds < 5 Then
                        If Not skipStepIfOptional() Then
                            abortScan("No response for required data", dumpCommLog:=True)
                        End If
                    End If
                End If
            End If
        End If
        If Not sync.state.done Then
            sync.Tick()
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
        Dim serverRecord = sync.GetServerRecord()


        If isInRequestState() Then Return ' remove this when implementing resend feature



        With state
            If Not .hasBasic Then
                Dim challengeSuffix = ""
                If Not .hasValidated Then
                    challenge = generateChallenge()
                    challengeSuffix = "\secure\" & challenge
                End If
                .requestingBasic = True
                serverSend("\basic\" & challengeSuffix)

            ElseIf Not .hasInfo Then
                If server.caps.hasCp437Info Then
                    packetCharset = Encoding.GetEncoding(437)
                End If
                .requestingInfo = True
                sync.state.savedInfo = False
                sync.state.savedGameInfo = False
                sync.state.done = False
                infoSentTimeLocal = Date.UtcNow
                serverSend("\info\" & IIf(server.caps.hasXSQ, xsqSuffix, ""))
            ElseIf Not .hasInfoExtended AndAlso Not .hasTimeTest AndAlso server.caps.hasPropertyInterface Then
                firstTimeTestLocal = Date.UtcNow ' AKA timestamp of sending the extended info request
                gamemodeQuery = GamemodeSpecificQuery.GetQueryObjectForContext(server)
                Dim gamemodeAdditionalRequests As String = "", otherAdditionalRequests As String = ""
                If Not IsNothing(gamemodeQuery) Then
                    gamemodeAdditionalRequests = gamemodeQuery.GetInfoRequestString()
                    server.caps.gamemodeExtendedInfo = True
                End If
                If Not server.info.ContainsKey("timelimit") Then
                    otherAdditionalRequests &= "\game_property\TimeLimit\"
                End If

                .requestingInfoExtended = True
                serverSend("\game_property\NumPlayers\\game_property\NumSpectators\" _
                           & "\game_property\GameSpeed\\game_property\CurrentID\" _
                           & "\game_property\bGameEnded\\game_property\bOvertime\" _
                           & "\game_property\ElapsedTime\\game_property\RemainingTime\" _
                           & otherAdditionalRequests _
                           & gamemodeAdditionalRequests)
            ElseIf Not .hasPlayers AndAlso server.info("numplayers") <> 0 AndAlso Not server.caps.fakePlayers Then
                If server.caps.hasUtf8PlayerList Then
                    packetCharset = Encoding.UTF8
                End If
                .requestingPlayers = True
                sync.state.savedPlayers = False
                sync.state.savedCumulativeStats = False
                sync.state.done = False
                serverSend("\players\" & IIf(server.caps.hasXSQ, xsqSuffix, ""))
            ElseIf Not .hasVariables AndAlso server.caps.supportsVariables Then
                .requestingVariables = True
                sync.state.savedVariables = False
                sync.state.done = False
                serverSend("\rules\" & IIf(server.caps.hasXSQ, xsqSuffix, ""))
            Else
                .done = True
                protocolFailures = 0
            End If


        End With
        lastActivity = Date.UtcNow
        server.lastActivity = lastActivity
    End Sub

    Private Sub serverSend(packet As String)
        Try
            socket.SendTo(addressQuery, packet)
            packetsSent += 1
            scannerMaster.commLogWrite(addressQuery, "UUU", packet)
            ' logDbg("Send STA=" & state.ToString() & " RQ=" & packet)

        Catch e As Sockets.SocketException
            abortScan("ServerSendException: " & e.Message)
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
            'logDbg("NoGamename: " & incomingPacketObj.ToString)
            abortScan("No GameName")
            Return
        End If

        'Dim serverRecord = sync.GetServerRecord()
        Dim gameName = incomingPacket("gamename").ToString().ToLower()
        Dim validServer As Boolean = False

        ' validate
        validServer = ValidateServer(gameName)

        If Not validServer Then
            'logDbg("InvalidServer: " & incomingPacketObj.ToString)
            abortScan("Challenge validation failed")
        End If

        If Not state.hasValidated Then
            server.lastValidation = Date.UtcNow
            state.hasValidated = True

        End If


        server.info("gamename") = incomingPacket("gamename")
        server.info("gamever") = incomingPacket("gamever")
        If incomingPacket.ContainsKey("minnetver") Then
            server.info("minnetver") = incomingPacket("minnetver")
        ElseIf incomingPacket.ContainsKey("mingamever") Then
            server.info("minnetver") = incomingPacket("mingamever")
        End If
        server.info("location") = incomingPacket("location")
        state.hasBasic = True
        isOnline = True
        server.caps.version = server.info("gamever")
        server.caps.gameName = server.info("gamename")

        If gameName = "ut" Then
            server.caps.hasXSQ = True ' set this flag for initial polling with XSQ suffix
            server.caps.hasUtf8PlayerList = Integer.Parse(server.caps.version) >= 469
        ElseIf gameName = "unreal" Then
            server.caps.hasCp437Info = True
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
        If Not incomingPacket.ContainsKey("hostname") OrElse
            Not incomingPacket.ContainsKey("mapname") OrElse
            Not incomingPacket.ContainsKey("numplayers") OrElse
            Not incomingPacket.ContainsKey("maxplayers") Then
            'logDbg("MissingFields: " & incomingPacket.ToString)
            abortScan("Missing required fields in packet", dumpCommLog:=True)
            Return
        End If
        For Each packetKey As String In incomingPacket.Keys
            If packetKey.Substring(0, 2) = "__" Then
                Continue For
            End If
            server.info(packetKey) = incomingPacket(packetKey)
        Next
        If incomingPacket.ContainsKey("hostport") AndAlso IsNumeric(incomingPacket("hostport")) Then
            addressGame = JulkinNet.GetHost(addressQuery) & ":" &
                Integer.Parse(incomingPacket("hostport"))
        End If
        server.caps.hasXSQ = incomingPacket.ContainsKey("xserverquery")
        If server.caps.hasXSQ Then
            Integer.TryParse(Replace(incomingPacket("xserverquery"), ".", ""), formatProvider, server.caps.XSQVersion)
            server.caps.hasPropertyInterface = False
            server.caps.timeTestPassed = False
        End If
        state.hasInfo = True
    End Sub

    Private Sub parseInfoExtended()
        Try
            If Not incomingPacket.ContainsKey("gamespeed") OrElse incomingPacket("numplayers") = "*Private*" Then
                Throw New Exception("Incorrect extended info (gamespeed/numplayers)")
            End If
            Single.TryParse(incomingPacket("gamespeed"), formatProvider, server.caps.gameSpeed)
            If server.caps.gameSpeed = 0 Then
                Throw New Exception("Incorrect extended info (gamespeed=0)")
            End If

            server.info("__uttrealplayers") = incomingPacket("numplayers")
            server.info("__uttspectators") = incomingPacket("numspectators")
            server.info("__uttgamespeed") = incomingPacket("gamespeed")
            server.info("__uttgamecurrentid") = incomingPacket("currentid")
            server.info("bgameended") = incomingPacket("bgameended")
            server.info("bovertime") = incomingPacket("bovertime")
            server.info("elapsedtime") = incomingPacket("elapsedtime")
            server.info("remainingtime") = incomingPacket("remainingtime")
            If incomingPacket.ContainsKey("timelimit") Then
                server.info("timelimit") = incomingPacket("timelimit")
            End If

            state.hasInfoExtended = True
            state.hasTimeTest = True
            server.caps.timeTestPassed = True

            ' fake players detection
            If server.info("numplayers") > incomingPacket("numplayers") + incomingPacket("numspectators") Then
                server.caps.fakePlayers = True
            End If

            If server.caps.gamemodeExtendedInfo Then
                gamemodeQuery.ParseInfoPacket(incomingPacket)
            End If
        Catch e As Exception
            state.hasTimeTest = True
            server.caps.timeTestPassed = False
            server.caps.hasPropertyInterface = False
        End Try
    End Sub

    Private Sub parsePlayers()
        Dim playerid As Integer = 0, suffix As String, playerinfo As Hashtable
        Dim buggedPingCount As Integer = 0 ' 2016-03-18: skip scanning of broken servers (all players with ping 9999)

        Try
            Do While incomingPacket.ContainsKey("player_" & playerid)
                suffix = "_" & playerid

                ' validate
                Dim parsedTmp As Long
                If Not incomingPacket.ContainsKey("player" & suffix) OrElse
                    Not incomingPacket.ContainsKey("team" & suffix) OrElse
                    Not Long.TryParse(incomingPacket("team" & suffix), parsedTmp) Then
                    abortScan("Player response is invalid ")
                End If


                playerinfo = New Hashtable
                playerinfo("name") = incomingPacket("player" & suffix)
                playerinfo("team") = incomingPacket("team" & suffix)
                playerinfo("frags") = incomingPacket("frags" & suffix)
                If playerinfo("team") = "255" Then
                    playerinfo("frags") = 0
                End If
                playerinfo("mesh") = incomingPacket("mesh" & suffix)
                playerinfo("skin") = incomingPacket("skin" & suffix)
                playerinfo("face") = incomingPacket("face" & suffix)


                Dim ping As Long
                Dim pingString As String = incomingPacket("ping" & suffix)

                If IsNothing(pingString) OrElse Not Long.TryParse(pingString, ping) Then
                    ping = 0
                End If

                playerinfo("ping") = ping

                If (ping > 100000) Then
                    buggedPingCount += 1
                End If

                If server.caps.hasXSQ Then
                    playerinfo("countryc") = incomingPacket("countryc" & suffix)
                    playerinfo("deaths") = incomingPacket("deaths" & suffix)
                    If server.caps.XSQVersion >= 200 Then
                        playerinfo("time") = incomingPacket("time" & suffix)
                    Else
                        playerinfo("time") = incomingPacket("time" & suffix) * 60
                    End If
                End If
                server.players.Add(playerinfo)
                playerid += 1
            Loop
            If buggedPingCount > server.players.Count / 2 Then
                abortScan("Frozen/glitched server")
            End If
        Catch e As Exception
            logDbg("ParsePlayersExc: " & e.Message)
        End Try
        state.hasPlayers = True
    End Sub

    Private Sub parseVariables()
        server.variables = incomingPacket.Clone()

        server.info("__utthaspropertyinterface") = server.caps.hasPropertyInterface
        server.info("__utttimetestpassed") = server.caps.timeTestPassed

        state.hasVariables = True
    End Sub

    Private Function skipStepIfOptional()
        With state
            If .requestingInfoExtended Then
                .requestingInfoExtended = False
                .hasTimeTest = True
                server.caps.hasPropertyInterface = False
                server.caps.timeTestPassed = False
                lastActivity = Date.UtcNow
                sendRequest()
                Return True
            ElseIf .requestingTimeTest Then
                .requestingTimeTest = False
                .hasTimeTest = True
                server.caps.timeTestPassed = False
                lastActivity = Date.UtcNow
                sendRequest()
                Return True

                ' workaround: XServerQuery not responding
            ElseIf .requestingInfo AndAlso server.caps.hasXSQ Then
                .requestingInfo = False
                server.caps.hasXSQ = False
                sendRequest()
                Return True
            ElseIf .requestingPlayers AndAlso server.caps.hasXSQ Then
                .requestingPlayers = False
                server.caps.hasXSQ = False
                sendRequest()
                Return True
            ElseIf .requestingVariables AndAlso server.caps.hasXSQ Then
                .requestingVariables = False
                server.caps.hasXSQ = False
                sendRequest()
                Return True

            ElseIf .requestingVariables Then
                .requestingVariables = False
                server.caps.supportsVariables = False
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
            Return Not .done AndAlso (
                .requestingBasic OrElse
                .requestingInfo OrElse
                .requestingInfoExtended OrElse
                .requestingPlayers OrElse
                .requestingVariables OrElse
                .requestingTimeTest
            )
        End With
    End Function

    Friend Sub abortScan(Optional reason As String = "?", Optional dumpCommLog As Boolean = False)
        If Not state.done Then
            state.done = True
            sync.state.done = True
            isOnline = False
            'isActive = False
            If Not state.requestingBasic Then
                protocolFailures += 1
                logDbg("#" & protocolFailures & " Aborting scan (" & reason & ") - " & state.ToString)
                If dumpCommLog Then
                    logDbg("CommLog: " & System.Environment.NewLine &
                        scannerMaster._targetCommLog(addressQuery))
                End If
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
        ElseIf starting Then
            ToString &= "starting"
        ElseIf started Then
            ToString &= "started"
        Else
            ToString &= "???"
        End If
        ToString &= "#"
    End Function
End Structure