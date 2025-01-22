﻿namespace Naomai.UTT.Indexer.Utt2Database;
public partial class Player
{
    public long? Id { get; set; }
    public string Slug { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string SkinData { get; set; } = null!;
    public string Country { get; set; } = null!;
    public ICollection<PlayerLog> PlayerLogs { get; set; } = new List<PlayerLog>();
    public ICollection<PlayerStat> PlayerStats { get; set; } = new List<PlayerStat>();
}