using Microsoft.AspNetCore.Mvc;
using RasGate.Application.Rac.Interfaces;
using RasGate.Contracts.Common;
using RasGate.Contracts.Rac;
using RasGate.Web.Api.OpenApi;

namespace RasGate.Web.Controllers;

[ApiController]
[Route("[controller]")]
public class RacController(IRacExecutor racExecutor) : ControllerBase
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

    [HttpPost("execute")]
    [ProducesResponseType(
        typeof(ApiResponse<ExecuteRacResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(OpenApiErrorResponse),
        StatusCodes.Status400BadRequest)]
    public async Task<ApiResponse<ExecuteRacResponse>> Execute(
        [FromBody] ExecuteRacRequest request,
        CancellationToken cancellationToken)
    {
        var result = await racExecutor.ExecuteAsync(
            request.Arguments,
            cancellationToken);

        return ApiResponse<ExecuteRacResponse>.Ok(
            new ExecuteRacResponse
            {
                ExitCode = result.ExitCode,
                StandardOutput = result.StandardOutput,
                StandardError = result.StandardError,
                DurationMilliseconds =
                    result.DurationMilliseconds,
                TimedOut = result.TimedOut
            });
    }
}