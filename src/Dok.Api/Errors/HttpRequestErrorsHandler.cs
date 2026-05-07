using Dok.Api.Dtos;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using BadHttpRequestException = Microsoft.AspNetCore.Http.BadHttpRequestException;

namespace Dok.Api.Errors;

public sealed class HttpRequestErrorsHandler(
    ILogger<HttpRequestErrorsHandler> logger,
    IOptions<JsonOptions> jsonOptions) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        switch (exception)
        {
            case BadHttpRequestException bad when IsPayloadTooLarge(bad):
                logger.LogWarning("Request body exceeded the configured limit");
                await Write(httpContext, StatusCodes.Status413PayloadTooLarge,
                    new ErrorPayload("payload_too_large"), cancellationToken);
                return true;

            case BadHttpRequestException bad:
                logger.LogWarning(bad, "Bad HTTP request");
                await Write(httpContext, StatusCodes.Status400BadRequest,
                    new ErrorPayload("invalid_request"), cancellationToken);
                return true;

            case System.Text.Json.JsonException jx:
                logger.LogWarning(jx, "JSON parsing failed");
                await Write(httpContext, StatusCodes.Status400BadRequest,
                    new ErrorPayload("invalid_request"), cancellationToken);
                return true;

            default:
                return false;
        }
    }

    private static bool IsPayloadTooLarge(BadHttpRequestException ex)
        => ex.StatusCode == StatusCodes.Status413PayloadTooLarge ||
           ex.Message.Contains("Request body too large", StringComparison.OrdinalIgnoreCase);

    private async Task Write<T>(HttpContext ctx, int status, T payload, CancellationToken _)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        await System.Text.Json.JsonSerializer.SerializeAsync(
            ctx.Response.Body, payload, jsonOptions.Value.SerializerOptions, CancellationToken.None);
    }
}
