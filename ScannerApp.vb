Imports System.Data
Imports System.Net
Imports MySql.Data.MySqlClient
Imports Naomai.UTT.ScannerV2.Utt2Database

Module ScannerApp
    Dim WithEvents scanner As ServerScanner
    Dim WithEvents master As GSMasterServer
    Dim masterBridge As GSMasterServerBridge
    Dim masterManager As MasterServerManager
    Dim ini As N14INI
    Dim log As DeVlog
    Dim db As MySQLDB
    Dim dbCtx As Utt2Context
    Dim dyncfg As DynConfig

    Sub Main()
        Dim appName = System.Reflection.Assembly.GetEntryAssembly.GetName.Name
        ini = New N14INI()
        log = New DeVlog()

        log.consoleLoggingLevel = (DeVlogLoggingLevel.err Or DeVlogLoggingLevel.out)
        If ini.getProperty("General.LogToFile", 0) Then
            log.fileLoggingLevel = (DeVlogLoggingLevel.err Or DeVlogLoggingLevel.out Or DeVlogLoggingLevel.debug)
        Else
            log.fileLoggingLevel = 0
        End If


        log.ErrorWriteLine("UTTracker Scanner")
        log.ErrorWriteLine("2009-24 naomai")
        log.ErrorWriteLine("")
        log.ErrorWriteLine("Loading config file from: {0}", ini.iniName)


        Dim dbconfig As MySQLDBConfig
        dbconfig.host = ini.getProperty("Database.MySQLHost", "changeme!!")
        dbconfig.username = ini.getProperty("Database.MySQLUser")
        dbconfig.password = ini.getProperty("Database.MySQLPass")
        dbconfig.database = ini.getProperty("Database.MySQLDB")
        dbconfig.protocol = ini.getProperty("Database.MySQLProtocol")
        'dbconfig.charset = "utf16"

        If dbconfig.host = "changeme!!" Then
            Throw New Exception("Please configure the scanner first (" & ini.iniName & ")")
        End If

        CRC32_Init()

        db = New MySQLDB(dbconfig)
        dbCtx = New Utt2Context(dbconfig)

        If Not db.ready Then
            'Throw New Exception("Database error")

        End If

        masterManager = New MasterServerManager(ini.getProperty("MasterServer.Cache", ".\server_list.txt"), ini.getProperty("MasterServer.GSListCFGLoc", ".\gslist.cfg"))
        ' masterManager.log = log

        Dim numMasterServers As Integer = ini.getProperty("MasterServer.MasterServersNum", "0"), masterServerString As String
        For i = 0 To numMasterServers
            masterServerString = ini.getProperty("UBrowserAll.ListFactories[" & i & "]")
            If masterServerString <> "" Then
                masterManager.addMasterServer(masterServerString)
            End If
        Next

        dyncfg = New DynConfig(db.dbh)

        Dim scannerConfig As ServerScannerConfig
        With scannerConfig
            .log = log
            .db = db
            .dbCtx = dbCtx
            .masterServerUpdateInterval = ini.getProperty("MasterServer.RefreshIntervalMins", "120") * 60
            .scanInterval = ini.getProperty("General.IntervalMins", "2") * 60
            .iniFile = ini.iniName
            .masterServerManager = masterManager
        End With
        scanner = New ServerScanner(scannerConfig)
        'scannerConfig.masterServerManager = Nothing
        'scannerConfig.db = New MySQLDB(dbconfig)
        'Dim tinyScanner = New AsyncLittleScanner(scannerConfig)

        Dim nextscan As DateTime


        If ini.getProperty("GSMasterServer.Enabled", "0") = 1 Then
            log.WriteLine("Enabling GameSpy Master Server...")
            Dim msPort As Integer = ini.getProperty("GSMasterServer.Port", "28900")
            master = New GSMasterServer(msPort)
            masterBridge = New GSMasterServerBridge(db.dbh)
            master.setServerListProvider(masterBridge)
            master.loadGSListFromDict(MasterServerManager.gamespyKeys)
            master.startServer()
            master.beginAsync()
        End If
        'tinyScanner.asyncBegin()
        Do
            log.WriteLine("Update in progress...")
            nextscan = Date.UtcNow + TimeSpan.FromSeconds(scanner.scanInterval)
            'Try
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
                'Catch e As Exception When e.Source = "MySql.Data"
            '    log.WriteLine(e.Message)
            '    Try
            '        Threading.Thread.Sleep(1000)
            '        If Not (db.dbh.State.HasFlag(ConnectionState.Open)) Then
            '            log.WriteLine("Lost connection to database server. Reconnecting...")
            '            db.Reconnect()
            '        Else
            '            log.WriteLine("Invalid operation on database server. Restarting db connection...")
            '            db.Reconnect()

            '        End If
            '    Catch e2 As Exception
            '        Threading.Thread.Sleep(1000)
            '        db.Reconnect()
            '    End Try

            'End Try
        Loop
    End Sub

    Private Sub master_ClientConnected(client As System.Net.IPEndPoint) Handles master.ClientConnected
        dyncfg.setProperty("net.gsmasterserver.lastevent", unixTime())
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

End Module
