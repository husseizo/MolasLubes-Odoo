namespace MolasLubes.Domain.Entities.Neon;

public class NeonAutoHubProduct
{
    public string ItemCode  { get; set; } = null!;
    public string ItemName  { get; set; } = null!;
    public decimal OnHandSap      { get; set; }
    public decimal AvailableCache { get; set; }
    public bool     IsActive             { get; set; }
    public DateTime SyncedAt             { get; set; }

    public string?  U_MdlTEST            { get; set; }
    public string?  U_Item_Name          { get; set; }
    public string?  U_Article_No         { get; set; }
    public string?  U_ReferenceNum       { get; set; }
    public string?  U_OriginalNumber     { get; set; }
    public string?  U_PT_No_Inproduction { get; set; }
}
