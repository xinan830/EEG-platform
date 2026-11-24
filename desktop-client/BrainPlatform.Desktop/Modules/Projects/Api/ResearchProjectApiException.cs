using System.Net;

namespace BrainPlatform.Desktop.Modules.Projects.Api;

public sealed class ResearchProjectApiException(HttpStatusCode statusCode, string code, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
