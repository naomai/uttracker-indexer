Imports System.Data
Imports System.Net
Imports System.Reflection
Imports Microsoft.Extensions.FileProviders
Imports Naomai.UTT.Indexer.Utt2Database

Module App
    Dim WithEvents scanner As Scanner
    Dim WithEvents master As GSMasterServer
    Dim masterBridge As GSMasterServerBridge
    Dim masterManager As MasterServerManager
    Dim ini As IniPropsProvider
    Dim log As Logger
    Dim dbCtx As Utt2Context
    Dim dyncfg As IPropsProvider

    Sub Main()
        Dim appName = System.Reflection.Assembly.GetEntryAssembly.GetName.Name
        ini = GetIniConfig()
        log = New Logger()

        log.consoleLoggingLevel = (LoggerLevel.err Or LoggerLevel.out Or LoggerLevel.debug)
        If ini.GetProperty("General.LogToFile", 0) Then
            log.fileLoggingLevel = (LoggerLevel.err Or LoggerLevel.out Or LoggerLevel.debug)
        Else
            log.fileLoggingLevel = 0
        End If


        log.ErrorWriteLine("UTTracker Scanner")
        log.ErrorWriteLine("2009-24 naomai")
        log.ErrorWriteLine("")
        log.ErrorWriteLine("Loading config file from: {0}", ini.IniName)


        Dim dbconfig As MySQLDBConfig
        With dbconfig
            Dim env = Environment.GetEnvironmentVariables()
            If env("DB_HOST") <> "" Then
                .host = env("DB_HOST")
                .username = env("DB_USERNAME")
                .password = env("DB_PASSWORD")
                .database = env("DB_DATABASE")
                .protocol = "socket"
            Else
                .host = ini.GetProperty("Database.MySQLHost", "changeme!!")
                .username = ini.GetProperty("Database.MySQLUser")
                .password = ini.GetProperty("Database.MySQLPass")
                .database = ini.GetProperty("Database.MySQLDB")
                .protocol = ini.GetProperty("Database.MySQLProtocol", "socket")
            End If
        End With
        'dbconfig.charset = "utf16"

        If dbconfig.host = "changeme!!" Then
            Throw New Exception("Please configure the scanner first (" & ini.IniName & ")")
        End If

        dbCtx = New Utt2Context(dbconfig)

        masterManager = New MasterServerManager()
        masterManager.log = log

        Dim msIdx As Integer = 0
        Dim masterServerString As String
        Do While ini.PropertyExists("UBrowserAll.ListFactories[" & msIdx & "]")
            masterServerString = ini.GetProperty("UBrowserAll.ListFactories[" & msIdx & "]")
            If masterServerString <> "" Then
                masterManager.AddMasterServer(masterServerString)
            End If
            msIdx += 1
        Loop

        msIdx = 0
        Do While ini.PropertyExists("XBrowser|XBrowserTabInternet.MasterServer[" & msIdx & "]")
            masterServerString = ini.GetProperty("XBrowser|XBrowserTabInternet.MasterServer[" & msIdx & "]")
            If masterServerString <> "" Then
                masterManager.AddMasterServer(masterServerString)
            End If
            msIdx += 1
        Loop

        masterManager.Refresh()
        masterManager.ThreadLoop()

        Dim dyncfgDbCtx = New Utt2Context(dbconfig)
        'dyncfg = New DatabasePropsProvider(dyncfgDbCtx, "utt.reaper")
        dyncfg = New DatabasePropsProvider(dyncfgDbCtx).Ns("utt.reaper")
        dyncfg.SetProperty("configsrc", ini.IniName, True)

        Dim scannerConfig As ServerScannerConfig
        With scannerConfig
            .log = log
            .dbCtx = dbCtx
            .dyncfg = dyncfg.Ns("scanner")
            .masterServerUpdateInterval = ini.GetProperty("MasterServer.RefreshIntervalMins", "120") * 60
            .scanInterval = ini.GetProperty("General.IntervalMins", "2") * 60
            .iniFile = ini.IniName
            .masterServerManager = masterManager
        End With
        scanner = New Scanner(scannerConfig)
        'scannerConfig.masterServerManager = Nothing
        'scannerConfig.db = New MySQLDB(dbconfig)
        'Dim tinyScanner = New AsyncLittleScanner(scannerConfig)

        Dim nextscan As DateTime


        If ini.GetProperty("GSMasterServer.Enabled", "0") = 1 Then
            log.WriteLine("Enabling GameSpy Master Server...")
            Dim msPort As Integer = ini.GetProperty("GSMasterServer.Port", "28900")
            Dim msDbCtx = New Utt2Context(dbconfig)
            master = New GSMasterServer(msPort)
            masterBridge = New GSMasterServerBridge(msDbCtx)
            master.SetServerListProvider(masterBridge)
            master.LoadGSListFromDict(MasterServerManager.gamespyKeys)
            master.StartServer()
        End If
        'tinyScanner.asyncBegin()
        Do
            log.WriteLine("Update in progress...")
            nextscan = Date.UtcNow + TimeSpan.FromSeconds(scanner.scanInterval)
            scanner.performScan()
            log.WriteLine("Scanned {0}, online: {1}; next: {2}s", scanner.serversCountTotal, scanner.serversCountOnline, Math.Round((nextscan - Date.UtcNow).TotalSeconds))
            Do While (nextscan - Date.UtcNow).TotalSeconds > 0 AndAlso (nextscan - Date.UtcNow).TotalSeconds <= scanner.scanInterval
                Threading.Thread.Sleep(50)
            Loop
            If (nextscan - Date.UtcNow).TotalSeconds < -7200 Then
                log.WriteLine("Time travel detected ({0} seconds)", Math.Round(-(nextscan - Date.UtcNow).TotalSeconds))
            ElseIf (nextscan - Date.UtcNow).TotalSeconds > 7200 Then
                log.WriteLine("Time travel detected, bios time was reset? Next scan schedule: {0}, offset: {1} seconds.", nextscan, Math.Round((nextscan - Date.UtcNow).TotalSeconds))
                log.WriteLine("Correct your system time and press ENTER")
                Console.ReadLine()
            End If

        Loop
    End Sub

    Private Sub master_ClientConnected(client As System.Net.IPEndPoint) Handles master.ClientConnected
        dyncfg.SetProperty("gsmasterserver.lastevent", UnixTime())
    End Sub

    Private Sub master_ClientDisconnected(client As IPEndPoint, reason As GSClosingReason, relatedException As Exception) Handles master.ClientDisconnected
        Dim errorMessage As String
        Select Case reason
            Case GSClosingReason.InvalidChallenge
                errorMessage = relatedException.Message
            Case GSClosingReason.InvalidPacket
                errorMessage = "Invalid packet: " & relatedException.Message
            Case GSClosingReason.Timeout
                errorMessage = "emo timeout"
            Case GSClosingReason.InternalError
                errorMessage = relatedException.Message
            Case Else
                Return
        End Select
        log.DebugWriteLine("GSMasterConnection({0}): {1}", client.ToString, errorMessage)
    End Sub

    Private Sub master_ClientRequestedList(client As System.Net.IPEndPoint) Handles master.ClientRequestedList
        log.DebugWriteLine("GSMasterConnection({0}): client requests list", client.ToString)
    End Sub

    Private Sub scanner_OnScanBegin(serverCount As Integer) Handles scanner.OnScanBegin
        log.WriteLine("Scanning {0} servers...", serverCount)
    End Sub

    Private Sub scanner_OnScanComplete(scannedServerCount As Integer, onlineServerCount As Integer, elapsedTime As System.TimeSpan) Handles scanner.OnScanComplete
        log.DebugWriteLine("Scan complete: {0}/{1} in {2} seconds", onlineServerCount, scannedServerCount, Math.Round(elapsedTime.TotalSeconds))
    End Sub

    Private Function GetIniConfig() As IniPropsProvider
        Dim bundledConfigProvider = New EmbeddedFileProvider(Assembly.GetExecutingAssembly(), "Naomai.UTT.Indexer")
        Dim bundledConfigFile = bundledConfigProvider.GetFileInfo("ConfigDist.ini")
        Dim bundledConfigStream As Stream = Nothing

        If bundledConfigFile.Exists Then
            bundledConfigStream = bundledConfigFile.CreateReadStream()

        End If

        Return New IniPropsProvider(
            Assembly.GetEntryAssembly.GetName.Name & ".ini"
        ) With {
            .templateFileStream = bundledConfigStream
        }
    End Function

End Module
