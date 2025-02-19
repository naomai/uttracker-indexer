using Microsoft.EntityFrameworkCore;



namespace Naomai.UTT.Indexer.Utt2Database
{
    public partial class Utt2Context : DbContext
    {
        private MySQLDBConfig dbConfig;

        public Utt2Context()
        {
        }

        public Utt2Context(DbContextOptions<Utt2Context> options) : base(options)
        {
        }

        public Utt2Context(MySQLDBConfig connectionConfig) : base()
        {
            dbConfig = connectionConfig;
        }

        public virtual DbSet<ConfigProp> ConfigProps { get; set; }

        public virtual DbSet<Player> Players { get; set; }

        public virtual DbSet<PlayerLog> PlayerLogs { get; set; }

        public virtual DbSet<PlayerStat> PlayerStats { get; set; }

        public virtual DbSet<ScanQueueEntry> ScanQueueEntries { get; set; }

        public virtual DbSet<Server> Servers { get; set; }

        public virtual DbSet<ServerMatch> ServerMatches { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            string connString = MySQLDB.makeConnectionStringFromConfigStruct(dbConfig);
            optionsBuilder.UseMySQL(connString, options => options.EnableRetryOnFailure());
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ConfigProp>(entity =>
            {
                entity.HasKey(e => e.Key).HasName("PRIMARY");

                entity.ToTable("config_props");

                entity.Property(e => e.Key).HasMaxLength(100).HasColumnName("key");
                entity.Property(e => e.Data).IsRequired().HasMaxLength(255).HasDefaultValueSql("''''''").HasColumnName("data");
                entity.Property(e => e.IsPrivate).HasColumnName("is_private");
            });

            modelBuilder.Entity<Player>(entity =>
            {
                entity.HasKey(e => e.Id).HasName("PRIMARY");

                entity.ToTable("players");

                entity.HasIndex(e => e.Slug, "players_slug_unique").IsUnique();

                entity.Property(e => e.Id).HasColumnType("bigint(20) unsigned").HasColumnName("id");
                entity.Property(e => e.Country).HasMaxLength(3).HasDefaultValueSql("'NULL'").HasColumnName("country");
                entity.Property(e => e.Name).IsRequired().HasMaxLength(255).HasColumnName("name");
                entity.Property(e => e.SkinData).IsRequired().HasMaxLength(255).HasColumnName("skin_data");
                entity.Property(e => e.Slug).IsRequired().HasMaxLength(80).HasColumnName("slug");
            });

            modelBuilder.Entity<PlayerLog>(entity =>
            {
                entity.HasKey(e => e.Id).HasName("PRIMARY");

                entity.ToTable("player_logs");

                entity.HasIndex(e => e.MatchId, "player_logs_match_id_foreign");

                entity.HasIndex(e => e.PlayerId, "player_logs_player_id_foreign");

                entity.HasIndex(e => e.ServerId, "player_logs_server_id_foreign");

                entity.HasIndex(e => new { e.PlayerId, e.MatchId }, "player_logs_player_id_match_id_unique").IsUnique();


                entity.Property(e => e.Id).HasColumnType("bigint(20) unsigned").HasColumnName("id");
                entity.Property(e => e.DeathsThisMatch).HasDefaultValueSql("'NULL'").HasColumnType("int(11)").HasColumnName("deaths_this_match");
                entity.Property(e => e.FirstSeenTime).HasDefaultValueSql("'current_timestamp()'").HasColumnType("timestamp").HasColumnName("first_seen_time");
                entity.Property(e => e.LastSeenTime).HasDefaultValueSql("'current_timestamp()'").HasColumnType("timestamp").HasColumnName("last_seen_time");
                entity.Property(e => e.MatchId).HasColumnType("bigint(20) unsigned").HasColumnName("match_id");
                entity.Property(e => e.PingSum).HasColumnType("int(11)").HasColumnName("ping_sum");
                entity.Property(e => e.PlayerId).HasColumnType("bigint(20) unsigned").HasColumnName("player_id");
                entity.Property(e => e.ScoreThisMatch).HasColumnType("bigint(20)").HasColumnName("score_this_match");
                entity.Property(e => e.SeenCount).HasColumnType("int(11)").HasColumnName("seen_count");
                entity.Property(e => e.ServerId).HasColumnType("bigint(20) unsigned").HasColumnName("server_id");
                entity.Property(e => e.Team).HasColumnType("int(11)").HasColumnName("team");
                entity.Property(e => e.Finished).HasColumnType("tinyint(1)").HasColumnName("finished");

                entity.HasOne(d => d.Match).WithMany(p => p.PlayerLogs).HasForeignKey(d => d.MatchId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("player_logs_match_id_foreign");

                entity.HasOne(d => d.Player).WithMany(p => p.PlayerLogs).HasForeignKey(d => d.PlayerId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("player_logs_player_id_foreign");

                entity.HasOne(d => d.Server).WithMany(p => p.PlayerLogs).HasForeignKey(d => d.ServerId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("player_logs_server_id_foreign");
            });

            modelBuilder.Entity<PlayerStat>(entity =>
            {
                entity.HasKey(e => e.Id).HasName("PRIMARY");

                entity.ToTable("player_stats");

                entity.HasIndex(e => e.LastMatchId, "player_stats_last_match_id_foreign");

                entity.HasIndex(e => new { e.PlayerId, e.ServerId }, "player_stats_player_id_server_id_unique").IsUnique();

                entity.HasIndex(e => e.ServerId, "player_stats_server_id_foreign");

                entity.Property(e => e.Id).HasColumnType("bigint(20) unsigned").HasColumnName("id");
                entity.Property(e => e.Deaths).HasColumnType("int(11)").HasColumnName("deaths");
                entity.Property(e => e.GameTime).HasColumnType("int(11)").HasColumnName("game_time");
                entity.Property(e => e.LastMatchId).HasColumnType("bigint(20) unsigned").HasColumnName("last_match_id");
                entity.Property(e => e.PlayerId).HasColumnType("bigint(20) unsigned").HasColumnName("player_id");
                entity.Property(e => e.Score).HasColumnType("bigint(20)").HasColumnName("score");
                entity.Property(e => e.ServerId).HasColumnType("bigint(20) unsigned").HasColumnName("server_id");

                entity.HasOne(d => d.LastMatch).WithMany(p => p.PlayerStats).HasForeignKey(d => d.LastMatchId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("player_stats_last_match_id_foreign");

                entity.HasOne(d => d.Player).WithMany(p => p.PlayerStats).HasForeignKey(d => d.PlayerId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("player_stats_player_id_foreign");

                entity.HasOne(d => d.Server).WithMany(p => p.PlayerStats).HasForeignKey(d => d.ServerId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("player_stats_server_id_foreign");
            });

            modelBuilder.Entity<ScanQueueEntry>(entity =>
            {
                entity.HasKey(e => e.Id).HasName("PRIMARY");

                entity.ToTable("scan_queue_entries");

                entity.Property(e => e.Id).HasColumnType("bigint(20) unsigned").HasColumnName("id");
                entity.Property(e => e.Address).IsRequired().HasMaxLength(40).HasColumnName("address");
                entity.Property(e => e.Flags).HasColumnType("int(11)").HasColumnName("flags");
            });

            modelBuilder.Entity<Server>(entity =>
            {
                entity.HasKey(e => e.Id).HasName("PRIMARY");

                entity.ToTable("servers");

                entity.HasIndex(e => e.AddressQuery, "servers_address_query_unique").IsUnique();
                entity.HasIndex(e => e.AddressGame, "servers_address_game_unique").IsUnique();

                entity.Property(e => e.Id).HasColumnType("bigint(20) unsigned").HasColumnName("id");
                entity.Property(e => e.AddressQuery).IsRequired().HasMaxLength(60).HasColumnName("address_query");
                entity.Property(e => e.AddressGame).IsRequired().HasMaxLength(60).HasColumnName("address_game");
                entity.Property(e => e.Country).HasMaxLength(3).HasDefaultValueSql("'NULL'").HasColumnName("country");
                entity.Property(e => e.GameName).IsRequired().HasMaxLength(255).HasColumnName("game_name");
                entity.Property(e => e.LastRatingCalculation).HasDefaultValueSql("'NULL'").HasColumnType("timestamp").HasColumnName("last_rating_calculation");
                entity.Property(e => e.LastCheck).HasDefaultValueSql("'NULL'").HasColumnType("timestamp").HasColumnName("last_check");
                entity.Property(e => e.LastSuccess).HasDefaultValueSql("'NULL'").HasColumnType("timestamp").HasColumnName("last_success");
                entity.Property(e => e.LastValidation).HasDefaultValueSql("'NULL'").HasColumnType("timestamp").HasColumnName("last_validation");
                entity.Property(e => e.Name).IsRequired().HasMaxLength(255).HasColumnName("name");
                entity.Property(e => e.RatingMonth).HasColumnType("int(11)").HasColumnName("rating_month");
                entity.Property(e => e.RatingMinute).HasColumnType("int(11)").HasColumnName("rating_minute");
                entity.Property(e => e.Variables).IsRequired().HasDefaultValueSql("'''{}'''").HasColumnName("variables");
            });

            modelBuilder.Entity<ServerMatch>(entity =>
            {
                entity.HasKey(e => e.Id).HasName("PRIMARY");

                entity.ToTable("server_matches");

                entity.HasIndex(e => e.ServerId, "server_matches_server_id_foreign");

                entity.Property(e => e.Id).HasColumnType("bigint(20) unsigned").HasColumnName("id");
                entity.Property(e => e.ServerPlayeridCounter).HasDefaultValueSql("'NULL'").HasColumnType("int(11)").HasColumnName("server_playerid_counter");
                entity.Property(e => e.MapName).IsRequired().HasMaxLength(255).HasColumnName("map_name");
                entity.Property(e => e.ServerId).HasColumnType("bigint(20) unsigned").HasColumnName("server_id");
                entity.Property(e => e.StartTime).HasColumnType("timestamp").HasColumnName("start_time");

                entity.HasOne(d => d.Server).WithMany(p => p.ServerMatches).HasForeignKey(d => d.ServerId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("server_matches_server_id_foreign");
            });

            OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}

public struct MySQLDBConfig
{
    public string host;
    public string protocol;
    public string username;
    public string password;
    public string database;
}

public class MySQLDB
{
    public static string makeConnectionStringFromConfigStruct(MySQLDBConfig connectionConfig)
    {
        string connectionString;
        { 
            if (connectionConfig.protocol == "pipe")
                connectionString = string.Format("server=.;uid={1};pwd={2};database={3};protocol=pipe;pipename={0};charset=utf8", 
                    connectionConfig.host, connectionConfig.username, connectionConfig.password, connectionConfig.database);
            else
                connectionString = string.Format("server={0};uid={1};pwd={2};database={3};protocol={4};charset=utf8", 
                    connectionConfig.host, connectionConfig.username, connectionConfig.password, connectionConfig.database, connectionConfig.protocol);
        }
        return connectionString;
    }
}