using Dok.Api.Logging;
using Serilog;

namespace Dok.Api.Extensions;

internal static class SerilogExtensions
{
    extension(WebApplicationBuilder builder)
    {
        /// <summary>Configura Serilog como provider de logging com mascaramento de placa para LGPD (ADR-010).</summary>
        public WebApplicationBuilder AddDokSerilog()
        {
            builder.Host.UseSerilog((ctx, services, lc) => lc
                .ReadFrom.Configuration(ctx.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Destructure.With<PlateDestructuringPolicy>());

            return builder;
        }
    }
}
