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
            MyBase.New()
            dbConfig = connectionConfig

        End Sub

        Public Overridable Property ConfigProps As DbSet(Of ConfigProp)

        Public Overridable Property Players As DbSet(Of Player)

        Public Overridable Property PlayerLogs As DbSet(Of PlayerLog)

        Public Overridable Property PlayerStats As DbSet(Of PlayerStat)

        Public Overridable Property ScanQueueEntries As DbSet(Of ScanQueueEntry)

        Public Overridable Property Servers As DbSet(Of Server)

        Public Overridable Property ServerMatches As DbSet(Of ServerMatch)

        Protected Overrides Sub OnConfiguring(optionsBuilder As DbContextOptionsBuilder)
            Dim connString As String = MySQLDB.makeConnectionStringFromConfigStruct(dbConfig)
            optionsBuilder.UseMySQL(connString)
            'optionsBuilder.LogTo(AddressOf Console.WriteLine)
            ' optionsBuilder.EnableSensitiveDataLogging(True)
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
                    entity.Property(Function(e) e.IsPrivate).HasColumnName("is_private")
                End Sub)

            modelBuilder.Entity(Of Player)(
                Sub(entity)
                    entity.HasKey(Function(e) e.Id).HasName("PRIMARY")

                    entity.ToTable("players")

                    entity.HasIndex(Function(e) e.Slug, "players_slug_unique").IsUnique()

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
                    entity.Property(Function(e) e.Slug).
                        IsRequired().
                        HasMaxLength(80).
                        HasColumnName("slug")
                End Sub)

            modelBuilder.Entity(Of PlayerLog)(
                Sub(entity)
                    entity.HasKey(Function(e) e.Id).HasName("PRIMARY")

                    entity.ToTable("player_logs")

                    entity.HasIndex(Function(e) e.MatchId, "player_logs_match_id_foreign")

                    entity.HasIndex(Function(e) e.PlayerId, "player_logs_player_id_foreign")

                    entity.HasIndex(Function(e) e.ServerId, "player_logs_server_id_foreign")

                    entity.HasIndex(Function(e) New With {e.PlayerId, e.MatchId}, "player_logs_player_id_match_id_unique").IsUnique()


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
                        HasColumnType("bigint(20) unsigned").
                        HasColumnName("match_id")
                    entity.Property(Function(e) e.PingSum).
                        HasColumnType("int(11)").
                        HasColumnName("ping_sum")
                    entity.Property(Function(e) e.PlayerId).
                        HasColumnType("bigint(20) unsigned").
                        HasColumnName("player_id")
                    entity.Property(Function(e) e.ScoreThisMatch).
                        HasColumnType("bigint(20)").
                        HasColumnName("score_this_match")
                    entity.Property(Function(e) e.SeenCount).
                        HasColumnType("int(11)").
                        HasColumnName("seen_count")
                    entity.Property(Function(e) e.ServerId).
                        HasColumnType("bigint(20) unsigned").
                        HasColumnName("server_id")
                    entity.Property(Function(e) e.Team).
                        HasColumnType("int(11)").
                        HasColumnName("team")
                    entity.Property(Function(e) e.Finished).
                        HasColumnType("tinyint(1)").
                        HasColumnName("finished")

                    entity.HasOne(Function(d) d.Match).WithMany(Function(p) p.PlayerLogs).
                        HasForeignKey(Function(d) d.MatchId).
                        OnDelete(DeleteBehavior.Restrict).
                        HasConstraintName("player_logs_match_id_foreign")

                    entity.HasOne(Function(d) d.Player).WithMany(Function(p) p.PlayerLogs).
                        HasForeignKey(Function(d) d.PlayerId).
                        OnDelete(DeleteBehavior.Restrict).
                        HasConstraintName("player_logs_player_id_foreign")

                    entity.HasOne(Function(d) d.Server).WithMany(Function(p) p.PlayerLogs).
                        HasForeignKey(Function(d) d.ServerId).
                        OnDelete(DeleteBehavior.Restrict).
                        HasConstraintName("player_logs_server_id_foreign")
                End Sub)

            modelBuilder.Entity(Of PlayerStat)(
                Sub(entity)
                    entity.HasKey(Function(e) e.Id).HasName("PRIMARY")

                    entity.ToTable("player_stats")

                    entity.HasIndex(Function(e) e.LastMatchId, "player_stats_last_match_id_foreign")

                    entity.HasIndex(Function(e) New With {e.PlayerId, e.ServerId}, "player_stats_player_id_server_id_unique").IsUnique()

                    entity.HasIndex(Function(e) e.ServerId, "player_stats_server_id_foreign")

                    entity.Property(Function(e) e.Id).
                        HasColumnType("bigint(20) unsigned").
                        HasColumnName("id")
                    entity.Property(Function(e) e.Deaths).
                        HasColumnType("int(11)").
                        HasColumnName("deaths")
                    entity.Property(Function(e) e.GameTime).
                        HasColumnType("int(11)").
                        HasColumnName("game_time")
                    entity.Property(Function(e) e.LastMatchId).
                        HasColumnType("bigint(20) unsigned").
                        HasColumnName("last_match_id")
                    entity.Property(Function(e) e.PlayerId).
                        HasColumnType("bigint(20) unsigned").
                        HasColumnName("player_id")
                    entity.Property(Function(e) e.Score).
                        HasColumnType("bigint(20)").
                        HasColumnName("score")
                    entity.Property(Function(e) e.ServerId).
                        HasColumnType("bigint(20) unsigned").
                        HasColumnName("server_id")

                    entity.HasOne(Function(d) d.LastMatch).WithMany(Function(p) p.PlayerStats).
                        HasForeignKey(Function(d) d.LastMatchId).
                        OnDelete(DeleteBehavior.Restrict).
                        HasConstraintName("player_stats_last_match_id_foreign")

                    entity.HasOne(Function(d) d.Player).WithMany(Function(p) p.PlayerStats).
                        HasForeignKey(Function(d) d.PlayerId).
                        OnDelete(DeleteBehavior.Restrict).
                        HasConstraintName("player_stats_player_id_foreign")

                    entity.HasOne(Function(d) d.Server).WithMany(Function(p) p.PlayerStats).
                        HasForeignKey(Function(d) d.ServerId).
                        OnDelete(DeleteBehavior.Restrict).
                        HasConstraintName("player_stats_server_id_foreign")
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

                    entity.HasIndex(Function(e) e.Address, "servers_address_unique").IsUnique()

                    entity.Property(Function(e) e.Id).
                        HasColumnType("bigint(20) unsigned").
                        HasColumnName("id")
                    entity.Property(Function(e) e.Address).
                        IsRequired().
                        HasMaxLength(60).
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
                    entity.Property(Function(e) e.LastCheck).
                        HasDefaultValueSql("'NULL'").
                        HasColumnType("timestamp").
                        HasColumnName("last_check")
                    entity.Property(Function(e) e.LastSuccess).
                        HasDefaultValueSql("'NULL'").
                        HasColumnType("timestamp").
                        HasColumnName("last_success")
                    entity.Property(Function(e) e.LastValidation).
                        HasDefaultValueSql("'NULL'").
                        HasColumnType("timestamp").
                        HasColumnName("last_validation")
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

                    entity.HasIndex(Function(e) e.ServerId, "server_matches_server_id_foreign")

                    entity.Property(Function(e) e.Id).
                        HasColumnType("bigint(20) unsigned").
                        HasColumnName("id")
                    entity.Property(Function(e) e.ServerPlayeridCounter).
                        HasDefaultValueSql("'NULL'").
                        HasColumnType("int(11)").
                        HasColumnName("server_playerid_counter")
                    entity.Property(Function(e) e.MapName).
                        IsRequired().
                        HasMaxLength(255).
                        HasColumnName("map_name")
                    entity.Property(Function(e) e.ServerId).
                        HasColumnType("bigint(20) unsigned").
                        HasColumnName("server_id")
                    entity.Property(Function(e) e.StartTime).
                        HasColumnType("timestamp").
                        HasColumnName("start_time")

                    entity.HasOne(Function(d) d.Server).WithMany(Function(p) p.ServerMatches).
                        HasForeignKey(Function(d) d.ServerId).
                        OnDelete(DeleteBehavior.Restrict).
                        HasConstraintName("server_matches_server_id_foreign")
                End Sub)

            OnModelCreatingPartial(modelBuilder)
        End Sub

        Partial Private Sub OnModelCreatingPartial(modelBuilder As ModelBuilder)
        End Sub
    End Class
End Namespace
