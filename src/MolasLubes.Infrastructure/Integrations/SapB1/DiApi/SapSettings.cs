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

    /// <summary>
    /// Branch ID (BPLId from OBPL) required when the company has multiple branches enabled.
    /// Leave null for single-branch companies (branch is set automatically by SAP).
    /// </summary>
    public int? BranchId { get; set; }

    /// <summary>
    /// SQL Server login for direct ADO.NET queries (e.g. pricing reads).
    /// Falls back to UserName/Password if not set, but SAP B1 users are often
    /// not valid SQL Server logins — set this to 'sa' or a dedicated SQL login.
    /// </summary>
    public string? SqlUserName { get; set; }
    public string? SqlPassword { get; set; }
}