using Dok.Api.Dtos;

namespace Dok.Api;

public static class Mapping
{
    public static DebtsResponseDto ToDto(this DebtsResult result) => new(
        result.Plate,
        result.Debts.Select(d => new DebtItemDto(
            DebtTypeMapper.ToWire(d.Type),
            d.OriginalAmount,
            d.UpdatedAmount,
            d.DueDate.ToString("yyyy-MM-dd"),
            d.DaysOverdue)).ToArray(),
        new SummaryDto(result.Summary.TotalOriginal, result.Summary.TotalUpdated),
        new PaymentsDto(result.Options.Select(o => new PaymentOptionDto(
            o.Type,
            o.Base,
            new PixDto(o.Pix.TotalWithDiscount),
            new CreditCardDto(o.CreditCard.Installments
                .Select(p => new InstallmentDto(p.Quantity, p.Amount)).ToArray()))).ToArray()));
}
