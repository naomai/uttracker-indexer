namespace Naomai.UTT.Indexer.Utt2Database;
public class PlayerStat
{
    public long? Id { get; set; }
    public long PlayerId { get; set; }
    public long ServerId { get; set; }
    public int GameTime { get; set; }
    public long Score { get; set; }
    public int Deaths { get; set; }
    public long LastMatchId { get; set; }
    public virtual ServerMatch LastMatch { get; set; } = null!;
    public virtual Player Player { get; set; } = null!;
    public virtual Server Server { get; set; } = null!;

    public override string ToString()
    {
        return "PS" + PlayerId + "@S" + ServerId;
    }
}