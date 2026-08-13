using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RasGate.Core.Common;
using RasGate.Core.Rac;
using RasGate.Core.Rac.Exceptions;
using RasGate.Web.Api.OpenApi;
using RasGate.Web.Observability;

namespace RasGate.Web.Controllers;

[ApiController]
[Route("[controller]")]
public class RacController(
    IRacExecutor racExecutor,
    ILogger<RacController> logger) : ControllerBase
{
    [HttpGet("status")]
    [ProducesResponseType(
        typeof(ApiResponse<RacStatusResponse>),
        StatusCodes.Status200OK)]
    public async Task<ApiResponse<RacStatusResponse>> GetStatus(
        CancellationToken cancellationToken)
    {
        var status = await racExecutor.GetStatusAsync(cancellationToken);

        return ApiResponse<RacStatusResponse>.Ok(
            new RacStatusResponse
            {
                Available = status.Available,
                Version = status.Version,
                Message = status.Message
            });
    }

    [Authorize]
    [HttpPost("execute")]
    [ProducesResponseType(
        typeof(ApiResponse<ExecuteRacResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status502BadGateway)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status503ServiceUnavailable)]
    public async Task<ApiResponse<ExecuteRacResponse>> Execute(
        [FromBody] ExecuteRacRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = RacCommandLogContext.FromArguments(request.Arguments);

        using var logScope = logger.BeginScope(
            new Dictionary<string, object?>
            {
                ["Phase"] = "RAC",
                ["RacCommand"] = command.Command,
                ["RacSubcommand"] = command.Subcommand,
                ["RacTarget"] = command.Target,
                ["RacArgumentCount"] = command.ArgumentCount,
                ["RacClientId"] = User.FindFirstValue(
                    ClaimTypes.NameIdentifier) ?? "<unknown>"
            });

        logger.LogInformation("Starting RAC command.");

        try
        {
            var result = await racExecutor.ExecuteAsync(
                request.Arguments,
                cancellationToken);

            var outcome = result.TimedOut
                ? RacExecutionOutcome.Unknown
                : result.ExitCode == 0
                    ? RacExecutionOutcome.Succeeded
                    : RacExecutionOutcome.Failed;

            logger.LogInformation(
                "RAC command completed with outcome {RacOutcome}, " +
                "exit code {RacExitCode}, timeout {RacTimedOut}, " +
                "duration {RacDurationMilliseconds} ms, stdout length " +
                "{RacStandardOutputLength}, stderr length " +
                "{RacStandardErrorLength}.",
                outcome,
                result.ExitCode,
                result.TimedOut,
                result.DurationMilliseconds,
                result.StandardOutput.Length,
                result.StandardError.Length);

            return ApiResponse<ExecuteRacResponse>.Ok(
                new ExecuteRacResponse
                {
                    Outcome = outcome,
                    ExitCode = result.ExitCode,
                    StandardOutput = result.StandardOutput,
                    StandardError = result.StandardError,
                    DurationMilliseconds =
                        result.DurationMilliseconds,
                    TimedOut = result.TimedOut
                });
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation(
                "RAC command was cancelled by the caller; external " +
                "outcome is unknown.");

            throw;
        }
        catch (Exception exception)
        {
            var outcome = exception is RacCapacityExceededException or
                RacArgumentValidationException or RacUnavailableException
                ? RacExecutionOutcome.Failed
                : RacExecutionOutcome.Unknown;

            logger.LogWarning(
                "RAC command failed with {RacExceptionType}; external " +
                "outcome is {RacOutcome}.",
                exception.GetType().Name,
                outcome);

            throw;
        }
    }
}