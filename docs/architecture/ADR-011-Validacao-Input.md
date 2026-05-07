# ADR-011 — Validação de input: FluentValidation vs DataAnnotations vs JsonConverter+VO

**Status:** aceito
**Data:** 2026-04-27

## Contexto

Aspectos a validar em uma request `POST /api/v1/debitos { "placa": "ABC1234" }`:

1. **Body presente e parseável** — coberto pelo ASP.NET nativamente (JSON malformado → 400).
2. **Tamanho do body** — coberto por middleware de tamanho (será o ADR-015).
3. **Campo `placa` presente** — pode ser null/empty se omitido.
4. **Formato da placa** — regex Mercosul (`AAA0A00`) ou antigo (`AAA0000`); inválido → HTTP 400 com `{"error":"invalid_plate"}`.
5. **Campos desconhecidos no JSON** — rejeitar (`UnmappedMemberHandling=Disallow` no .NET 9+) — também ADR-015.

A pergunta deste ADR: **onde mora a validação do formato da placa**, considerando que o `Plate` (VO, ADR-006) **já valida no `Plate.Parse`**?

## Opções consideradas

### Opção A — `JsonConverter` + `Plate.Parse` (recomendada)

O DTO de request tem `public required Plate Placa { get; init; }`. Um `JsonConverter<Plate>` chama `Plate.Parse(reader.GetString())` durante a desserialização. Inválido → `InvalidPlateException` → middleware central → `400 invalid_plate`.

```csharp
public sealed class PlateJsonConverter : JsonConverter<Plate>
{
    public override Plate Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
        => Plate.Parse(reader.GetString() ?? throw new InvalidPlateException(null));

    public override void Write(Utf8JsonWriter writer, Plate value, JsonSerializerOptions o)
        => writer.WriteStringValue(value.Value);
}
```

- ✅ **Validação acontece no único lugar onde já mora (Plate.Parse)**: zero duplicação. ADR-006 vira fonte única da verdade.
- ✅ **DTO já tipado com `Plate`**, não `string` — assinaturas internas mais expressivas.
- ✅ **Caminho de erro unificado**: a mesma exception (`InvalidPlateException`) é lançada se a placa vier do request, de um teste, de uma chamada interna. Middleware central trata uniformemente.
- ✅ **Zero dependência externa** para a validação principal.
- ❌ Para body com **vários campos**, validação de regras compostas (ex: "campo X obrigatório só se Y for Z") fica esquisita em converters.
- ❌ Mensagem de erro vem da exception — menos flexível que a API rica de mensagens do FluentValidation.

### Opção B — DataAnnotations (`[Required]`, `[RegularExpression]`)

```csharp
public sealed class ConsultRequest
{
    [Required, RegularExpression("^[A-Z]{3}\\d[A-Z]\\d{2}$|^[A-Z]{3}\\d{4}$")]
    public string Placa { get; init; } = string.Empty;
}
```

`[ApiController]` aciona validação automática e devolve 400 com `ProblemDetails`.

- ✅ Built-in, zero dependência.
- ✅ ASP.NET valida automaticamente.
- ❌ **Duplica a regex**: agora vive no atributo DTO **e** no `Plate.Parse`. Mudou o regex? Tem que lembrar de mudar nos dois.
- ❌ DTO recebe `string`, não `Plate` — perde o ganho de tipo do ADR-006.
- ❌ Customização de payload de erro (formato `{"error":"invalid_plate"}`) requer trabalho extra.

### Opção C — FluentValidation

Validator separado: `class ConsultRequestValidator : AbstractValidator<ConsultRequest>`.

```csharp
public sealed class ConsultRequestValidator : AbstractValidator<ConsultRequest>
{
    public ConsultRequestValidator()
    {
        RuleFor(r => r.Placa)
            .NotEmpty()
            .Must(BeValidPlate)
            .WithErrorCode("invalid_plate");
    }

    private static bool BeValidPlate(string? raw) =>
        raw is not null && (Mercosul.IsMatch(raw) || Antiga.IsMatch(raw));
}
```

- ✅ Validators são classes separadas, **testáveis** isoladamente.
- ✅ Sintaxe expressiva para regras compostas (`When`, `Must`, `Custom`, `Cascade`).
- ✅ Bom argumento de banca: *"FluentValidation é o padrão da indústria para validação em ASP.NET"*.
- ❌ **Duplicação igual à Opção B**: a regra de placa vive no validator **e** no `Plate.Parse`. A não ser que o validator chame `Plate.TryParse`, o que é possível mas desnecessário se a Opção A já resolve.
- ❌ Dependência externa adicional para resolver um problema que o VO já resolve.
- ❌ Para 1 campo, é over-engineering visível.

### Opção D — Validação manual no Controller

```csharp
public IActionResult Consult([FromBody] ConsultRequest req)
{
    if (string.IsNullOrWhiteSpace(req.Placa)) return Problem(...);
    if (!IsValidPlate(req.Placa)) return Problem(...);
    var plate = Plate.Parse(req.Placa);
    ...
}
```

- ✅ Sem dependência.
- ❌ Validação espalhada — qualquer endpoint novo precisa repetir.
- ❌ Mistura responsabilidade (Controller faz validação, parsing e orquestração).
- ❌ "Cara" de júnior em apresentação sênior.

## Tradeoffs principais (lado a lado)

| Critério | A — JsonConverter+VO | B — DataAnnotations | C — FluentValidation | D — Manual |
|---|---|---|---|---|
| Duplica a validação do VO | ❌ não | ✅ sim | ✅ sim (a menos que delegue ao VO) | ✅ sim |
| DTO recebe `Plate` (não `string`) | ✅ | ❌ | ❌ | ❌ |
| Dependência externa | nenhuma | nenhuma | FluentValidation | nenhuma |
| Suporte a regras compostas | limitado | limitado | excelente | manual |
| Boilerplate | mínimo | baixo | médio | alto |
| Defensável em sênior | ✅ (foco em VO como SSOT) | ⚠️ idiomático mas duplica | ✅ (padrão da indústria) | ❌ |
| Adequado a este desafio (1 campo) | ✅ | ⚠️ | ⚠️ over-engineering | ❌ |

## Recomendação

**Opção A — `JsonConverter` + `Plate.Parse`** como mecanismo principal.

Razão central: **`Plate` é a fonte única da verdade da validação de placa** (ADR-006). Adicionar uma segunda camada de validação (B ou C) só faria sentido se duplicasse a regra, o que viola DRY e fragiliza refactors. A Opção A delega a validação ao próprio VO, que é o que mais alinha com a arquitetura escolhida.

**Para o que NÃO é placa**:
- `[Required]` no DTO (DataAnnotation) cobre "campo placa presente". É leve, idiomático, integrado ao ASP.NET, e não duplica o `Plate.Parse`.
- ASP.NET nativo cobre body malformado, content-type errado, body vazio.

**FluentValidation fica para o futuro**: registrado no ADR como melhoria a adotar **se** o body crescer com regras compostas (validações cruzadas entre campos, "campo X obrigatório só se Y").

## Decisão

- **Validação de formato da placa**: `JsonConverter<Plate>` que invoca `Plate.Parse(reader.GetString())` durante a desserialização da request. `InvalidPlateException` lançada pelo VO é traduzida pelo middleware central (ADR-004 / futuro ADR-014) em `HTTP 400 { "error": "invalid_plate" }`.
- **Validação de presença do campo**: `[Required]` (DataAnnotation) no DTO de request, com `[ApiController]` automaticamente respondendo 400 quando ausente.
- **Validação de body malformado / content-type / vazio**: ASP.NET nativo (sem código adicional).
- **FluentValidation**: não adotado neste momento. Registrado no README como melhoria futura caso o body cresça com regras compostas (validações cruzadas entre múltiplos campos).

## Justificativa

1. **DRY**: o `Plate` VO (ADR-006) é a fonte única da verdade da regra de placa. Adicionar uma camada paralela de validação (DataAnnotations regex ou FluentValidation) duplicaria a regra e fragilizaria refactors futuros.
2. **Tipagem na borda**: o DTO recebe `Plate` direto (não `string`), preservando a expressividade dos VOs em todas as camadas.
3. **Caminho de erro unificado**: a mesma `InvalidPlateException` é lançada vinda de request, teste, ou chamada interna. Middleware central trata uniformemente — não há "validação só no controller" e "validação no domínio" separadas.
4. **Custo defensável**: zero dependências adicionais; `JsonConverter` ≈ 15 linhas. Se o body crescer e exigir regras compostas, FluentValidation entra sem refator do existente.
5. **Mensagem de banca**: *"reduzi duplicação tratando o Value Object como autoridade da validação. FluentValidation é excelente para regras compostas — aqui seria duplicação. Reservei para crescimento futuro."*

---
