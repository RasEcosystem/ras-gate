using System.Text;

namespace RasGate.Web.Middlewares;

public static class RequestBodyLoggingExtensions
{
    public const string RequestBodyItemKey =
        "RasGate.Logging.RequestBody";

    public const string RequestBodyTruncatedItemKey =
        "RasGate.Logging.RequestBodyTruncated";

    public static void UseRequestBodyLogging(
        this IApplicationBuilder app,
        bool includeRequestBody,
        int maxRequestBodyBytes)
    {
        if (!includeRequestBody)
            return;

        app.Use(async (context, next) =>
        {
            if (CanReadBody(context.Request))
                await StoreRequestBodyAsync(
                    context,
                    Math.Max(1, maxRequestBodyBytes));

            await next();
        });
    }

    private static bool CanReadBody(HttpRequest request)
    {
        if (request.ContentLength is null or <= 0)
            return false;

        if (request.ContentType is null)
            return false;

        return request.ContentType.StartsWith(
                   "application/json",
                   StringComparison.OrdinalIgnoreCase)
               ||
               request.ContentType.StartsWith(
                   "text/plain",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static async Task StoreRequestBodyAsync(
        HttpContext context,
        int maxRequestBodyBytes)
    {
        var request = context.Request;

        request.EnableBuffering();

        var buffer =
            new byte[maxRequestBodyBytes + 1];

        var totalRead = 0;

        while (totalRead < buffer.Length)
        {
            var read = await request.Body.ReadAsync(
                buffer.AsMemory(totalRead));

            if (read == 0)
                break;

            totalRead += read;
        }

        request.Body.Position = 0;

        var truncated =
            totalRead > maxRequestBodyBytes;

        var bodyBytes =
            truncated
                ? buffer.AsSpan(0, maxRequestBodyBytes)
                : buffer.AsSpan(0, totalRead);

        context.Items[RequestBodyItemKey] =
            Encoding.UTF8.GetString(bodyBytes);

        if (truncated)
            context.Items[RequestBodyTruncatedItemKey] = true;
    }
}