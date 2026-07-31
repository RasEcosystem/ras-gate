using System.Net;
using System.Text.Json;

namespace RasGate.IntegrationTests.Api;

public sealed class RasGateControllerTests
{
    [Fact]
    public async Task GetStatus_ReturnsInstanceNameAndVersion()
    {
        const string instanceName = "RasGate Integration Test";

        await using var factory =
            new RasGateWebApplicationFactory(
                "unused-rac",
                instanceName: instanceName);

        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/rasgate/status");

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

        Assert.Equal(
            instanceName,
            data.GetProperty("instanceName").GetString());

        var version =
            data.GetProperty("version").GetString();

        Assert.False(
            string.IsNullOrWhiteSpace(version));

        Assert.Matches(
            @"^\d+\.\d+\.\d+",
            version);

        Assert.False(
            data.TryGetProperty("apiKey", out _));
    }
}