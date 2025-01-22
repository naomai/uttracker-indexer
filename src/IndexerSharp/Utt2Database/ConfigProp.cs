namespace Naomai.UTT.Indexer.Utt2Database;
public partial class ConfigProp
{
    public string Key { get; set; } = null!;
    public string Data { get; set; } = null!;
    public bool IsPrivate { get; set; }
    public override string ToString()
    {
        return Key;
    }
}
