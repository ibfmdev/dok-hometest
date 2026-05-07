namespace Dok.Domain;

/// <summary>
/// State holder do nome do provider que respondeu com sucesso na request atual.
/// Registrado como Scoped — uma instância por request HTTP. Permite à camada de borda
/// (Api) expor essa informação como header de response sem acoplar Domain ao ASP.NET.
/// </summary>
public sealed class ProviderUsage
{
    public string? Name { get; private set; }

    public void Mark(string name) => Name = name;
}
