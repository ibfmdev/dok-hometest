using System.Text.Json;
using System.Text.Json.Serialization;
using Dok.Api.Json;

namespace Dok.Api.Extensions;

internal static class JsonExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registra Controllers + converters (Plate, Money) + <c>UnmappedMemberHandling.Disallow</c>
        /// tanto na pipeline MVC quanto nas respostas geradas via <see cref="System.Text.Json"/> direto
        /// (usado pelos <c>IExceptionHandler</c>) — ADR-006/011/015.
        /// </summary>
        public IServiceCollection AddDokJson()
        {
            services.AddControllers()
                    .AddJsonOptions(o => Apply(o.JsonSerializerOptions));

            services.ConfigureHttpJsonOptions(o => Apply(o.SerializerOptions));

            return services;
        }
    }

    private static void Apply(JsonSerializerOptions opts)
    {
        opts.Converters.Add(new PlateJsonConverter());
        opts.Converters.Add(new MoneyJsonConverter());
        opts.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        opts.PropertyNameCaseInsensitive = false;
    }
}
