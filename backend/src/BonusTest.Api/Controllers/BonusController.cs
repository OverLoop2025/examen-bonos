using BonusTest.Application.Bonuses;
using BonusTest.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace BonusTest.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public class BonusController(BonusService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResponse<BonusDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] BonusFilter filter, CancellationToken cancellationToken) =>
        Ok(await service.ListAsync(filter, cancellationToken));

    [HttpGet("employee/{departmentCode}/{employeeNumber:int}")]
    [ProducesResponseType<EmployeeDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEmployee(string departmentCode, int employeeNumber, CancellationToken cancellationToken) =>
        Ok(await service.GetEmployeeAsync(departmentCode, employeeNumber, cancellationToken));

    [HttpPost]
    [ProducesResponseType<BonusCreatedDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Register(RegisterBonusRequest request, [FromHeader] string? userCode, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await service.RegisterAsync(request, userCode ?? "", cancellationToken));

    [HttpPut("{bonusId:int}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(int bonusId, [FromHeader] string? userCode, CancellationToken cancellationToken)
    {
        await service.CancelAsync(bonusId, userCode ?? "", cancellationToken);
        return NoContent();
    }

    [HttpDelete("{bonusId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int bonusId, [FromHeader] string? userCode, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(bonusId, userCode ?? "", cancellationToken);
        return NoContent();
    }

    [HttpPost("{bonusId:int}/pay")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Pay(int bonusId, [FromHeader] string? userCode, CancellationToken cancellationToken)
    {
        await service.PayAsync(bonusId, userCode ?? "", cancellationToken);
        return NoContent();
    }
}
