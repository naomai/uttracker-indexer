Imports System.Net
Imports System.Globalization
Imports System.Text
Imports System.Text.RegularExpressions

Public Class ServerQuery
    Dim socket As SocketManager
    Public scannerMaster As Scanner


    Protected networkCapNextSendDeadline As Date
    Protected networkTimeoutDeadline As Date? = Nothing

    Protected nextInfoDeadline As Date = Date.UtcNow
    Protected nextGameStateDeadline As Date = Date.UtcNow
    Protected nextVerifyDeadline As Date = Date.UtcNow

    Protected challenge As String

    Private state As ServerQueryState

    Public isOnline As Boolean
    Public isActive As Boolean = False
    Public addressQuery As String
    Public addressGame As String
    Friend incomingPacket As UTQueryPacket
    Private resendAttempts As Integer = 0

    Protected protocolFailures As Integer = 0
    Friend lastActivity As Date

    Protected dto As ServerInfo
    Protected sync As ServerDataPersistence
    Protected gamemodeQuery As GamemodeSpecificQuery

    Private formatProvider = CultureInfo.InvariantCulture

    Protected packetCharset As Encoding = Encoding.GetEncoding(1252)
    Const INTERVAL_INFO As Integer = 10 * 60
    Const INTERVAL_STATE As Integer = 2 * 60
    Const INTERVAL_VERIFY As Integer = 24 * 60 * 60

    Const NETWORKCAP_SECONDS As Integer = 3 ' interval between requests
    Const NETWORK_TIMEOUT_SECONDS As Integer = 15


    Public Sub New(master As Scanner, serverAddress As String)
        addressQuery = serverAddress
        addressGame = JulkinNet.GetHost(serverAddress) & ":" &
            (JulkinNet.GetPort(serverAddress) - 1)

        scannerMaster = master
        dto = New ServerInfo()
        sync = New ServerDataPersistence(dto, Me)

        With dto.Capabilities
            .hasPropertyInterface = True
            .supportsVariables = True
        End With

        state.starting = False
        state.started = False

        networkCapNextSendDeadline = Date.UtcNow.AddMilliseconds(rand(0, 15000))
    End Sub

    Public Sub Update()
        CheckScanDeadlines()
        Tick()
        If Not sync.state.done Then
            sync.Tick()
        End If
    End Sub

    Protected Sub CheckScanDeadlines()
        Dim now = Date.UtcNow

        If protocolFailures >= 3 AndAlso now >= lastActivity.AddMinutes(30) Then
            protocolFailures = 0
        End If

        If state.starting OrElse isInRequestState() OrElse protocolFailures >= 3 Then
            Return
        End If

        Dim jitterMs As Integer
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
            jitterMs = 2000 - rand(0, 4000)
            nextInfoDeadline = now.AddSeconds(INTERVAL_INFO).AddMilliseconds(jitterMs)
            ' reload DB record (free irrelevant data)
            sync.state.hasDBRecord = False
            actionNeeded = True
        End If
        If nextGameStateDeadline <= now Then
            With state
                If dto.Capabilities.quickNumPlayers Then
                    .hasInfoExtended = False
                Else
                    .hasInfo = False
                    .hasPlayers = False
                End If
            End With
            jitterMs = 2000 - rand(0, 4000)
            nextGameStateDeadline = now.AddSeconds(INTERVAL_STATE).AddMilliseconds(jitterMs)
            actionNeeded = True
        End If

        If actionNeeded Then
            state.started = False
            state.done = False
            state.starting = True
            resetRequestFlags()
        End If
    End Sub

    Public Sub Tick()
        If state.done Then
            Return
        End If

        If state.starting Then
            state.started = True
            state.starting = False
            sendRequest()
            Return
        End If

        If Not IsNothing(incomingPacket) Then ' we received a full response from server
            Dim packet = incomingPacket
            incomingPacket = Nothing
            packetReceived(packet)
            Return
        End If

        Dim isTimedOut = Not IsNothing(networkTimeoutDeadline) _
                AndAlso Date.UtcNow >= networkTimeoutDeadline _
                AndAlso (Date.UtcNow - scannerMaster.scanLastTouchAll).TotalSeconds < 10

        If isTimedOut Then
            If Not skipStepIfOptional() Then
                abortScan("No response for required data", dumpCommLog:=True)
            End If
            Return
        End If

        sendRequest()

    End Sub

    Public Function getState() As ServerQueryState
        Return state
    End Function

    Public Sub setSocket(ByRef master As SocketManager)
        socket = master
    End Sub

    Private Sub sendRequest()
        Const xsqSuffix = "XServerQuery"

        If isInRequestState() Then Return ' remove this when implementing resend feature

        If Date.UtcNow < networkCapNextSendDeadline Then
            Return
        End If

        Dim serverRecord = sync.GetServerRecord()

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
                If dto.Capabilities.hasCp437Info Then
                    packetCharset = Encoding.GetEncoding(437)
                End If
                .requestingInfo = True
                sync.state.savedInfo = False
                sync.state.savedGameInfo = False
                sync.state.done = False
                dto.infoRequestTime = Date.UtcNow
                serverSend("\info\" & IIf(dto.Capabilities.hasXSQ, xsqSuffix, ""))
            ElseIf Not .hasInfoExtended AndAlso dto.Capabilities.hasPropertyInterface Then
                gamemodeQuery = GamemodeSpecificQuery.GetQueryObjectForContext(dto)
                Dim gamemodeAdditionalRequests As String = "", otherAdditionalRequests As String = ""
                If Not IsNothing(gamemodeQuery) Then
                    gamemodeAdditionalRequests = gamemodeQuery.GetInfoRequestString()
                    dto.Capabilities.gamemodeExtendedInfo = True
                End If
                If Not dto.Info.ContainsKey("timelimit") Then
                    otherAdditionalRequests &= "\game_property\TimeLimit\"
                End If

                .requestingInfoExtended = True
                dto.PropsRequestTime = Date.UtcNow ' AKA timestamp of sending the extended info request
                serverSend("\game_property\NumPlayers\\game_property\NumSpectators\" _
                           & "\game_property\GameSpeed\\game_property\CurrentID\" _
                           & "\game_property\bGameEnded\\game_property\bOvertime\" _
                           & "\game_property\ElapsedTime\\game_property\RemainingTime\" _
                           & "\level_property\Outer\" _
                           & otherAdditionalRequests _
                           & gamemodeAdditionalRequests)
                sync.state.savedInfo = False
                sync.state.savedGameInfo = False
                sync.state.done = False
            ElseIf Not .hasPlayers AndAlso dto.Info("numplayers") <> 0 AndAlso Not dto.Capabilities.fakePlayers Then
                If dto.Capabilities.hasUtf8PlayerList Then
                    packetCharset = Encoding.UTF8
                End If
                .requestingPlayers = True
                sync.state.savedPlayers = False
                sync.state.savedCumulativeStats = False
                sync.state.done = False
                serverSend("\players\" & IIf(dto.Capabilities.hasXSQ, xsqSuffix, ""))
            ElseIf Not .hasVariables AndAlso dto.Capabilities.supportsVariables Then
                .requestingVariables = True
                sync.state.savedVariables = False
                sync.state.done = False
                serverSend("\rules\")
            Else
                .done = True
                protocolFailures = 0
                networkTimeoutDeadline = Nothing
            End If


        End With
        lastActivity = Date.UtcNow
        dto.LastActivityTime = lastActivity
        networkCapNextSendDeadline = Date.UtcNow.AddSeconds(NETWORKCAP_SECONDS)
    End Sub

    Private Sub serverSend(packet As String)
        Try
            socket.SendTo(addressQuery, packet)
            scannerMaster.commLogWrite(addressQuery, "UUU", packet)
            networkTimeoutDeadline = Date.UtcNow.AddSeconds(NETWORK_TIMEOUT_SECONDS)

        Catch e As Sockets.SocketException
            abortScan("ServerSendException: " & e.Message)
        End Try
    End Sub

    Private Sub packetReceived(packet As UTQueryPacket)
        Try
            With state
                If .requestingBasic Then
                    parseBasic(packet)
                ElseIf .requestingInfo Then
                    parseInfo(packet)
                ElseIf .requestingInfoExtended Then
                    parseInfoExtended(packet)
                ElseIf .requestingPlayers Then
                    parsePlayers(packet)
                ElseIf .requestingVariables Then
                    parseVariables(packet)
                End If
            End With
        Catch e As UTQueryValidationException
            abortScan("Invalid data received from server: " & e.Message)
        End Try
        lastActivity = Date.UtcNow
        networkTimeoutDeadline = Nothing
        resetRequestFlags()
    End Sub

    Private Sub parseBasic(packetObj As UTQueryPacket)

        Dim packet = ServerQueryValidators.basic.Validate(packetObj)

        Dim gameName = packet("gamename").ToLower()
        Dim validServer As Boolean = False

        ' validate
        validServer = ValidateServer(packetObj, gameName)

        If Not validServer Then
            abortScan("Challenge validation failed")
        End If

        If Not state.hasValidated Then
            dto.LastValidationTime = Date.UtcNow
            state.hasValidated = True

        End If


        dto.Info("gamename") = packet("gamename")
        dto.Info("gamever") = packet("gamever")
        If packet.ContainsKey("minnetver") Then
            dto.Info("minnetver") = packet("minnetver")
        ElseIf packet.ContainsKey("mingamever") Then
            dto.Info("minnetver") = packet("mingamever")
        End If
        dto.Info("location") = packet("location")
        state.hasBasic = True
        isOnline = True
        dto.Capabilities.version = dto.Info("gamever")
        dto.Capabilities.gameName = dto.Info("gamename")

        If gameName = "ut" Then
            dto.Capabilities.hasXSQ = True ' set this flag for initial polling with XSQ suffix
            dto.Capabilities.hasUtf8PlayerList = Integer.Parse(dto.Capabilities.version) >= 469
        ElseIf gameName = "unreal" Then
            dto.Capabilities.hasCp437Info = True
            dto.Capabilities.hasPropertyInterface = False
        End If

    End Sub

    Private Function ValidateServer(packetObj As UTQueryPacket, gameName As String) As Boolean
        If state.hasValidated Then
            Return True
        End If

        Dim validServer As Boolean

        Try
            Dim packet = ServerQueryValidators.challenge.Validate(packetObj)
            If packet("validate") = "Orange" Then ' dunno where does this come from
                validServer = True
            ElseIf Len(packet("validate")) <> 8 OrElse Not MasterServerManager.gamespyKeys.ContainsKey(gameName) Then
                validServer = False
            Else
                Dim expectedResponse = GameSpyProtocol.GenerateValidateResponse(challenge, MasterServerManager.gamespyKeys(gameName).encKey)
                validServer = (expectedResponse = packet("validate"))
            End If
        Catch e As UTQueryValidationException
            validServer = False
        End Try

        Return validServer
    End Function

    Private Sub parseInfo(packetObj As UTQueryPacket)
        Dim validated = ServerQueryValidators.info.Validate(packetObj)

        For Each pair In packetObj
            If pair.key.Substring(0, 2) = "__" Then
                Continue For
            End If
            dto.Info(pair.key) = pair.value
        Next
        If validated.ContainsKey("hostport") Then
            addressGame = JulkinNet.GetHost(addressQuery) & ":" & validated("hostport")
        End If
        dto.Capabilities.hasXSQ = packetObj.ContainsKey("xserverquery")
        If dto.Capabilities.hasXSQ Then
            Dim xsqVersion As Integer
            Integer.TryParse(Replace(packetObj("xserverquery"), ".", ""), formatProvider, xsqVersion)
            ' property interface brought back in XServerQuery 211fix4
            dto.Capabilities.hasPropertyInterface = xsqVersion >= 211
            dto.Capabilities.XSQVersion = xsqVersion
        End If

        dto.Info("__uttrealplayers") = validated("numplayers") ' might be overwritten later
        state.hasInfo = True
        state.hasNumPlayers = True
    End Sub

    Private Sub parseInfoExtended(packetObj As UTQueryPacket)
        Try
            Dim validated = ServerQueryValidators.infoExtended.Validate(packetObj)

            dto.Capabilities.quickNumPlayers = True

            Dim needsUpdatingPlayerList = validated("numplayers") > 0 OrElse
                validated("numspectators") > 0

            If needsUpdatingPlayerList Then
                state.hasPlayers = False
            End If

            dto.Info("__uttrealplayers") = validated("numplayers")
            dto.Info("__uttspectators") = validated("numspectators")
            dto.Info("__uttgamespeed") = validated("gamespeed")
            dto.Info("__uttgamecurrentid") = validated("currentid")
            dto.Info("bgameended") = validated("bgameended")
            dto.Info("bovertime") = validated("bovertime")
            dto.Info("elapsedtime") = validated("elapsedtime")
            dto.Info("remainingtime") = validated("remainingtime")
            If validated.ContainsKey("timelimit") Then
                dto.Info("timelimit") = validated("timelimit")
            End If

            'state.hasInfo = True
            state.hasInfoExtended = True
            sync.state.savedVariables = False

            Dim mapName = Regex.Match(validated("outer"), "^Package'(.+)'$").Groups(1).ToString()
            If mapName <> dto.Info("mapname") Then
                state.hasInfo = False
            End If




            ' fake players detection
            If dto.Info("numplayers") > validated("numplayers") + validated("numspectators") Then
                dto.Capabilities.fakePlayers = True
            End If

            If dto.Capabilities.gamemodeExtendedInfo Then
                gamemodeQuery.ParseInfoPacket(packetObj.ConvertToHashtablePacket())
            End If

            state.hasNumPlayers = True
        Catch e As Exception
            dto.Capabilities.hasPropertyInterface = False
        End Try
    End Sub

    Private Sub parsePlayers(packetObj As UTQueryPacket)
        Dim playerid As Integer = 0, playerinfo As Dictionary(Of String, String)
        Dim buggedPingCount As Integer = 0 ' 2016-03-18: skip scanning of broken servers (all players with ping 9999)
        Dim validated = ServerQueryValidators.players.Validate(packetObj)

        Try
            dto.Players.Clear()

            For Each pair As KeyValuePair(Of Integer, Object) In validated("player")
                playerid = pair.Key

                ' validate

                If validated("ngsecret").ContainsKey(playerid) AndAlso validated("ngsecret")(playerid) = "bot" Then
                    ' skip entries marked as bots
                    playerid += 1
                    Continue For
                End If


                playerinfo = New Dictionary(Of String, String)
                playerinfo("name") = validated("player")(playerid)
                playerinfo("team") = validated("team")(playerid)
                If validated("frags").ContainsKey(playerid) Then
                    playerinfo("frags") = validated("frags")(playerid)
                Else
                    playerinfo("frags") = 0
                End If

                If playerinfo("team") = "255" Then
                    playerinfo("frags") = 0
                End If
                playerinfo("mesh") = validated("mesh")(playerid)
                playerinfo("skin") = validated("skin")(playerid)
                playerinfo("face") = validated("face")(playerid)

                playerinfo("ping") = validated("ping")(playerid)

                If (playerinfo("ping") > 100000) Then
                    buggedPingCount += 1
                End If

                playerinfo("countryc") = validated("countryc")(playerid)
                playerinfo("deaths") = validated("deaths")(playerid)

                If dto.Capabilities.hasXSQ Then
                    If dto.Capabilities.XSQVersion >= 200 Then
                        playerinfo("time") = validated("time")(playerid)
                    Else
                        playerinfo("time") = validated("time")(playerid) * 60
                    End If
                End If
                dto.Players.Add(playerinfo)
                playerid += 1
            Next
            If buggedPingCount > dto.Players.Count / 2 Then
                abortScan("Frozen/glitched server")
            End If
        Catch e As Exception
            logDbg("ParsePlayersExc: " & e.Message)
        End Try
        state.hasPlayers = True
    End Sub

    Private Sub parseVariables(packetObj As UTQueryPacket)
        dto.Variables = packetObj.ConvertToDictionary()

        dto.Info("__utthaspropertyinterface") = dto.Capabilities.hasPropertyInterface

        state.hasVariables = True
    End Sub

    Private Function skipStepIfOptional()
        With state
            If .requestingInfoExtended Then
                .requestingInfoExtended = False
                dto.Capabilities.hasPropertyInterface = False
                lastActivity = Date.UtcNow
                sendRequest()
                Return True
            ElseIf .requestingTimeTest Then
                .requestingTimeTest = False
                lastActivity = Date.UtcNow
                sendRequest()
                Return True

            ElseIf .requestingInfo AndAlso dto.Capabilities.hasXSQ Then
                ' workaround: XServerQuery not responding
                .requestingInfo = False
                dto.Capabilities.hasXSQ = False
                sendRequest()
                Return True

            ElseIf .requestingVariables Then
                .requestingVariables = False
                dto.Capabilities.supportsVariables = False
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
            If Not state.requestingBasic Then
                protocolFailures += 1
                logDbg("#" & protocolFailures & " Aborting scan (" & reason & ") - " & state.ToString)
                If dumpCommLog Then
                    'logDbg("CommLog: " & System.Environment.NewLine &
                    '   scannerMaster._targetCommLog(addressQuery))
                End If
            End If

        End If
    End Sub


    ''' <summary>
    ''' Estimate match start time from server data 
    ''' </summary>
    ''' <returns>
    ''' On success: Date object in the past representing beginning of the match
    ''' When match is not yet started: Date object one year into the future
    ''' When beginning cannot be estimated: null
    ''' </returns>
    Public Function GetEstimatedMatchStartTime() As Date?
        Dim correctElapsedTime = IsNumeric(dto.Info("elapsedtime")) AndAlso
               dto.Info("elapsedtime") > 0

        Dim correctTimeLimit = IsNumeric(dto.Info("timelimit")) AndAlso
            dto.Info("timelimit") > 0 AndAlso
            (dto.Info("timelimit") * 60) - dto.Info("remainingtime") > 0

        Dim secondsElapsed As Integer = Nothing


        If correctElapsedTime Then
            secondsElapsed = dto.Info("elapsedtime")
        ElseIf correctTimeLimit Then
            secondsElapsed = (dto.Info("timelimit") * 60) - dto.Info("remainingtime")
        Else
            secondsElapsed = Nothing
        End If

        Dim isNotStarted = (secondsElapsed = 0)
        If isNotStarted Then
            Return Date.UtcNow.AddYears(1)
        End If

        If Not IsNothing(secondsElapsed) Then
            Return dto.PropsRequestTime.AddSeconds(-secondsElapsed)
        End If

        Return Nothing

    End Function

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
        Static randomGen = New Random()
        Return randomGen.next(min, max)
    End Function

    Protected Friend Sub logDbg(msg As String)
        scannerMaster.log.DebugWriteLine("ServerQuery[{0}]: {1}", addressQuery, msg)
    End Sub



    Private Class ServerQueryValidators
        Public Shared ReadOnly basic = UTQueryValidator.FromRuleDict(New Dictionary(Of String, String) From {
                             {"gamename", "required|string"},
                             {"gamever", "required|string"},
                             {"minnetver", "string"},
                             {"mingamever", "string"},
                             {"location", "integer"}
                            })

        Public Shared ReadOnly challenge = UTQueryValidator.FromRuleDict(New Dictionary(Of String, String) From {
            {"validate", "required|string|gte:6|lte:8"}
         })
        Public Shared ReadOnly info = UTQueryValidator.FromRuleDict(New Dictionary(Of String, String) From {
                         {"hostname", "required|string"},
                         {"mapname", "required|string"},
                         {"numplayers", "required|integer|gte:0"},
                         {"maxplayers", "required|integer|gte:1"},
                         {"hostport", "integer|gte:1|lte:65535"}
                        })

        Public Shared ReadOnly infoExtended = UTQueryValidator.FromRuleDict(New Dictionary(Of String, String) From {
                         {"gamespeed", "required|float|gt:0"},
                         {"numplayers", "required|integer|gte:0"},
                         {"numspectators", "integer|gte:0"},
                         {"currentid", "integer|gte:0"},
                         {"elapsedtime", "integer|gte:0"},
                         {"remainingtime", "integer|gte:0"},
                         {"timelimit", "integer|gte:0"},
                         {"bgameended", "boolean"},
                         {"bovertime", "boolean"},
                         {"outer", "string"}
                        })
        Public Shared ReadOnly players = UTQueryValidator.FromRuleDict(New Dictionary(Of String, String) From {
                         {"player", "array:string|gt:0"},
                         {"team", "array:integer"},
                         {"frags", "array:integer|default:0"},
                         {"ping", "array:integer|default:0"},
                         {"mesh", "array:string|nullable"},
                         {"skin", "array:string|nullable"},
                         {"face", "array:string|nullable"},
                         {"countryc", "array:string|nullable|default:none"},
                         {"deaths", "array:integer|default:0"},
                         {"time", "array:integer"},
                         {"ngsecret", "array:string"}
                        })
    End Class
End Class


Public Structure ServerQueryState
    Dim started As Boolean
    Dim starting As Boolean
    Dim hasValidated As Boolean
    Dim hasBasic As Boolean
    Dim hasInfo As Boolean
    Dim hasNumPlayers As Boolean
    Dim hasTeams As Boolean
    Dim hasInfoExtended As Boolean
    Dim hasTimeTest As Boolean
    Dim hasPlayers As Boolean
    Dim hasVariables As Boolean

    Dim requestingBasic As Boolean
    Dim requestingInfo As Boolean
    Dim requestingInfoShort As Boolean
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
        ElseIf requestingInfoShort Then
            ToString &= "requestingInfoShort"
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

