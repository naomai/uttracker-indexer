using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace Naomai.UTT.Indexer.Utt2Database;
public partial class PlayerLog
{
    public long? Id { get; set; }
    public long PlayerId { get; set; }
    public long ServerId { get; set; }
    public long MatchId { get; set; }
    public int SeenCount { get; set; }
    public DateTime LastSeenTime { get; set; }
    public DateTime FirstSeenTime { get; set; }
    public long ScoreThisMatch { get; set; }
    public int? DeathsThisMatch { get; set; }
    public int PingSum { get; set; }
    public int Team { get; set; }
    public bool Finished { get; set; }
    public ServerMatch Match { get; set; } = null!;
    public Player Player { get; set; } = null!;
    public Server Server { get; set; } = null!;

    public override string ToString()
    {
        return "PL" +PlayerId + "#M" + MatchId + "@S" + ServerId;
    }
}