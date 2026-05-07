using Dok.Api.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Dok.Api.Controllers;

[ApiController]
[Route("api/v1/debitos")]
public sealed class DebtsController(IDebtsService service) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<DebtsResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorPayload>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorPayload>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<UnknownDebtTypeErrorPayload>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ErrorPayload>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Consult([FromBody] ConsultRequest request, CancellationToken ct)
    {
        var result = await service.GetAsync(request.Placa, ct);
        return Ok(result.ToDto());
    }
}
