Imports System
Imports System.Collections.Generic
Imports Microsoft.EntityFrameworkCore
Imports Microsoft.VisualBasic

Namespace Utt2Database
    Partial Public Class Utt2Context
        Inherits DbContext

        Private dbConfig As MySQLDBConfig

        Public Sub New()
        End Sub

        Public Sub New(options As DbContextOptions(Of Utt2Context))
            MyBase.New(options)
        End Sub

        Public Sub New(connectionConfig As MySQLDBConfig)
            dbConfig = connectionConfig

        End Sub

        Public Overridable Property ConfigProps As DbSet(Of ConfigProp)

        Public Overridable Property Players As DbSet(Of Player)

        Public Overridable Property PlayerLiveLogs As DbSet(Of PlayerLiveLog)

        Public Overridable Property PlayerLogs As DbSet(Of PlayerLog)

        Public Overridable Property PlayerStats As DbSet(Of PlayerStat)

        Public Overridable Property ScanQueueEntries As DbSet(Of ScanQueueEntry)

        Public Overridable Property Servers As DbSet(Of Server)

        Public Overridable Property ServerMatches As DbSet(Of ServerMatch)

        Protected Overrides Sub OnConfiguring(optionsBuilder As DbContextOptionsBuilder)
            Dim connString As String = MySQLDB.makeConnectionStringFromConfigStruct(dbConfig)
            optionsBuilder.UseMySQL(connString)
        End Sub

        Protected Overrides Sub OnModelCreating(modelBuilder As ModelBuilder)
            modelBuilder.Entity(Of ConfigProp)(
                Sub(entity)
                    entity.HasKey(Function(e) e.Key).HasName("PRIMARY")

                    entity.ToTable("config_props")

                    entity.Property(Function(e) e.Key).
                        HasMaxLength(100).
                        HasColumnName("key")
                    entity.Property(Function(e) e.Data).
                        IsRequired().
                        HasMaxLength(255).
                        HasDefaultValueSql("''''''").
                        HasColumnName("data")
                    entity.Property(Function(e) e.[Private]).HasColumnName("private")
                End Sub)

            modelBuilder.Entity(Of Player)(
                Sub(entity)
                    entity.HasKey(Function(e) e.Id).HasName("PRIMARY")

                    entity.ToTable("players")

                    entity.Property(Function(e) e.Id).
                        HasColumnType("bigint(20) unsigned").
                        HasColumnName("id")
                    entity.Property(Function(e) e.Country).
                        HasMaxLength(3).
                        HasDefaultValueSql("'NULL'").
                        HasColumnName("country")
                    entity.Property(Function(e) e.Name).
                        IsRequired().
                        HasMaxLength(255).
                        HasColumnName("name")
                    entity.Property(Function(e) e.SkinData).
                        IsRequired().
                        HasMaxLength(255).
                        HasColumnName("skin_data")
                End Sub)

            modelBuilder.Entity(Of PlayerLiveLog)(
                Sub(entity)
                    entity.HasKey(Function(e) e.Id).HasName("PRIMARY")

                    entity.ToTable("player_live_logs")

                    entity.Property(Function(e) e.Id).
                        HasColumnType("bigint(20) unsigned").
                        HasColumnName("id")
                    entity.Property(Function(e) e.DeathsThisMatch).
                        HasDefaultValueSql("'NULL'").
                        HasColumnType("int(11)").
                        HasColumnName("deaths_this_match")
                    entity.Property(Function(e) e.FirstSeenTime).
                        HasDefaultValueSql("'current_timestamp()'").
                        HasColumnType("timestamp").
                        HasColumnName("first_seen_time")
                    entity.Property(Function(e) e.LastSeenTime).
                        HasDefaultValueSql("'current_timestamp()'").
                        HasColumnType("timestamp").
                        HasColumnName("last_seen_time")
                    entity.Property(Function(e) e.MatchId).
                        HasColumnType("int(11)").
                        HasColumnName("match_id")
                    entity.Property(Function(e) e.PingSum).
                        HasColumnType("int(11)").
                        HasColumnName("ping_sum")
                    entity.Property(Function(e) e.PlayerId).
                        HasColumnType("int(11)").
                        HasColumnName("player_id")
                    entity.Property(Function(e) e.ScoreThisMatch).
                        HasColumnType("bigint(20)").
                        HasColumnName("score_this_match")
                    entity.Property(Function(e) e.SeenCount).
                        HasColumnType("int(11)").
                        HasColumnName("seen_count")
                    entity.Property(Function(e) e.ServerId).
                        HasColumnType("int(11)").
                        HasColumnName("server_id")
                    entity.Property(Function(e) e.Team).
                        HasColumnType("int(11)").
                        HasColumnName("team")
                End Sub)

            modelBuilder.Entity(Of PlayerLog)(
                Sub(entity)
                    entity.HasKey(Function(e) e.Id).HasName("PRIMARY")

                    entity.ToTable("player_logs")

                    entity.Property(Function(e) e.Id).
                        HasColumnType("bigint(20) unsigned").
                        HasColumnName("id")
                    entity.Property(Function(e) e.DeathsThisMatch).
                        HasDefaultValueSql("'NULL'").
                        HasColumnType("int(11)").
                        HasColumnName("deaths_this_match")
                    entity.Property(Function(e) e.FirstSeenTime).
                        HasDefaultValueSql("'current_timestamp()'").
                        HasColumnType("timestamp").
                        HasColumnName("first_seen_time")
                    entity.Property(Function(e) e.LastSeenTime).
                        HasDefaultValueSql("'current_timestamp()'").
                        HasColumnType("timestamp").
                        HasColumnName("last_seen_time")
                    entity.Property(Function(e) e.MatchId).
                        HasColumnType("int(11)").
                        HasColumnName("match_id")
                    entity.Property(Function(e) e.PingSum).
                        HasColumnType("int(11)").
                        HasColumnName("ping_sum")
                    entity.Property(Function(e) e.PlayerId).
                        HasColumnType("int(11)").
                        HasColumnName("player_id")
                    entity.Property(Function(e) e.ScoreThisMatch).
                        HasColumnType("bigint(20)").
                        HasColumnName("score_this_match")
                    entity.Property(Function(e) e.SeenCount).
                        HasColumnType("int(11)").
                        HasColumnName("seen_count")
                    entity.Property(Function(e) e.ServerId).
                        HasColumnType("int(11)").
                        HasColumnName("server_id")
                    entity.Property(Function(e) e.Team).
                        HasColumnType("int(11)").
                        HasColumnName("team")
                End Sub)

            modelBuilder.Entity(Of PlayerStat)(
                Sub(entity)
                    entity.
                    HasNoKey().
                    ToTable("player_stats")

                    entity.HasIndex(Function(e) New With {e.PlayerId, e.ServerId}, "player_stats_player_id_server_id_unique").IsUnique()

                    entity.Property(Function(e) e.Deaths).
                        HasColumnType("int(11)").
                        HasColumnName("deaths")
                    entity.Property(Function(e) e.GameTime).
                        HasColumnType("int(11)").
                        HasColumnName("game_time")
                    entity.Property(Function(e) e.LastMatchId).
                        HasColumnType("int(11)").
                        HasColumnName("last_match_id")
                    entity.Property(Function(e) e.PlayerId).
                        HasColumnType("int(11)").
                        HasColumnName("player_id")
                    entity.Property(Function(e) e.Score).
                        HasColumnType("bigint(20)").
                        HasColumnName("score")
                    entity.Property(Function(e) e.ServerId).
                        HasColumnType("int(11)").
                        HasColumnName("server_id")
                End Sub)

            modelBuilder.Entity(Of ScanQueueEntry)(
                Sub(entity)
                    entity.HasKey(Function(e) e.Id).HasName("PRIMARY")

                    entity.ToTable("scan_queue_entries")

                    entity.Property(Function(e) e.Id).
                        HasColumnType("bigint(20) unsigned").
                        HasColumnName("id")
                    entity.Property(Function(e) e.Address).
                        IsRequired().
                        HasMaxLength(40).
                        HasColumnName("address")
                    entity.Property(Function(e) e.Flags).
                        HasColumnType("int(11)").
                        HasColumnName("flags")
                End Sub)

            modelBuilder.Entity(Of Server)(
                Sub(entity)
                    entity.HasKey(Function(e) e.Id).HasName("PRIMARY")

                    entity.ToTable("servers")

                    entity.Property(Function(e) e.Id).
                        HasColumnType("bigint(20) unsigned").
                        HasColumnName("id")
                    entity.Property(Function(e) e.Address).
                        IsRequired().
                        HasMaxLength(40).
                        HasColumnName("address")
                    entity.Property(Function(e) e.Country).
                        HasMaxLength(3).
                        HasDefaultValueSql("'NULL'").
                        HasColumnName("country")
                    entity.Property(Function(e) e.GameName).
                        IsRequired().
                        HasMaxLength(255).
                        HasColumnName("game_name")
                    entity.Property(Function(e) e.LastRankCalculation).
                        HasDefaultValueSql("'NULL'").
                        HasColumnType("timestamp").
                        HasColumnName("last_rank_calculation")
                    entity.Property(Function(e) e.LastScan).
                        HasDefaultValueSql("'NULL'").
                        HasColumnType("timestamp").
                        HasColumnName("last_scan")
                    entity.Property(Function(e) e.Name).
                        IsRequired().
                        HasMaxLength(255).
                        HasColumnName("name")
                    entity.Property(Function(e) e.RfScore).
                        HasColumnType("int(11)").
                        HasColumnName("rf_score")
                    entity.Property(Function(e) e.Rules).
                        IsRequired().
                        HasDefaultValueSql("'''{}'''").
                        HasColumnName("rules")
                End Sub)

            modelBuilder.Entity(Of ServerMatch)(
                Sub(entity)
                    entity.HasKey(Function(e) e.Id).HasName("PRIMARY")

                    entity.ToTable("server_matches")

                    entity.Property(Function(e) e.Id).
                        HasColumnType("bigint(20) unsigned").
                        HasColumnName("id")
                    entity.Property(Function(e) e.InternalMatchId).
                        HasDefaultValueSql("'NULL'").
                        HasColumnType("int(11)").
                        HasColumnName("internal_match_id")
                    entity.Property(Function(e) e.MapName).
                        IsRequired().
                        HasMaxLength(255).
                        HasColumnName("map_name")
                    entity.Property(Function(e) e.ServerId).
                        HasColumnType("int(11)").
                        HasColumnName("server_id")
                    entity.Property(Function(e) e.StartTime).
                        ValueGeneratedOnAddOrUpdate().
                        HasDefaultValueSql("'current_timestamp()'").
                        HasColumnType("timestamp").
                        HasColumnName("start_time")
                End Sub)

            OnModelCreatingPartial(modelBuilder)
        End Sub

        Partial Private Sub OnModelCreatingPartial(modelBuilder As ModelBuilder)
        End Sub
    End Class
End Namespace
