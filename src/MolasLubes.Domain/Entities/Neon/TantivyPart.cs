namespace MolasLubes.Domain.Entities.Neon;

public class TantivyPart
{
    public string   ItemCode   { get; set; } = null!;
    public string   ItemName   { get; set; } = null!;
    public string?  MdlTest    { get; set; }
    public string?  ArticleNo  { get; set; }
    public string?  EngineCode { get; set; }
    public DateTime SyncedAt   { get; set; }
}
