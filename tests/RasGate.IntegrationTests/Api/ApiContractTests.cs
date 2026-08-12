using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RasGate.Core.Common;
using RasGate.Core.RasGate;
using RasGate.Web.Api;

namespace RasGate.IntegrationTests.Api;

public sealed class ApiContractTests
{
    [Fact]
    public async Task OpenApi_ResponseSchema_MatchesOptionalEnvelopeFields()
    {
        await using var factory =
            new RasGateWebApplicationFactory(
                "unused-rac",
                environment: "Development");

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");

        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());

        var required = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("ApiResponseOfRasGateStatusResponse")
            .GetProperty("required")
            .EnumerateArray()
            .Select(item => item.GetString()!)
            .ToArray();

        Assert.Collection(
            required,
            item => Assert.Equal("success", item));
    }

    [Theory]
    [InlineData("GET", "/missing-resource", HttpStatusCode.NotFound,
        "not_found")]
    [InlineData("POST", "/rasgate/status", HttpStatusCode.MethodNotAllowed,
        "method_not_allowed")]
    public async Task InfrastructureErrors_ReturnEnvelopeAndTraceId(
        string method,
        string path,
        HttpStatusCode expectedStatus,
        string expectedErrorCode)
    {
        await using var factory =
            new RasGateWebApplicationFactory("unused-rac");

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            new HttpMethod(method),
            path);
        using var response = await client.SendAsync(request);

        Assert.Equal(expectedStatus, response.StatusCode);
        AssertTraceId(response);

        var envelope = await response.Content
            .ReadFromJsonAsync<ApiResponse<object>>();

        Assert.NotNull(envelope);
        Assert.False(envelope.Success);
        Assert.Equal(expectedErrorCode, envelope.Error?.Code);
        Assert.Null(envelope.Data);
    }

    [Fact]
    public async Task Execute_UnsupportedMediaType_ReturnsEnvelopeAndTraceId()
    {
        await using var factory =
            new RasGateWebApplicationFactory("unused-rac");

        using var client = factory.CreateAuthenticatedClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/rac/execute")
        {
            Content = new StringContent(
                "{}",
                Encoding.UTF8,
                "text/plain")
        };

        using var response = await client.SendAsync(request);

        Assert.Equal(
            HttpStatusCode.UnsupportedMediaType,
            response.StatusCode);
        AssertTraceId(response);

        var envelope = await response.Content
            .ReadFromJsonAsync<ApiResponse<object>>();

        Assert.NotNull(envelope);
        Assert.False(envelope.Success);
        Assert.Equal(
            "unsupported_media_type",
            envelope.Error?.Code);
    }

    [Fact]
    public async Task Get_WithNonJsonContentType_IsNotRejected()
    {
        await using var factory =
            new RasGateWebApplicationFactory("unused-rac");

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/rasgate/status")
        {
            Content = new StringContent(
                string.Empty,
                Encoding.UTF8,
                "text/plain")
        };

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertTraceId(response);

        var envelope = await response.Content
            .ReadFromJsonAsync<ApiResponse<RasGateStatusResponse>>();

        Assert.NotNull(envelope);
        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        Assert.False(
            string.IsNullOrWhiteSpace(envelope.Data.InstanceName));
    }

    private static void AssertTraceId(
        HttpResponseMessage response)
    {
        Assert.True(
            response.Headers.TryGetValues(
                ApiTrace.HeaderName,
                out var traceIds));

        Assert.Single(traceIds);
        Assert.False(string.IsNullOrWhiteSpace(traceIds.Single()));
    }
}