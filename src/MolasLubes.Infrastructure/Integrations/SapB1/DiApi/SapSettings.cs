namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

public class SapSettings
{
    public string Server { get; set; } = null!;
    public string CompanyDB { get; set; } = null!;
    public string UserName { get; set; } = null!;
    public string Password { get; set; } = null!;
    public string DbServerType { get; set; } = null!;
    public string LicenseServer { get; set; } = null!;
    public string SLDServer { get; set; } = null!;

    /// <summary>SAP numbering series name for customer (OCRD). Defaults to "CSR".</summary>
    public string CustomerSeries { get; set; } = "CSR";
}