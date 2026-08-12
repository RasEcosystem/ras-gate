using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RasGate.Core.Common;
using RasGate.Core.Rac;
using RasGate.Web.Api;
using RasGate.Web.Authentication;

namespace RasGate.IntegrationTests.Api;

public sealed class RacControllerTests
{
    [Fact]
    public async Task GetStatus_AvailableRac_ReturnsAvailable()
    {
        await using var factory =
            new RasGateWebApplicationFactory(
                GetFakeRacPath());

        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/rac/status");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        using var document =
            JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());

        var root = document.RootElement;
        var data = root.GetProperty("data");

        Assert.True(
            root.GetProperty("success").GetBoolean());

        Assert.True(
            data.GetProperty("available").GetBoolean());

        Assert.Contains(
            "Remote Administrative Client",
            data.GetProperty("version").GetString());
    }

    [Fact]
    public async Task GetStatus_MissingRac_ReturnsUnavailable()
    {
        await using var factory =
            new RasGateWebApplicationFactory(
                GetMissingExecutablePath());

        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/rac/status");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        using var document =
            JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());

        var root = document.RootElement;
        var data = root.GetProperty("data");

        Assert.True(
            root.GetProperty("success").GetBoolean());

        Assert.False(
            data.GetProperty("available").GetBoolean());

        Assert.Equal(
            JsonValueKind.Null,
            data.GetProperty("version").ValueKind);

        Assert.Equal(
            "RAC executable could not be started.",
            data.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid-api-key")]
    public async Task Execute_WithoutValidApiKey_ReturnsUnauthorized(
        string? apiKey)
    {
        await using var factory =
            new RasGateWebApplicationFactory(
                GetFakeRacPath());

        using var client = factory.CreateClient();

        if (apiKey is not null)
            client.DefaultRequestHeaders.Add(
                ApiKeyAuthenticationDefaults.HeaderName,
                apiKey);

        var response = await client.PostAsJsonAsync(
            "/rac/execute",
            new
            {
                arguments = new[]
                {
                    "--version"
                }
            });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);

        Assert.Contains(
            response.Headers.WwwAuthenticate,
            challenge => string.Equals(
                challenge.Scheme,
                ApiKeyAuthenticationDefaults.Scheme,
                StringComparison.Ordinal));
        Assert.True(
            response.Headers.Contains(ApiTrace.HeaderName));

        using var document =
            JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());

        var root = document.RootElement;
        var error = root.GetProperty("error");

        Assert.False(
            root.GetProperty("success").GetBoolean());

        Assert.False(
            root.TryGetProperty("data", out _));

        Assert.Equal(
            "unauthorized",
            error.GetProperty("code").GetString());

        Assert.Equal(
            "Unauthorized",
            error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Execute_Version_ReturnsExecutionResult()
    {
        await using var factory =
            new RasGateWebApplicationFactory(
                GetFakeRacPath());

        using var client =
            factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/rac/execute",
            new
            {
                arguments = new[]
                {
                    "--version"
                }
            });

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var envelope = await response.Content
            .ReadFromJsonAsync<ApiResponse<ExecuteRacResponse>>();

        Assert.NotNull(envelope);
        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        Assert.Equal(0, envelope.Data.ExitCode);
        Assert.Equal(
            RacExecutionOutcome.Succeeded,
            envelope.Data.Outcome);
        Assert.False(envelope.Data.TimedOut);
        Assert.Contains(
            "Remote Administrative Client",
            envelope.Data.StandardOutput);
        Assert.Equal("", envelope.Data.StandardError);
    }

    [Fact]
    public async Task Execute_NonZeroExit_ReturnsFailedOutcome()
    {
        await using var factory = new RasGateWebApplicationFactory(
            GetFakeRacPath());
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            "/rac/execute",
            new
            {
                arguments = new[] { "__test", "exit", "17" }
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");

        Assert.Equal("failed", data.GetProperty("outcome").GetString());
        Assert.Equal(17, data.GetProperty("exitCode").GetInt32());
        Assert.False(data.GetProperty("timedOut").GetBoolean());
    }

    [Fact]
    public async Task Execute_Timeout_ReturnsUnknownOutcome()
    {
        await using var factory = new RasGateWebApplicationFactory(
            GetFakeRacPath(),
            1);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            "/rac/execute",
            new
            {
                arguments = new[] { "__test", "delay", "10000" }
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");

        Assert.Equal("unknown", data.GetProperty("outcome").GetString());
        Assert.Equal(-1, data.GetProperty("exitCode").GetInt32());
        Assert.True(data.GetProperty("timedOut").GetBoolean());
    }

    [Fact]
    public async Task Execute_MissingRac_ReturnsServiceUnavailable()
    {
        await using var factory =
            new RasGateWebApplicationFactory(
                GetMissingExecutablePath());

        using var client =
            factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/rac/execute",
            new
            {
                arguments = new[]
                {
                    "--version"
                }
            });

        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);

        using var document =
            JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());

        var root = document.RootElement;
        var error = root.GetProperty("error");

        Assert.False(
            root.GetProperty("success").GetBoolean());

        Assert.False(
            root.TryGetProperty("data", out _));

        Assert.Equal(
            "rac_unavailable",
            error.GetProperty("code").GetString());

        Assert.Equal(
            "RAC executable could not be started.",
            error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Execute_OutputExceedsLimit_ReturnsBadGateway()
    {
        const int maxOutputBytes = 1024;

        await using var factory =
            new RasGateWebApplicationFactory(
                GetFakeRacPath(),
                maxOutputBytes: maxOutputBytes);

        using var client =
            factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/rac/execute",
            new
            {
                arguments = new[]
                {
                    "__test",
                    "large-output",
                    "1048576"
                }
            });

        Assert.Equal(
            HttpStatusCode.BadGateway,
            response.StatusCode);

        using var document =
            JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());

        var root = document.RootElement;
        var error = root.GetProperty("error");

        Assert.False(
            root.GetProperty("success").GetBoolean());

        Assert.False(
            root.TryGetProperty("data", out _));

        Assert.Equal(
            "rac_output_limit_exceeded",
            error.GetProperty("code").GetString());

        Assert.Equal(
            $"RAC output exceeded the configured limit of " +
            $"{maxOutputBytes} bytes. The external command outcome " +
            $"is unknown; automatic retry is unsafe.",
            error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Execute_EmptyArguments_ReturnsBadRequest()
    {
        await using var factory =
            new RasGateWebApplicationFactory(
                GetFakeRacPath());

        using var client =
            factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/rac/execute",
            new
            {
                arguments = Array.Empty<string>()
            });

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);

        using var document =
            JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());

        var root = document.RootElement;
        var error = root.GetProperty("error");

        Assert.False(
            root.GetProperty("success").GetBoolean());

        Assert.False(
            root.TryGetProperty("data", out _));

        Assert.Equal(
            "bad_request",
            error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Execute_NullArgument_ReturnsBadRequest()
    {
        await using var factory =
            new RasGateWebApplicationFactory(
                GetFakeRacPath());

        using var client =
            factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            "/rac/execute",
            new
            {
                arguments = new[]
                {
                    "__test",
                    null
                }
            });

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        Assert.Equal(
            "bad_request",
            document.RootElement
                .GetProperty("error")
                .GetProperty("code")
                .GetString());
    }

    [Fact]
    public async Task Execute_OversizedArgument_ReturnsBadRequestAndKeepsRacAvailable()
    {
        await using var factory = new RasGateWebApplicationFactory(
            GetFakeRacPath());
        using var client = factory.CreateAuthenticatedClient();

        using var rejected = await client.PostAsJsonAsync(
            "/rac/execute",
            new
            {
                arguments = new[]
                {
                    "__test",
                    "stdout",
                    new string('x', 8193)
                }
            });

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        using var accepted = await client.PostAsJsonAsync(
            "/rac/execute",
            new { arguments = new[] { "--version" } });

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Fact]
    public async Task Execute_AllSlotsOccupied_ReturnsTooManyRequests()
    {
        await using var factory =
            new RasGateWebApplicationFactory(
                GetFakeRacPath(),
                5,
                1);

        using var client =
            factory.CreateAuthenticatedClient();

        var runningRequest = client.PostAsJsonAsync(
            "/rac/execute",
            new
            {
                arguments = new[]
                {
                    "__test",
                    "delay",
                    "1500"
                }
            });

        await Task.Delay(200);

        var response = await client.PostAsJsonAsync(
            "/rac/execute",
            new
            {
                arguments = new[]
                {
                    "--version"
                }
            });

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            response.StatusCode);

        using var document =
            JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());

        var root = document.RootElement;
        var error = root.GetProperty("error");

        Assert.False(
            root.GetProperty("success").GetBoolean());

        Assert.False(
            root.TryGetProperty("data", out _));

        Assert.Equal(
            "rac_capacity_exceeded",
            error.GetProperty("code").GetString());

        Assert.Equal(
            "All RAC execution slots are currently occupied.",
            error.GetProperty("message").GetString());

        var runningResponse = await runningRequest;

        Assert.Equal(
            HttpStatusCode.OK,
            runningResponse.StatusCode);
    }

    private static string GetFakeRacPath()
    {
        var fileName = OperatingSystem.IsWindows()
            ? "RasGate.FakeRac.exe"
            : "RasGate.FakeRac";

        var configuration = new DirectoryInfo(
                AppContext.BaseDirectory)
            .Parent?
            .Name;

        if (string.IsNullOrWhiteSpace(configuration))
            throw new InvalidOperationException(
                $"Could not determine build configuration from " +
                $"'{AppContext.BaseDirectory}'.");

        var path = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "RasGate.FakeRac",
                "bin",
                configuration,
                "net10.0",
                fileName));

        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Fake RAC executable was not found: {path}",
                path);

        return path;
    }

    private static string GetMissingExecutablePath()
    {
        return Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString(),
            "missing-rac");
    }
}