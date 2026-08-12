using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RasGate.Core.Common;
using RasGate.Core.RasGate;
using RasGate.Infrastructure.RasGate;

namespace RasGate.Web.Controllers;

[ApiController]
[Route("[controller]")]
public sealed class RasGateController(IOptions<RasGateOptions> rasGateOptions) : ControllerBase
{
    [HttpGet("status")]
    [ProducesResponseType(
        typeof(ApiResponse<RasGateStatusResponse>),
        StatusCodes.Status200OK)]
    public ApiResponse<RasGateStatusResponse> GetStatus()
    {
        return ApiResponse<RasGateStatusResponse>.Ok(
            new RasGateStatusResponse
            {
                InstanceName = rasGateOptions.Value.InstanceName,
                Version = ThisAssembly.AssemblyInformationalVersion
            });
    }
}