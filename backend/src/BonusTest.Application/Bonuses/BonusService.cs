using BonusTest.Domain.Constants;
using BonusTest.Domain.Entities;
using BonusTest.Domain.Exceptions;
using BonusTest.Application.Common;

namespace BonusTest.Application.Bonuses;

public class BonusService(IBonusRepository repository, TimeProvider clock)
{
    // Lima no cambia de horario; si el sistema no trae la zona, basta con UTC-5.
    private static readonly TimeZoneInfo Lima = TimeZoneInfo.TryFindSystemTimeZoneById("America/Lima", out var zone)
        ? zone : TimeZoneInfo.CreateCustomTimeZone("America/Lima", TimeSpan.FromHours(-5), "Lima", "Lima");

    public Task<PagedResponse<BonusDto>> ListAsync(BonusFilter filter, CancellationToken cancellationToken) =>
        repository.ListAsync(filter, cancellationToken);

    public async Task<EmployeeDto> GetEmployeeAsync(string departmentCode, int employeeNumber, CancellationToken cancellationToken)
    {
        var employee = await repository.GetEmployeeAsync(departmentCode, employeeNumber, cancellationToken) ??
            throw new BusinessException("EMPLOYEE_NOT_FOUND", "No existe un empleado con esa área y número.");
        if (employee.EmployeeStateCode != EmployeeStates.Active)
            throw new BusinessException("EMPLOYEE_NOT_ACTIVE", "Solo se aceptan empleados activos.");
        return new EmployeeDto(employee.DepartmentCode, employee.EmployeeNumber, employee.FullName,
            employee.HireDate, employee.MonthlySalary, employee.TaxRate);
    }

    public async Task<BonusCreatedDto> RegisterAsync(RegisterBonusRequest request, string userCode, CancellationToken cancellationToken)
    {
        var bonus = await repository.RegisterAsync(request, userCode, Now(), cancellationToken);
        return new BonusCreatedDto(bonus.BonusId, bonus.StateCode, bonus.GrossAmount, bonus.TaxAmount, bonus.NetAmount);
    }

    public async Task CancelAsync(int bonusId, string userCode, CancellationToken cancellationToken)
    {
        var bonus = await FindAsync(bonusId, cancellationToken);
        bonus.Cancel(userCode, Now());
        await repository.SaveAsync(cancellationToken);
    }

    public async Task DeleteAsync(int bonusId, string userCode, CancellationToken cancellationToken)
    {
        var bonus = await FindAsync(bonusId, cancellationToken);
        bonus.Delete(userCode, Now());
        await repository.SaveAsync(cancellationToken);
    }

    public Task PayAsync(int bonusId, string userCode, CancellationToken cancellationToken) =>
        repository.PayAsync(bonusId, userCode, Now(), cancellationToken);

    private async Task<Bonus> FindAsync(int bonusId, CancellationToken cancellationToken) =>
        await repository.FindAsync(bonusId, cancellationToken) ??
        throw new BusinessException("BONUS_NOT_FOUND", "El bono no existe o fue eliminado.");

    private DateTimeOffset Now() => TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Lima);
}
