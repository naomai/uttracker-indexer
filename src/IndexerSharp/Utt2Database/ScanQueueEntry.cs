namespace Naomai.UTT.Indexer.Utt2Database;
public partial class ScanQueueEntry
{
    public long? Id { get; set; }
    public string Address { get; set; } = null!;
    public int Flags { get; set; }
}