using Dok.Api.OpenApi;
using Scalar.AspNetCore;

namespace Dok.Api.Extensions;

internal static class OpenApiExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registra o gerador OpenAPI nativo do .NET 9+ com schema transformers que descrevem
        /// os Value Objects (<c>Plate</c>, <c>Money</c>) como string com pattern e example (ADR-016).
        /// </summary>
        public IServiceCollection AddDokOpenApi()
        {
            services.AddOpenApi(o => o.AddSchemaTransformer(SchemaTransformers.TransformDomainTypes));
            return services;
        }
    }

    extension(WebApplication app)
    {
        /// <summary>
        /// Expõe a spec OpenAPI em <c>/openapi/{documentName}.json</c> e o Scalar UI em <c>/scalar</c>.
        /// </summary>
        public WebApplication MapDokOpenApi()
        {
            app.MapOpenApi("/openapi/{documentName}.json");
            app.MapScalarApiReference("/scalar");
            return app;
        }
    }
}
