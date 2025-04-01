using System.Xml.Linq;

namespace Naomai.UTT.Indexer.Utt2Database;
public partial class ServerMatch
{
    public long? Id { get; set; } 
    public long ServerId { get; set; } 
    public DateTime StartTime { get; set; }
    public string MapName { get; set; } = null!;
    public int? ServerPlayeridCounter { get; set; }

    public virtual ICollection<PlayerLog> PlayerLogs { get; set; } = new List<PlayerLog>();
    public virtual ICollection<PlayerStat> PlayerStats { get; set; } = new List<PlayerStat>();
    public Server Server = null!;

    public override string ToString()
    {
        return "M" + Id + "#" + Server.AddressGame + "[" + MapName + "]";
    }
}