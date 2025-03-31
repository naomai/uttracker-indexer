Imports System.Net
Imports System.Globalization
Imports System.Text
Imports System.Text.RegularExpressions
Imports K4os.Compression.LZ4.Internal
Imports Microsoft.EntityFrameworkCore.Metadata
Imports Org.BouncyCastle.Bcpg

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
    Public checkFakePlayers As Boolean = False
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
            .HasPropertyInterface = True
            .SupportsVariables = True
            .CompoundRequest = True
        End With

        state.IsStarting = False
        state.IsStarted = False

        networkCapNextSendDeadline = Date.UtcNow.AddMilliseconds(rand(0, 15000))
    End Sub

    Public Sub Update()
        Try
            CheckScanDeadlines()
            Tick()
            sync.Tick()
        Catch e As Exception
            abortScan(e.Message)
        End Try
    End Sub

    Protected Sub CheckScanDeadlines()
        Dim now = Date.UtcNow

        If protocolFailures >= 3 AndAlso now >= lastActivity.AddMinutes(30) Then
            protocolFailures = 0
        End If

        If state.IsStarting OrElse isInRequestState() OrElse protocolFailures >= 3 Then
            Return
        End If

        Dim jitterMs As Integer
        Dim actionNeeded = False
        If nextVerifyDeadline <= now Then
            state.HasValidated = False
            nextVerifyDeadline = Date.UtcNow.AddSeconds(INTERVAL_VERIFY)
            actionNeeded = True
        End If
        If nextInfoDeadline <= now Then
            With state
                .HasBasic = False
                .HasInfo = False
                .HasInfoExtended = False
                .HasVariables = False
            End With
            jitterMs = 2000 - rand(0, 4000)
            nextInfoDeadline = now.AddSeconds(INTERVAL_INFO).AddMilliseconds(jitterMs)
            ' reload DB record (free irrelevant data)
            sync.InvalidateServerRecord()
            dto.Info = New Dictionary(Of String, String)()
            actionNeeded = True
        End If
        If nextGameStateDeadline <= now Then
            With state
                If dto.Capabilities.QuickNumPlayers Then
                    .HasInfoExtended = False
                Else
                    .HasInfo = False
                    .HasPlayers = False
                End If
            End With
            jitterMs = 2000 - rand(0, 4000)
            nextGameStateDeadline = now.AddSeconds(INTERVAL_STATE).AddMilliseconds(jitterMs)
            actionNeeded = True
        End If

        If actionNeeded Then
            state.IsStarted = False
            state.done = False
            state.IsStarting = True
            resetRequestFlags()
        End If
    End Sub

    Public Sub Tick()
        If state.done Then
            Return
        End If

        If state.IsStarting Then
            state.IsStarted = True
            state.IsStarting = False
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

        Dim allowCompoundRequest As Boolean = dto.Capabilities.CompoundRequest AndAlso state.HasProbed
        Dim allowMoreRequests As Boolean = allowCompoundRequest

        Dim request As New ServerRequest()

        With state
            If Not .HasBasic Then
                request.Add("basic", "")
                If Not .HasValidated Then
                    challenge = generateChallenge()
                    request.Add("secure", challenge)
                End If
                If Not .HasProbed Then
                    request.Add("echo", generateChallenge())
                    request.Add("game_property", "NumPlayers")
                End If

                .RequestingBasic = True
            ElseIf Not .HasInfo Then
                Dim packet As New UTQueryPacket(flags:=UTQueryPacket.Flags.UTQP_SimpleRequest)
                If dto.Capabilities.HasCp437Info Then
                    packetCharset = Encoding.GetEncoding(437)
                End If
                .RequestingInfo = True
                sync.InvalidateInfo()
                dto.InfoRequestTime = Date.UtcNow
                packet.Add("info", IIf(dto.Capabilities.HasXsq, xsqSuffix, ""))
                request.Add(packet)

            ElseIf Not .HasInfoExtended AndAlso dto.Capabilities.HasPropertyInterface Then
                gamemodeQuery = GamemodeSpecificQuery.GetQueryObjectForContext(dto)
                Dim gamemodeAdditionalRequests As String = "", otherAdditionalRequests As String = ""
                If Not IsNothing(gamemodeQuery) Then
                    gamemodeAdditionalRequests = gamemodeQuery.GetInfoRequestString()
                    dto.Capabilities.GamemodeExtendedInfo = True
                End If
                If Not dto.Info.ContainsKey("timelimit") Then
                    request.Add("\game_property\TimeLimit\")
                End If

                .RequestingInfoExtended = True
                dto.PropsRequestTime = Date.UtcNow ' AKA timestamp of sending the extended info request
                request.Add("\game_property\NumPlayers\")
                request.Add("\game_property\NumSpectators\")
                request.Add("\game_property\GameSpeed\")
                request.Add("\game_property\CurrentID\")
                request.Add("\game_property\bGameEnded\")
                request.Add("\game_property\bOvertime\")
                request.Add("\game_property\ElapsedTime\")
                request.Add("\game_property\RemainingTime\")
                request.Add("\level_property\Outer\")

                sync.InvalidateInfo()
            ElseIf Not .HasPlayers AndAlso dto.Info("numplayers") <> 0 AndAlso Not dto.Capabilities.FakePlayers Then
                Dim packet As New UTQueryPacket(flags:=UTQueryPacket.Flags.UTQP_SimpleRequest)
                If dto.Capabilities.HasUtf8PlayerList Then
                    packetCharset = Encoding.UTF8
                End If
                .RequestingPlayers = True
                sync.InvalidatePlayers()
                packet("players") = IIf(dto.Capabilities.HasXsq, xsqSuffix, "")
                request.Add(packet)

            ElseIf Not .HasVariables AndAlso dto.Capabilities.SupportsVariables Then
                Dim packet As New UTQueryPacket(flags:=UTQueryPacket.Flags.UTQP_SimpleRequest)
                .RequestingVariables = True
                sync.InvalidateVariables()
                packet("rules") = ""
                request.Add(packet)
            Else
                .done = True
            protocolFailures = 0
            networkTimeoutDeadline = Nothing
            End If
        End With
        If request.Count > 0 Then
            serverSend(request.ToString())
        End If

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
                If .RequestingBasic Then
                    parseBasic(packet)
                End If
                If .RequestingInfo Then
                    parseInfo(packet)
                End If
                If .RequestingInfoExtended Then
                    parseInfoExtended(packet)
                End If
                If .RequestingPlayers Then
                    parsePlayers(packet)
                End If
                If .RequestingVariables Then
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
        Dim packet As Hashtable
        Try
            packet = ServerQueryValidators.basic.Validate(packetObj)
        Catch ex As UTQueryValidationException
            If Not state.HasProbed Then
                state.HasProbed = True
                dto.Capabilities.CompoundRequest = False
                dto.Capabilities.SupportsVariables = False
                Return
            End If
            abortScan("Server did not provide basic information")
            Return
        End Try

        Dim gameName = packet("gamename").ToLower()
        Dim validServer As Boolean = False

        ' validate
        validServer = ValidateServer(packetObj, gameName)

        If Not validServer Then
            abortScan("Challenge validation failed")
        End If

        If Not state.HasValidated Then
            dto.LastValidationTime = Date.UtcNow
            state.HasValidated = True

        End If


        dto.Info("gamename") = packet("gamename")
        dto.Info("gamever") = packet("gamever")
        If packet.ContainsKey("minnetver") Then
            dto.Info("minnetver") = packet("minnetver")
        ElseIf packet.ContainsKey("mingamever") Then
            dto.Info("minnetver") = packet("mingamever")
        End If
        dto.Info("location") = packet("location")
        state.HasBasic = True
        isOnline = True
        dto.Capabilities.GameVersion = dto.Info("gamever")
        dto.Capabilities.GameName = dto.Info("gamename")

        If gameName = "ut" Then
            dto.Capabilities.HasXsq = True ' set this flag for initial polling with XSQ suffix
            dto.Capabilities.HasUtf8PlayerList = Integer.Parse(dto.Capabilities.GameVersion) >= 469
        ElseIf gameName = "unreal" Then
            dto.Capabilities.HasCp437Info = True
            dto.Capabilities.HasPropertyInterface = False
        End If

        If Not state.HasProbed Then
            Dim tmp As Integer
            dto.Capabilities.CompoundRequest = packetObj.ContainsKey("echo_replay") OrElse packetObj.ContainsKey("echo")
            dto.Capabilities.HasPropertyInterface = packetObj.ContainsKey("numplayers") AndAlso Integer.TryParse(packetObj("numplayers"), tmp)

            state.HasProbed = True
        End If

    End Sub

    Private Function ValidateServer(packetObj As UTQueryPacket, gameName As String) As Boolean
        If state.HasValidated Then
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
            If pair.key.Length >2 AndAlso pair.key.Substring(0, 2) = "__" Then
                Continue For
            End If
            dto.Info(pair.key) = pair.value
        Next
        If validated.ContainsKey("hostport") Then
            addressGame = JulkinNet.GetHost(addressQuery) & ":" & validated("hostport")
        End If
        dto.Capabilities.HasXsq = packetObj.ContainsKey("xserverquery")
        If dto.Capabilities.HasXsq Then
            Dim xsqVersion As Integer
            Integer.TryParse(Replace(packetObj("xserverquery"), ".", ""), formatProvider, xsqVersion)
            ' property interface brought back in XServerQuery 211fix4
            dto.Capabilities.HasPropertyInterface = xsqVersion >= 211
            dto.Capabilities.XsqVersion = xsqVersion
        End If

        dto.Info("__uttrealplayers") = validated("numplayers") ' might be overwritten later
        state.HasInfo = True
        state.HasNumPlayers = True

        If dto.Capabilities.HasPropertyInterface Then
            dto.Capabilities.FakePlayers = False
            checkFakePlayers = True
        End If
    End Sub

    Private Sub parseInfoExtended(packetObj As UTQueryPacket)
        Try
            Dim validated = ServerQueryValidators.infoExtended.Validate(packetObj)

            dto.Capabilities.QuickNumPlayers = True

            Dim needsUpdatingPlayerList = validated("numplayers") > 0 OrElse
                validated("numspectators") > 0

            If needsUpdatingPlayerList Then
                state.HasPlayers = False
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
            state.HasInfoExtended = True
            sync.InvalidateVariables()

            Dim mapName = Regex.Match(validated("outer"), "^Package'(.+)'$").Groups(1).ToString()
            If mapName <> dto.Info("mapname") Then
                state.HasInfo = False
            End If




            ' fake players detection
            If checkFakePlayers AndAlso dto.Info("numplayers") > validated("numplayers") + validated("numspectators") Then
                dto.Capabilities.FakePlayers = True
            End If

            If dto.Capabilities.GamemodeExtendedInfo Then
                gamemodeQuery.ParseInfoPacket(packetObj.ConvertToHashtablePacket())
            End If

            state.HasNumPlayers = True
        Catch e As Exception
            dto.Capabilities.HasPropertyInterface = False
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

                If dto.Capabilities.HasXsq Then
                    If dto.Capabilities.XsqVersion >= 200 Then
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
        state.HasPlayers = True
    End Sub

    Private Sub parseVariables(packetObj As UTQueryPacket)
        dto.Variables = packetObj.ConvertToDictionary()

        dto.Info("__utthaspropertyinterface") = dto.Capabilities.HasPropertyInterface

        state.HasVariables = True
    End Sub

    Private Function skipStepIfOptional()
        With state
            If .RequestingBasic AndAlso Not .HasProbed Then
                .HasProbed = True
                dto.Capabilities.CompoundRequest = False
                dto.Capabilities.SupportsVariables = False
                lastActivity = Date.UtcNow
                sendRequest()
                Return True
            ElseIf .RequestingInfoExtended Then
                .RequestingInfoExtended = False
                dto.Capabilities.HasPropertyInterface = False
                dto.Capabilities.QuickNumPlayers = False
                lastActivity = Date.UtcNow
                sendRequest()
                Return True
            ElseIf .RequestingTimeTest Then
                .RequestingTimeTest = False
                lastActivity = Date.UtcNow
                sendRequest()
                Return True

            ElseIf .RequestingInfo AndAlso dto.Capabilities.HasXsq Then
                ' workaround: XServerQuery not responding
                .RequestingInfo = False
                dto.Capabilities.HasXsq = False
                sendRequest()
                Return True

            ElseIf .RequestingVariables Then
                .RequestingVariables = False
                dto.Capabilities.SupportsVariables = False
                .HasVariables = False
                sendRequest()
                Return True
            End If
        End With

        Return False
    End Function

    Private Sub resetRequestFlags()
        With state
            .RequestingBasic = False
            .RequestingInfo = False
            .RequestingInfoExtended = False
            .RequestingPlayers = False
            .RequestingVariables = False
            .RequestingTimeTest = False
        End With
    End Sub

    Private Function isInRequestState()
        With state
            Return Not .done AndAlso (
                .RequestingBasic OrElse
                .RequestingInfo OrElse
                .RequestingInfoExtended OrElse
                .RequestingPlayers OrElse
                .RequestingVariables OrElse
                .RequestingTimeTest
            )
        End With
    End Function

    Friend Sub abortScan(Optional reason As String = "?", Optional dumpCommLog As Boolean = False)
        If Not state.done Then
            state.done = True
            sync.FinishSync()
            isOnline = False
            If Not state.RequestingBasic Then
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
        Public Shared ReadOnly basic As UTQueryValidator =
            UTQueryValidator.FromRuleDict(New Dictionary(Of String, String) From {
                             {"gamename", "required|string"},
                             {"gamever", "required|string"},
                             {"minnetver", "string"},
                             {"mingamever", "string"},
                             {"location", "integer"}
                            })

        Public Shared ReadOnly challenge As UTQueryValidator =
            UTQueryValidator.FromRuleDict(New Dictionary(Of String, String) From {
            {"validate", "required|string|gte:6|lte:8"}
         })
        Public Shared ReadOnly info As UTQueryValidator =
            UTQueryValidator.FromRuleDict(New Dictionary(Of String, String) From {
                         {"hostname", "required|string"},
                         {"mapname", "required|string"},
                         {"numplayers", "required|integer|gte:0"},
                         {"maxplayers", "required|integer|gte:1"},
                         {"hostport", "integer|gte:1|lte:65535"}
                        })

        Public Shared ReadOnly infoExtended As UTQueryValidator =
            UTQueryValidator.FromRuleDict(New Dictionary(Of String, String) From {
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
        Public Shared ReadOnly players As UTQueryValidator =
            UTQueryValidator.FromRuleDict(New Dictionary(Of String, String) From {
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
    Dim IsStarted As Boolean
    Dim IsStarting As Boolean
    Dim HasValidated As Boolean
    Dim HasProbed As Boolean
    Dim HasBasic As Boolean
    Dim HasInfo As Boolean
    Dim HasNumPlayers As Boolean
    Dim HasTeams As Boolean
    Dim HasInfoExtended As Boolean
    Dim HasTimeTest As Boolean
    Dim HasPlayers As Boolean
    Dim HasVariables As Boolean

    Dim RequestingBasic As Boolean
    Dim RequestingInfo As Boolean
    Dim RequestingInfoShort As Boolean
    Dim RequestingInfoExtended As Boolean
    Dim RequestingTimeTest As Boolean
    Dim RequestingPlayers As Boolean
    Dim RequestingVariables As Boolean

    Dim done As Boolean

    Public Overrides Function ToString() As String
        ToString = "ServerQueryState#"
        If RequestingVariables Then
            ToString &= "requestingVariables"
        ElseIf RequestingPlayers Then
            ToString &= "requestingPlayers"
        ElseIf RequestingTimeTest Then
            ToString &= "requestingTimeTest"
        ElseIf RequestingInfoExtended Then
            ToString &= "requestingInfoExtended"
        ElseIf RequestingInfoShort Then
            ToString &= "requestingInfoShort"
        ElseIf RequestingInfo Then
            ToString &= "requestingInfo"
        ElseIf RequestingBasic Then
            ToString &= "requestingBasic"
        ElseIf done Then
            ToString &= "done"
        ElseIf IsStarting Then
            ToString &= "starting"
        ElseIf IsStarted Then
            ToString &= "started"
        Else
            ToString &= "???"
        End If
        ToString &= "#"
    End Function
End Structure

Public Class ServerRequest
    Protected packetList As New List(Of UTQueryPacket)

    Public Sub Add(packet As UTQueryPacket)
        packetList.Add(packet)
    End Sub

    Public Sub Add(key As String, value As String)
        Dim packet = New UTQueryPacket(UTQueryPacket.Flags.UTQP_SimpleRequest)
        packet.Add(key, value)
        Me.Add(packet)
    End Sub

    Public Sub Add(packetString As String)
        Dim packet = New UTQueryPacket(packetString, UTQueryPacket.Flags.UTQP_SimpleRequest)
        Me.Add(packet)
    End Sub

    Public ReadOnly Property Count As Integer
        Get
            Return packetList.Count
        End Get
    End Property

    Public Overrides Function ToString() As String
        Dim result As New StringBuilder()
        Dim lastChar As Char = "\"
        For Each packet In packetList
            If lastChar <> "\" Then
                ' quirk: each request must be separated by two backslashes:
                ' \info\\rules\
                ' \info\xserverquery\\rules\
                result.Append("\")
            End If
            Dim packetString = packet.ToString()
            result.Append(packetString)
            lastChar = packetString.Last()
        Next
        Return result.ToString()
    End Function
End Class

Public Class ScanException
    Inherits Exception
    Public Sub New()
    End Sub

    Public Sub New(message As String)
        MyBase.New(message)
    End Sub

    Public Sub New(message As String, inner As Exception)
        MyBase.New(message, inner)
    End Sub
End Class
