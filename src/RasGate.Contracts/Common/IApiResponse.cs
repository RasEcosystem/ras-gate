using System.Net;

namespace RasGate.Contracts.Common;

public interface IApiResponse
{
    bool Success { get; }

    public HttpStatusCode
        StatusCode { get; }
}