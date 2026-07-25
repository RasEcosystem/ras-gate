using System.Diagnostics;

namespace RasGate.Web.Api;

public static class ApiTrace
{
    public const string HeaderName = "X-Trace-Id";

    public static string GetTraceId(
        HttpContext context)
    {
        return Activity.Current?.Id
               ?? context.TraceIdentifier;
    }
}