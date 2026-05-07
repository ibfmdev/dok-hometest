namespace Dok.Api.Extensions;

internal static class KestrelExtensions
{
    private const long DefaultMaxBodyBytes = 1L * 1024 * 1024; // 1 MiB (ADR-015)

    extension(WebApplicationBuilder builder)
    {
        /// <summary>Configura limites do Kestrel a partir de <c>RequestLimits:*</c> (ADR-015 + ADR-017).</summary>
        public WebApplicationBuilder ConfigureDokKestrel()
        {
            var maxBodyBytes = builder.Configuration.GetValue<long?>("RequestLimits:MaxBodyBytes")
                               ?? DefaultMaxBodyBytes;

            builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = maxBodyBytes);

            return builder;
        }
    }
}
