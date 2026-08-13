using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using RasGate.Core.Common;

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

        if (objectResult.Value is not IApiResponse)
        {
            var statusCode = objectResult.StatusCode
                             ?? context.HttpContext.Response.StatusCode;

            objectResult.Value = statusCode >=
                                 StatusCodes.Status400BadRequest
                ? ApiResponse<object>.FailWithDefaultError(
                    (HttpStatusCode)statusCode)
                : ApiResponse<object>.Ok(
                    objectResult.Value);

            objectResult.StatusCode ??= statusCode;
        }

        await next();
    }
}