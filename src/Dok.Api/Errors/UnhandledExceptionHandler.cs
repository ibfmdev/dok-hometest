using Dok.Api.Dtos;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace Dok.Api.Errors;

public sealed class UnhandledExceptionHandler(
    ILogger<UnhandledExceptionHandler> logger,
    IOptions<JsonOptions> jsonOptions) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception");
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "application/json";
        await System.Text.Json.JsonSerializer.SerializeAsync(
            httpContext.Response.Body,
            new ErrorPayload("internal_error"),
            jsonOptions.Value.SerializerOptions,
            CancellationToken.None);
        return true;
    }
}
