using Dok.Api.Dtos;
using Dok.Api.Errors;
using Microsoft.AspNetCore.Mvc;

namespace Dok.Api.Extensions;

internal static class ErrorHandlingExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registra a chain de <see cref="Microsoft.AspNetCore.Diagnostics.IExceptionHandler"/> (ADR-014):
        /// HTTP errors → Domain → Unhandled. Também sobrescreve a resposta automática 400 do
        /// <c>[ApiController]</c> pelo payload literal exigido pela spec.
        /// </summary>
        public IServiceCollection AddDokErrorHandling()
        {
            // Ordem importa: handler de borda HTTP → Domain → fallback genérico.
            services.AddExceptionHandler<HttpRequestErrorsHandler>();
            services.AddExceptionHandler<DomainExceptionHandler>();
            services.AddExceptionHandler<UnhandledExceptionHandler>();

            // Necessário para UseExceptionHandler() sem args ter um fallback registrado.
            services.AddProblemDetails();

            // Substitui a resposta automática 400 ProblemDetails do [ApiController] (ex: campo desconhecido)
            // pelo payload literal {"error":"invalid_request"} exigido pela spec.
            services.Configure<ApiBehaviorOptions>(o =>
            {
                o.InvalidModelStateResponseFactory = _ =>
                    new BadRequestObjectResult(new ErrorPayload("invalid_request"));
            });

            return services;
        }
    }
}
