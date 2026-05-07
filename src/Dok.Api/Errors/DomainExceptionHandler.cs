using Dok.Api.Dtos;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace Dok.Api.Errors;

public sealed class DomainExceptionHandler(
    ILogger<DomainExceptionHandler> logger,
    IOptions<JsonOptions> jsonOptions) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        switch (exception)
        {
            case InvalidPlateException invalid:
                // Nunca logar a placa raw — viola LGPD. Logamos só o tamanho, o que basta para diagnose.
                logger.LogWarning("Invalid plate format received (length={Length})",
                    invalid.Raw?.Length ?? 0);
                await Write(httpContext, StatusCodes.Status400BadRequest,
                    new ErrorPayload("invalid_plate"), cancellationToken);
                return true;

            case UnknownDebtTypeException unknown:
                logger.LogWarning("Unknown debt type encountered: {Type}", unknown.Type);
                await Write(httpContext, StatusCodes.Status422UnprocessableEntity,
                    new UnknownDebtTypeErrorPayload("unknown_debt_type", unknown.Type), cancellationToken);
                return true;

            case AllProvidersUnavailableException allDown:
                logger.LogError(allDown, "All providers are unavailable");
                await Write(httpContext, StatusCodes.Status503ServiceUnavailable,
                    new ErrorPayload("all_providers_unavailable"), cancellationToken);
                return true;

            default:
                return false;
        }
    }

    private async Task Write<T>(HttpContext ctx, int status, T payload, CancellationToken _)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        // Usar CancellationToken.None: o token original já pode estar cancelado
        // (Polly timeout, etc.). Queremos garantir que a resposta de erro saia.
        await System.Text.Json.JsonSerializer.SerializeAsync(
            ctx.Response.Body, payload, jsonOptions.Value.SerializerOptions, CancellationToken.None);
    }
}
