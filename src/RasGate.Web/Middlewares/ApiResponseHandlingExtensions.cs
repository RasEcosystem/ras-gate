using System.Net;
using RasGate.Core.Common;
using RasGate.Web.Api;

namespace RasGate.Web.Middlewares;

public static class ApiResponseHandlingExtensions
{
    public static void UseApiTraceHeader(
        this IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(
                static state =>
                {
                    var httpContext = (HttpContext)state;

                    httpContext.Response.Headers[ApiTrace.HeaderName] =
                        ApiTrace.GetTraceId(httpContext);

                    return Task.CompletedTask;
                },
                context);

            await next(context);
        });
    }

    public static void UseApiStatusCodeResponses(
        this IApplicationBuilder app)
    {
        app.UseStatusCodePages(async statusCodeContext =>
        {
            var context = statusCodeContext.HttpContext;
            var statusCode = (HttpStatusCode)context.Response.StatusCode;
            var response = ApiResponse<object>.FailWithDefaultError(
                statusCode);

            await context.Response.WriteAsJsonAsync(
                response,
                ApiJson.Default,
                context.RequestAborted);
        });
    }
}