Imports System.IO
Imports System.Net
Imports System.Reflection
Imports Microsoft.Extensions.FileProviders
Imports Naomai.UTT.Indexer.Utt2Database

Module App
    Dim scanner As Scanner
    Dim WithEvents gsServer As GSMasterServer
    Dim gsServerProvider As GSMasterServerListProvider
    Dim masterManager As MasterServerManager
    Dim ini As IniPropsProvider
    Dim log As LoggingAdapter
    Dim dbCtx As Utt2Context
    Dim dyncfg As IPropsProvider

    Sub Main()
        ini = GetAppIniProvider()
        log = CreateLoggingAdapter(ini)

        log.WriteLine("UTTracker Scanner")
        log.WriteLine("2009-24 naomai")
        log.DebugWriteLine("Loading config file from: {0}", ini.IniName)

        Dim dbconfig As MySQLDBConfig = GetDbConfig(ini)
        dbCtx = New Utt2Context(dbconfig)

        Dim dyncfgDbCtx = New Utt2Context(dbconfig)
        dyncfg = New DatabasePropsProvider(dyncfgDbCtx).Ns("utt.reaper")
        dyncfg.SetProperty("configsrc", ini.IniName, True)

        InitMasterServerManager(log, ini)
        InitScanner(log, dbCtx, masterManager)
        InitGSMasterServer(dbconfig, ini)

        scanner.ScannerThread()
    End Sub

    Private Function CreateLoggingAdapter(configSource As IniPropsProvider) As LoggingAdapter
        Dim log = New LoggingAdapter()
        log.consoleLoggingLevel = (LoggerLevel.err Or LoggerLevel.out Or LoggerLevel.debug)
        If configSource.GetProperty("General.LogToFile", 0) Then
            log.fileLoggingLevel = (LoggerLevel.err Or LoggerLevel.out Or LoggerLevel.debug)
        Else
            log.fileLoggingLevel = 0
        End If
        Return log
    End Function

    Private Function GetDbConfig(configSource As IniPropsProvider) As MySQLDBConfig
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
                .host = configSource.GetProperty("Database.MySQLHost", "changeme!!")
                .username = configSource.GetProperty("Database.MySQLUser")
                .password = configSource.GetProperty("Database.MySQLPass")
                .database = configSource.GetProperty("Database.MySQLDB")
                .protocol = configSource.GetProperty("Database.MySQLProtocol", "socket")
            End If
        End With

        If dbconfig.host = "changeme!!" Then
            Throw New Exception("Please configure the scanner first (" & configSource.IniName & ")")
        End If

        Return dbconfig
    End Function

    Private Sub InitScanner(logger As LoggingAdapter, dbCtx As Utt2Context, master As MasterServerManager)
        scanner = New Scanner(dbCtx, master)
        scanner.logger = logger.CreateLogger("ServerScanner")
    End Sub

    Private Sub InitMasterServerManager(logger As LoggingAdapter, config As IniPropsProvider)
        masterManager = New MasterServerManager()
        masterManager.log = logger.CreateLogger("MasterServerManager")
        masterManager.UpdateInterval = config.GetProperty("MasterServer.RefreshIntervalMins", "120") * 60

        Dim msIdx As Integer = 0
        Dim masterServerString As String
        Do While config.PropertyExists("UBrowserAll.ListFactories[" & msIdx & "]")
            masterServerString = config.GetProperty("UBrowserAll.ListFactories[" & msIdx & "]")
            If masterServerString <> "" Then
                masterManager.AddMasterServer(masterServerString)
            End If
            msIdx += 1
        Loop

        msIdx = 0
        Do While config.PropertyExists("XBrowser|XBrowserTabInternet.MasterServer[" & msIdx & "]")
            masterServerString = config.GetProperty("XBrowser|XBrowserTabInternet.MasterServer[" & msIdx & "]")
            If masterServerString <> "" Then
                masterManager.AddMasterServer(masterServerString)
            End If
            msIdx += 1
        Loop

        masterManager.RefreshAsync().Wait()
        masterManager.ThreadLoop()
    End Sub

    Private Sub InitGSMasterServer(dbconfig As MySQLDBConfig, config As IniPropsProvider)
        If config.GetProperty("GSMasterServer.Enabled", "0") = 1 Then
            log.WriteLine("Enabling GameSpy Master Server...")
            Dim msPort As Integer = config.GetProperty("GSMasterServer.Port", "28900")
            Dim msDbCtx = New Utt2Context(dbconfig)
            gsServer = New GSMasterServer(msPort)
            gsServerProvider = New GSMasterServerListProvider(msDbCtx)
            gsServer.SetServerListProvider(gsServerProvider)
            gsServer.LoadGSListFromDict(MasterServerManager.gamespyKeys)
            gsServer.StartServer()
        End If
    End Sub

    Private Sub master_ClientConnected(client As System.Net.IPEndPoint) Handles gsServer.ClientConnected
        dyncfg.SetProperty("gsmasterserver.lastevent", UnixTime())
    End Sub

    Private Sub master_ClientDisconnected(client As IPEndPoint, reason As GSClosingReason, relatedException As Exception) Handles gsServer.ClientDisconnected
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

    Private Sub master_ClientRequestedList(client As System.Net.IPEndPoint) Handles gsServer.ClientRequestedList
        log.DebugWriteLine("GSMasterConnection({0}): client requests list", client.ToString)
    End Sub


    Private Function GetAppIniProvider() As IniPropsProvider
        Dim bundledConfigProvider = New EmbeddedFileProvider(Assembly.GetExecutingAssembly(), "Naomai.UTT.Indexer")
        Dim bundledConfigFile = bundledConfigProvider.GetFileInfo("ConfigDist.ini")
        Dim bundledConfigStream As Stream = Nothing

        If bundledConfigFile.Exists Then
            bundledConfigStream = bundledConfigFile.CreateReadStream()

        End If

        Return New IniPropsProvider(
            Assembly.GetEntryAssembly.GetName.Name & ".ini",
            template:=bundledConfigStream
        )
    End Function

End Module
