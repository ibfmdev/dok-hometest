using System.ComponentModel.DataAnnotations;

namespace Dok.Infrastructure.Options;

public sealed class ProvidersOptions
{
    public const string SectionName = "Providers";

    [Required, Url]
    public string ProviderAUrl { get; init; } = string.Empty;

    [Required, Url]
    public string ProviderBUrl { get; init; } = string.Empty;
}
