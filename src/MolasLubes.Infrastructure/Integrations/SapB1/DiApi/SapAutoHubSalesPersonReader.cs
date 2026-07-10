#pragma warning disable CA1416 // COM interop — Windows only

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MolasLubes.Infrastructure.Integrations.SapB1.Profiles;
using SAPbobsCOM;
using System.Runtime.InteropServices;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

public record AutoHubSalesPersonRow(int SlpCode, string SlpName, bool IsActive, string? Email);

public class SapAutoHubSalesPersonReader
{
    private const string ProfileKey = "AutoHub";

    private readonly IntegrationProfilesOptions _profiles;
    private readonly ILogger<SapAutoHubSalesPersonReader> _logger;

    public SapAutoHubSalesPersonReader(
        IOptions<IntegrationProfilesOptions> profileOptions,
        ILogger<SapAutoHubSalesPersonReader> logger)
    {
        _profiles = profileOptions.Value;
        _logger   = logger;
    }

    public List<AutoHubSalesPersonRow> ReadAll()
    {
        if (!_profiles.Profiles.TryGetValue(ProfileKey, out var profile))
            throw new InvalidOperationException($"Integration profile '{ProfileKey}' is not configured.");

        var results    = new List<AutoHubSalesPersonRow>();
        Exception? threadEx = null;

        var thread = new Thread(() =>
        {
            Company?   company = null;
            Recordset? rs      = null;
            try
            {
                company = SapAutoHubDocumentReader.Connect(profile.Sap);
                rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
                rs.DoQuery(@"
SELECT SlpCode, SlpName, Active, Email
FROM OSLP
ORDER BY SlpCode");

                while (!rs.EoF)
                {
                    var code = ToInt(rs, "SlpCode");
                    var name = Str(rs, "SlpName") ?? "";
                    var active = Str(rs, "Active") == "Y";
                    var email = Str(rs, "Email");

                    results.Add(new AutoHubSalesPersonRow(code, name, active, email));
                    rs.MoveNext();
                }
            }
            catch (Exception ex) { threadEx = ex; }
            finally
            {
                if (rs      != null) Marshal.ReleaseComObject(rs);
                if (company is { Connected: true }) company.Disconnect();
                if (company != null) Marshal.ReleaseComObject(company);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx != null) throw threadEx;

        _logger.LogInformation("SapAutoHubSalesPersonReader.ReadAll: {Count} rows", results.Count);
        return results;
    }

    private static int ToInt(Recordset rs, string field) =>
        int.Parse(rs.Fields.Item(field).Value?.ToString() ?? "0");

    private static string? Str(Recordset rs, string field)
    {
        var v = rs.Fields.Item(field).Value;
        return v == null || v is DBNull ? null : v.ToString()?.Trim();
    }
}
