using System.Data;
using BonusTest.Application.Bonuses;
using BonusTest.Application.Common;
using BonusTest.Domain.Entities;
using BonusTest.Domain.Exceptions;
using BonusTest.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BonusTest.Infrastructure.Repositories;

public class BonusRepository(AppDbContext context) : IBonusRepository
{
    public async Task<PagedResponse<BonusDto>> ListAsync(BonusFilter filter, CancellationToken cancellationToken)
    {
        var query = context.Bonuses.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filter.DepartmentCode))
            query = query.Where(x => x.DepartmentCode == filter.DepartmentCode);
        if (filter.EmployeeNumber.HasValue)
            query = query.Where(x => x.EmployeeNumber == filter.EmployeeNumber);
        if (!string.IsNullOrWhiteSpace(filter.Period))
            query = query.Where(x => x.Period == filter.Period);
        if (!string.IsNullOrWhiteSpace(filter.StateCode))
            query = query.Where(x => x.StateCode == filter.StateCode);

        var total = await query.CountAsync(cancellationToken);
        var offset = (long)(filter.PageIndex - 1) * filter.PageSize;
        if (offset >= total)
            return PagedResponse<BonusDto>.Create([], filter.PageIndex, filter.PageSize, total);

        var rows = await (
            from bonus in query
            join employee in context.Employees on new { bonus.DepartmentCode, bonus.EmployeeNumber }
                equals new { employee.DepartmentCode, employee.EmployeeNumber }
            join department in context.Departments on bonus.DepartmentCode equals department.DepartmentCode
            join type in context.BonusTypes on bonus.BonusTypeId equals type.BonusTypeId
            join reason in context.BonusReasons on bonus.ReasonCode equals reason.ReasonCode
            join state in context.States on bonus.StateCode equals state.StateCode
            orderby bonus.BonusId descending
            select new BonusDto(bonus.BonusId, bonus.DepartmentCode, department.DepartmentName,
                bonus.EmployeeNumber, employee.FullName, bonus.Period, bonus.BonusTypeId, type.BonusTypeDescription,
                bonus.Value, bonus.ReasonCode, reason.ReasonDescription, bonus.StateCode, state.StateDescription,
                bonus.GrossAmount, bonus.TaxAmount, bonus.NetAmount))
            .Skip((int)offset).Take(filter.PageSize).ToListAsync(cancellationToken);
        return PagedResponse<BonusDto>.Create(rows, filter.PageIndex, filter.PageSize, total);
    }

    public Task<Employee?> GetEmployeeAsync(string departmentCode, int employeeNumber, CancellationToken cancellationToken) =>
        context.Employees.AsNoTracking().SingleOrDefaultAsync(x =>
            x.DepartmentCode == departmentCode && x.EmployeeNumber == employeeNumber, cancellationToken);

    public async Task<Bonus> RegisterAsync(RegisterBonusRequest request, string userCode, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var employee = await GetEmployeeAsync(request.DepartmentCode, request.EmployeeNumber, cancellationToken) ??
            throw new BusinessException("EMPLOYEE_NOT_ACTIVE", "Solo se aceptan empleados activos.");
        var reason = await context.BonusReasons.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ReasonCode == request.ReasonCode, cancellationToken) ??
            throw new BusinessException("REASON_NOT_ACTIVE", "El motivo debe estar activo.");
        var bonus = Bonus.Register(employee, request.Period, request.BonusTypeId, request.Value, reason, userCode, now);
        context.Bonuses.Add(bonus);
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return bonus;
    }

    public Task<Bonus?> FindAsync(int bonusId, CancellationToken cancellationToken) =>
        context.Bonuses.SingleOrDefaultAsync(x => x.BonusId == bonusId, cancellationToken);

    public async Task PayAsync(int bonusId, string userCode, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var bonus = await FindAsync(bonusId, cancellationToken) ??
            throw new BusinessException("BONUS_NOT_FOUND", "El bono no existe o fue eliminado.");
        var hours = await context.EmployeeProjectHours.AsNoTracking().Where(x =>
            x.DepartmentCode == bonus.DepartmentCode && x.EmployeeNumber == bonus.EmployeeNumber)
            .ToListAsync(cancellationToken);
        bonus.Pay(hours, userCode, now);
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessException("BONUS_CONFLICT", "El bono cambió durante la operación. Actualice el listado.");
        }
        catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new BusinessException("BONUS_DUPLICATE", "El empleado ya tiene un bono vigente en ese periodo.");
        }
    }
}
