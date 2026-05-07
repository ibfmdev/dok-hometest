using System.Text.Json.Serialization;

namespace Dok.Api.Dtos;

public sealed record DebtsResponseDto(
    [property: JsonPropertyName("placa")] Plate Placa,
    [property: JsonPropertyName("debitos")] IReadOnlyList<DebtItemDto> Debitos,
    [property: JsonPropertyName("resumo")] SummaryDto Resumo,
    [property: JsonPropertyName("pagamentos")] PaymentsDto Pagamentos);

public sealed record DebtItemDto(
    [property: JsonPropertyName("tipo")] string Tipo,
    [property: JsonPropertyName("valor_original")] Money ValorOriginal,
    [property: JsonPropertyName("valor_atualizado")] Money ValorAtualizado,
    [property: JsonPropertyName("vencimento")] string Vencimento,
    [property: JsonPropertyName("dias_atraso")] int DiasAtraso);

public sealed record SummaryDto(
    [property: JsonPropertyName("total_original")] Money TotalOriginal,
    [property: JsonPropertyName("total_atualizado")] Money TotalAtualizado);

public sealed record PaymentsDto(
    [property: JsonPropertyName("opcoes")] IReadOnlyList<PaymentOptionDto> Opcoes);

public sealed record PaymentOptionDto(
    [property: JsonPropertyName("tipo")] string Tipo,
    [property: JsonPropertyName("valor_base")] Money ValorBase,
    [property: JsonPropertyName("pix")] PixDto Pix,
    [property: JsonPropertyName("cartao_credito")] CreditCardDto CartaoCredito);

public sealed record PixDto(
    [property: JsonPropertyName("total_com_desconto")] Money TotalComDesconto);

public sealed record CreditCardDto(
    [property: JsonPropertyName("parcelas")] IReadOnlyList<InstallmentDto> Parcelas);

public sealed record InstallmentDto(
    [property: JsonPropertyName("quantidade")] int Quantidade,
    [property: JsonPropertyName("valor_parcela")] Money ValorParcela);
