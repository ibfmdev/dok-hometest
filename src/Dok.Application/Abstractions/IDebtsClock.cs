namespace Dok.Application.Abstractions;

/// <summary>
/// Relógio de domínio para cálculo de "hoje" no <c>DebtsCalculator</c>. Separado
/// do <see cref="TimeProvider"/> global porque o Polly v8 também usa o TimeProvider
/// injetado no DI para sua sliding window do circuit breaker — congelar o relógio
/// global (para fixar a data da spec na demo) também congelaria a janela do CB,
/// quebrando a semântica de "X falhas em Y segundos".
/// </summary>
public interface IDebtsClock
{
    DateOnly Today { get; }
}
