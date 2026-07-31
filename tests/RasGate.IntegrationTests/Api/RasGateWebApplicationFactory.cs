using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using RasGate.Web;
using RasGate.Web.Authentication;

namespace RasGate.IntegrationTests.Api;

public sealed class RasGateWebApplicationFactory
    : WebApplicationFactory<Program>
{
    private readonly string _apiKey;
    private readonly string _executablePath;
    private readonly string _instanceName;
    private readonly int _maxConcurrentProcesses;
    private readonly int _maxOutputBytes;
    private readonly int _timeoutSeconds;

    public RasGateWebApplicationFactory(
        string executablePath,
        int timeoutSeconds = 5,
        int maxConcurrentProcesses = 2,
        int maxOutputBytes = 4194304,
        string instanceName = "RasGate Test Application",
        string apiKey = "rasgate-integration-test-api-key")
    {
        _apiKey = apiKey;
        _executablePath = executablePath;
        _timeoutSeconds = timeoutSeconds;
        _maxConcurrentProcesses = maxConcurrentProcesses;
        _maxOutputBytes = maxOutputBytes;
        _instanceName = instanceName;
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();

        client.DefaultRequestHeaders.Add(
            ApiKeyAuthenticationDefaults.HeaderName,
            _apiKey);

        return client;
    }

    protected override void ConfigureWebHost(
        IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Rac:ExecutablePath"] = _executablePath,
                    ["Rac:TimeoutSeconds"] = _timeoutSeconds.ToString(),
                    ["Rac:MaxConcurrentProcesses"] = _maxConcurrentProcesses.ToString(),
                    ["Rac:MaxOutputBytes"] = _maxOutputBytes.ToString(),
                    ["RasGate:InstanceName"] = _instanceName,
                    ["RasGate:ApiKey"] = _apiKey
                });
        });
    }
}