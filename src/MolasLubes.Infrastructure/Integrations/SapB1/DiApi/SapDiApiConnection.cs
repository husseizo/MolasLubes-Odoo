using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

public class SapDiApiConnection : IDisposable
{
    private readonly SapSettings _settings;
    private readonly ILogger<SapDiApiConnection> _logger;
    private Company? _company;

    public SapDiApiConnection(
        IOptions<SapSettings> options,
        ILogger<SapDiApiConnection> logger)
    {
        _settings = options.Value;
        _logger = logger;

        LogConfigurationSummary();
    }

    /// <summary>
    /// Logs SAP configuration summary WITHOUT sensitive data
    /// </summary>
    private void LogConfigurationSummary()
    {
        _logger.LogInformation(
            "🔧 SAP DI API config loaded | Server={Server} | CompanyDB={CompanyDB} | User={User} | DbType={DbType} | LicenseServer={LicenseServer} | SLDServer={SLDServer}",
            _settings.Server,
            _settings.CompanyDB,
            _settings.UserName,
            _settings.DbServerType,
            _settings.LicenseServer,
            _settings.SLDServer);
    }

    private Company CreateCompany()
    {
        _logger.LogInformation(
            "🔌 Initializing SAP Company object for {CompanyDB} on {Server}",
            _settings.CompanyDB,
            _settings.Server);

        return new Company
        {
            Server = _settings.Server,
            CompanyDB = _settings.CompanyDB,
            UserName = _settings.UserName,
            Password = _settings.Password, // NEVER log
            DbServerType = Enum.Parse<BoDataServerTypes>($"dst_{_settings.DbServerType}"),
            language = BoSuppLangs.ln_English,
            UseTrusted = false,
            LicenseServer = _settings.LicenseServer,
            SLDServer = _settings.SLDServer
        };
    }

    public Company GetConnectedCompany()
    {
        if (_company != null && _company.Connected)
        {
            _logger.LogDebug("ℹ️ Reusing existing SAP DI API connection");
            return _company;
        }

        var company = CreateCompany();
        var rc = company.Connect();

        if (rc != 0)
        {
            company.GetLastError(out var code, out var message);

            _logger.LogError(
                "❌ SAP DI API connection failed | Code={Code} | Message={Message}",
                code,
                message);

            throw new Exception($"SAP Connection failed ({code}): {message}");
        }

        _logger.LogInformation(
            "✅ SAP DI API connected successfully | CompanyDB={CompanyDB}",
            _settings.CompanyDB);

        _company = company;
        return _company;
    }



    public Company CreateNewCompany()
    {
        _logger.LogInformation(
            "🧵 Creating NEW SAP Company instance (thread-safe)");

        return new Company
        {
            Server = _settings.Server,
            CompanyDB = _settings.CompanyDB,
            UserName = _settings.UserName,
            Password = _settings.Password, // never log
            DbServerType = Enum.Parse<BoDataServerTypes>($"dst_{_settings.DbServerType}"),
            language = BoSuppLangs.ln_English,
            UseTrusted = false,
            LicenseServer = _settings.LicenseServer,
            SLDServer = _settings.SLDServer
        };
    }

    public void Dispose()
    {
        if (_company != null && _company.Connected)
        {
            _company.Disconnect();
            _logger.LogInformation("🔌 SAP DI API disconnected cleanly");
        }
    }
}