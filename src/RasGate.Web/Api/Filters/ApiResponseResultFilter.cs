using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using RasGate.Contracts.Common;

namespace RasGate.Web.Api.Filters;

public sealed class ApiResponseResultFilter
    : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(
        ResultExecutingContext context,
        ResultExecutionDelegate next)
    {
        if (context.Result
            is not ObjectResult objectResult)
        {
            await next();
            return;
        }

        if (objectResult.Value is null)
        {
            await next();
            return;
        }

        var traceId =
            ApiTrace.GetTraceId(
                context.HttpContext);

        context.HttpContext.Response.Headers[ApiTrace.HeaderName] = traceId;

        if (objectResult.Value is IApiResponse response)
        {
            objectResult.StatusCode = (int)response.StatusCode;
        }
        else
        {
            objectResult.Value =
                ApiResponse<object>
                    .Ok(
                        objectResult.Value);

            objectResult.StatusCode
                ??=
                StatusCodes
                    .Status200OK;
        }

        await next();
    }
}