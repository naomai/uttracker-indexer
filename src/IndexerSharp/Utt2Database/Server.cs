namespace Naomai.UTT.Indexer.Utt2Database;
public partial class Server
{
    public long? Id { get; set; }
    public string AddressQuery { get; set; } = null!;
    public string AddressGame { get; set; } = null!;
    public string Name  { get; set; }= null!;
    public string Variables { get; set; } = null!;
    
    public DateTime? LastCheck { get; set; } 
    public DateTime? LastSuccess { get; set; } 
    public DateTime? LastValidation { get; set; } 
    public DateTime? LastRatingCalculation { get; set; }
    public int RatingMonth { get; set; }
    public int RatingMinute { get; set; }
    public string? Country { get; set; } = null!;
    public string GameName { get; set; } = null!;

    public virtual ICollection<PlayerLog> PlayerLogs { get; } = new List<PlayerLog>();
    public virtual ICollection<PlayerStat> PlayerStats { get;  } = new List<PlayerStat>();
    public virtual ICollection<ServerMatch> ServerMatches { get;  } = new List<ServerMatch>();

    public override string ToString()
    {
        return "S" + Id + "#" + AddressGame + "[" + Name + "]";
    }
}