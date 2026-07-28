using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using RasGate.Contracts.Common;
using RasGate.Infrastructure.RasGate;
using RasGate.Web.Api;

namespace RasGate.Web.Authentication;

public sealed class ApiKeyAuthenticationHandler
    : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IOptionsMonitor<RasGateOptions> _rasGateOptions;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptionsMonitor<RasGateOptions> rasGateOptions)
        : base(options, logger, encoder)
    {
        _rasGateOptions = rasGateOptions;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(
                ApiKeyAuthenticationDefaults.HeaderName,
                out var headerValues))
            return Task.FromResult(
                AuthenticateResult.NoResult());

        if (headerValues.Count != 1)
            return Task.FromResult(
                AuthenticateResult.Fail(
                    "A single API key must be provided."));

        var providedApiKey = headerValues[0];
        var expectedApiKey = _rasGateOptions.CurrentValue.ApiKey;

        if (string.IsNullOrEmpty(providedApiKey) ||
            string.IsNullOrEmpty(expectedApiKey) ||
            !ApiKeysEqual(expectedApiKey, providedApiKey))
            return Task.FromResult(
                AuthenticateResult.Fail(
                    "The provided API key is invalid."));

        var identity = new ClaimsIdentity(
            [
                new Claim(
                    ClaimTypes.NameIdentifier,
                    ApiKeyAuthenticationDefaults.Scheme)
            ],
            Scheme.Name);

        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(
            AuthenticateResult.Success(ticket));
    }

    protected override async Task HandleChallengeAsync(
        AuthenticationProperties properties)
    {
        Response.StatusCode =
            StatusCodes.Status401Unauthorized;

        var traceId = ApiTrace.GetTraceId(Context);

        Response.Headers[ApiTrace.HeaderName] =
            traceId;

        var response = ApiResponse<object>.Fail(
            HttpStatusCode.Unauthorized);

        await Response.WriteAsJsonAsync(
            response,
            ApiJson.Default,
            Context.RequestAborted);
    }

    private static bool ApiKeysEqual(
        string expectedApiKey,
        string providedApiKey)
    {
        var expectedHash = SHA256.HashData(
            Encoding.UTF8.GetBytes(expectedApiKey));

        var providedHash = SHA256.HashData(
            Encoding.UTF8.GetBytes(providedApiKey));

        return CryptographicOperations.FixedTimeEquals(
            expectedHash,
            providedHash);
    }
}