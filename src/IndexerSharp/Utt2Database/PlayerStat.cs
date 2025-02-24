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
    public ServerMatch LastMatch { get; set; }
    public Player Player { get; set; }
    public Server Server { get; set; }

    public override string ToString()
    {
        return "PS" + PlayerId + "@S" + ServerId;
    }
}