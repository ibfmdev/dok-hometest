using System.Globalization;
using System.Xml.Linq;
using Dok.Infrastructure.Abstractions;
using Microsoft.Extensions.Logging;

namespace Dok.Infrastructure.Providers;

public sealed class ProviderBXmlAdapter(
    HttpClient http,
    ILogger<ProviderBXmlAdapter> logger) : IDebtProvider
{
    public string Name => "ProviderB";

    public async Task<IReadOnlyList<Debt>> FetchAsync(Plate plate, CancellationToken ct)
    {
        logger.LogDebug("Calling ProviderB for {@Plate}", plate);
        using var response = await http.GetAsync($"/debts/{plate.Value}", ct);
        response.EnsureSuccessStatusCode();
        EnsureXmlContentType(response);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var doc = await XDocument.LoadAsync(stream, LoadOptions.None, ct);

        var debtsElement = doc.Root?.Element("debts");
        if (debtsElement is null || !debtsElement.HasElements)
            return Array.Empty<Debt>(); // cobre <debts/> autofechado

        var result = new List<Debt>();
        foreach (var debt in debtsElement.Elements("debt"))
        {
            var category = debt.Element("category")?.Value ?? string.Empty;
            var valueText = debt.Element("value")?.Value ?? "0";
            var expirationText = debt.Element("expiration")?.Value
                ?? throw new InvalidOperationException("Provider B debt missing expiration");

            var type = DebtTypeMapper.Parse(category);
            var amount = Money.Of(decimal.Parse(valueText, CultureInfo.InvariantCulture));
            var due = DateOnly.Parse(expirationText);
            result.Add(new Debt(type, amount, due));
        }
        return result;
    }

    /// <summary>
    /// Provider precisa anunciar XML via Content-Type. Aceita <c>application/xml</c>,
    /// <c>text/xml</c> e variantes <c>application/*+xml</c>. Outros valores indicam degradação
    /// — lançamos <see cref="HttpRequestException"/> para que o <see cref="DebtProviderChain"/>
    /// dispare fallback.
    /// </summary>
    private static void EnsureXmlContentType(HttpResponseMessage response)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is null
            || (!mediaType.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
                && !mediaType.Equals("text/xml", StringComparison.OrdinalIgnoreCase)
                && !(mediaType.StartsWith("application/", StringComparison.OrdinalIgnoreCase)
                     && mediaType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase))))
        {
            throw new HttpRequestException(
                $"ProviderB returned unexpected Content-Type '{mediaType ?? "<none>"}', expected application/xml");
        }
    }
}
