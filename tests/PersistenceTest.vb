
Imports Naomai.UTT.Indexer.Utt2Database
Imports Moq
Imports NUnit.Framework
Imports Microsoft.EntityFrameworkCore

Namespace Tests
    Public Class PersistenceTest

        <Test>
        Public Sub CreateServerRecord()
            Dim env = CreateTestServer()

            Dim dto = env.Dto
            Dim sync = env.PersistenceService

            sync.Tick()
            Dim record = sync.GetServerRecord()

            Assert.That(record.Id, [Is].Not.Null())
            Assert.That(record.Name, [Is].EqualTo(dto.Info("hostname")))
            Assert.That(record.AddressGame, [Is].EqualTo(dto.AddressGame))
            Assert.That(record.AddressQuery, [Is].EqualTo(dto.AddressQuery))
            Assert.That(record.GameName, [Is].EqualTo(dto.GameName))
        End Sub

        <Test>
        Public Sub CreateMatchRecord()
            Dim env = CreateTestServer()

            Dim dto = env.Dto
            Dim sync = env.PersistenceService

            sync.Tick()
            Dim serverRecord = sync.GetServerRecord()

            Assert.That(serverRecord.ServerMatches, Has.Exactly(1).Items)
            Dim match = serverRecord.ServerMatches.First()

            Assert.That(match.MapName, [Is].EqualTo(dto.Info("mapname")))
        End Sub

        <Test>
        Public Sub SavePlayerData()
            Dim env = CreateTestServer()

            Dim dto = env.Dto
            Dim sync = env.PersistenceService
            FakePlayersForServer(dto)

            sync.Tick()
            Assert.That(sync.IsSyncInProgress(), [Is].False)

            Dim record = sync.GetServerRecord()
            Dim logs = record.PlayerLogs

            Assert.That(logs, Has.Exactly(5).Items)
            For Each player In dto.Players
                Dim matchingPlayers = env.DatabaseContext.Players.Where(
                        Function(l) l.Name = player("name")
                    ).ToList()
                Assert.That(matchingPlayers, Has.Exactly(1).Items)
                Dim info = matchingPlayers.First()

                Dim matchingPlayerLogs = logs.Where(
                        Function(l) l.Player Is info
                    ).ToList()
                Assert.That(matchingPlayerLogs, Has.Exactly(1).Items)
                Dim log = matchingPlayerLogs.First()

                Assert.That(log.Team, [Is].EqualTo(Integer.Parse(player("team"))))
                Assert.That(log.ScoreThisMatch, [Is].EqualTo(Integer.Parse(player("frags"))))
                Assert.That(log.DeathsThisMatch, [Is].EqualTo(Integer.Parse(player("deaths"))))
                Assert.That(log.SeenCount, [Is].EqualTo(1))
                Assert.That(log.Finished, [Is].EqualTo(False))
            Next

        End Sub


        <Test>
        Public Sub UpdatedPlayerData()
            Dim env = CreateTestServer()

            Dim dto = env.Dto
            Dim sync = env.PersistenceService
            FakePlayersForServer(dto)

            sync.Tick()

            Dim playerDto = dto.Players(1)
            Dim newFrags = Rand(500, 1000), newPing = Rand(20, 100), newDeaths = Rand(100, 1000)
            Dim pingSum = newPing + Integer.Parse(playerDto("ping"))
            playerDto("frags") = newFrags
            playerDto("ping") = newPing
            playerDto("deaths") = newDeaths

            sync.InvalidatePlayers()
            sync.Tick()

            Dim playerRecord = env.DatabaseContext.Players.Where(
                        Function(l) l.Name = playerDto("name")
                    ).First()
            Dim playerLog = sync.GetServerRecord().PlayerLogs.Where(
                        Function(l) l.Player Is playerRecord
                    ).First()

            Assert.That(playerLog.ScoreThisMatch, [Is].EqualTo(newFrags))
            Assert.That(playerLog.PingSum, [Is].EqualTo(pingSum))
            Assert.That(playerLog.DeathsThisMatch, [Is].EqualTo(newDeaths))
            Assert.That(playerLog.SeenCount, [Is].EqualTo(2))
        End Sub

        <Test>
        Public Sub NewMatchByMapName()
            Dim env = CreateTestServer()

            Dim dto = env.Dto
            Dim sync = env.PersistenceService
            FakePlayersForServer(dto)

            sync.Tick()
            env.DatabaseContext.SaveChanges()
            Dim server = sync.GetServerRecord()

            Dim mapRecordInitial = server.ServerMatches.Where(
                        Function(l) l.MapName = dto.Info("mapname")
                    ).First()

            dto.Info("mapname") = "CTF-Face"
            sync.InvalidateInfo()
            sync.InvalidatePlayers()
            sync.Tick()


            Assert.That(server.ServerMatches, Has.Exactly(2).Items)
            Dim mapRecordsMatchingMapname = server.ServerMatches.Where(
                        Function(l) l.MapName = dto.Info("mapname")
                    )
            Assert.That(mapRecordsMatchingMapname, Has.Exactly(1).Items)

            Dim mapRecord = mapRecordsMatchingMapname.First()
            Assert.That(mapRecord, [Is].Not.SameAs(mapRecordInitial))

            ' check if all player logs under initial match are marked as finished
            Dim logsFinished = mapRecordInitial.PlayerLogs _
                    .Where(
                        Function(l) l.Finished = True
                    )
            Assert.That(logsFinished, Has.Exactly(5).Items)



        End Sub

        Private Function CreateDbContext() As Utt2Context
            Dim options = New DbContextOptionsBuilder(Of Utt2Context)() _
                .UseSqlite("DataSource=file::memory:?cache=shared") _
                .Options
            Dim mockCtx = New Utt2Context(options)
            mockCtx.Database.EnsureCreated()
            Return mockCtx
        End Function


        Private Function FakeServerDto() As ServerInfo
            Static addressSequence As Byte = 1
            Dim dto = New ServerInfo() With {
                .AddressGame = $"192.168.43.{addressSequence}:7777",
                .AddressQuery = $"192.168.43.{addressSequence}:7778",
                .GameName = "ut",
                .InfoRequestTime = Date.UtcNow.AddSeconds(-10),
                .PropsRequestTime = Date.UtcNow.AddSeconds(-5),
                .LastActivityTime = Date.UtcNow,
                .LastValidationTime = Date.UtcNow.AddSeconds(-10)
            }

            addressSequence += 1

            dto.Info = New Dictionary(Of String, String) From {
                {"gamename", "ut"},
                {"gamever", "469"},
                {"minnetver", "432"},
                {"location", "0"},
                {"goalteamscore", "0"},
                {"mapname", "CTF-Coret"},
                {"maxteams", "2"},
                {"mutators", "UTPure, NewNetIG, SmartCTF 4D++"},
                {"balanceteams", "False"},
                {"adminname", "unknown"},
                {"listenserver", "False"},
                {"hostport", "7777"},
                {"hostname", "Unknown's Brightmare"},
                {"changelevels", "True"},
                {"tournament", "True"},
                {"friendlyfire", "0%"},
                {"password", "True"},
                {"timelimit", "20"},
                {"numplayers", "0"},
                {"playersbalanceteams", "False"},
                {"gamestyle", "Hardcore"},
                {"maxplayers", "16"},
                {"gametype", "CTFGame"},
                {"minplayers", "0"},
                {"__uttrealplayers", "0"},
                {"__utthaspropertyinterface", "False"}
            }
            dto.Variables = dto.Info


            With dto.State
                .HasBasic = True
                .HasInfo = True
                .HasInfoExtended = False
                .HasPlayers = True
                .HasTeams = True
                .HasVariables = True
            End With
            Return dto
        End Function


        Private Sub ConvertToXServerQuery(dto As ServerInfo)
            dto.Info.Add("xserverquery", "2.01")
            dto.Info.Add("countrys", "de")
            dto.Info.Add("newnet", "")
            dto.Info.Add("protection", "ACEv13m")

            For Each player In dto.Players
                player("country") = "es"
                player("deaths") = Rand(5, 190)
                player("time") = Rand(20, 1000)
            Next
        End Sub

        Private Sub FakePlayersForServer(dto As ServerInfo)
            Static sequence As Integer = 1
            For iss = 1 To 5
                Dim player As New Dictionary(Of String, String) From {
                    {"name", $"Player{sequence}"},
                    {"team", Rand(0, 1)},
                    {"frags", Rand(-5, 200)},
                    {"mesh", $"Mesh{sequence}"},
                    {"skin", $"Skin{sequence}"},
                    {"face", $"Face{sequence}"},
                    {"ping", Rand(20, 300)},
                    {"countryc", "none"},
                    {"deaths", 0}
                }
                dto.Players.Add(player)
                dto.Info("numplayers") = Integer.Parse(dto.Info("numplayers")) + 1
                dto.Info("__uttrealplayers") = Integer.Parse(dto.Info("__uttrealplayers")) + 1
                sequence += 1
            Next
        End Sub

        Private Function CreateTestServer() As ServerInfoSyncTuple
            Dim dbCtx = CreateDbContext()
            Dim repo = New ServerRepository(dbCtx)

            Dim dto = FakeServerDto()
            Dim sync = New ServerDataPersistence(dto, dbCtx, repo)

            Dim tuple As ServerInfoSyncTuple
            tuple.Dto = dto
            tuple.PersistenceService = sync
            tuple.DatabaseContext = dbCtx
            tuple.Repository = repo

            Return tuple
        End Function

        Private Shared Function Rand(min As Integer, max As Integer) As Integer
            Static randomGen = New Random()
            Return randomGen.next(min, max)
        End Function

        Private Structure ServerInfoSyncTuple
            Dim DatabaseContext As Utt2Context
            Dim Repository As ServerRepository
            Dim PersistenceService As ServerDataPersistence
            Dim Dto As ServerInfo
        End Structure

    End Class

End Namespace