using System.Globalization;
using BonusTest.Domain.Bonuses;
using BonusTest.Domain.Constants;
using BonusTest.Domain.Exceptions;

namespace BonusTest.Domain.Entities;

public class Bonus
{
    private Bonus() { }

    public int BonusId { get; private set; }
    public string DepartmentCode { get; private set; } = null!;
    public int EmployeeNumber { get; private set; }
    public string Period { get; private set; } = null!;
    public int BonusTypeId { get; private set; }
    public decimal Value { get; private set; }
    public string ReasonCode { get; private set; } = null!;
    public decimal MonthlySalary { get; private set; }
    public decimal TaxRate { get; private set; }
    public decimal GrossAmount { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal NetAmount { get; private set; }
    public string StateCode { get; private set; } = null!;
    public bool IsDeleted { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public string? ModifiedBy { get; private set; }
    public DateTimeOffset? ModifiedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static Bonus Register(Employee employee, string period, int bonusTypeId, decimal value,
        BonusReason reason, string userCode, DateTimeOffset now)
    {
        if (employee is null || employee.EmployeeStateCode != EmployeeStates.Active)
            throw new BusinessException("EMPLOYEE_NOT_ACTIVE", "Solo se aceptan empleados activos.");
        if (reason is null || !reason.State)
            throw new BusinessException("REASON_NOT_ACTIVE", "El motivo debe estar activo.");
        if (period is not { Length: 7 } ||
            !DateOnly.TryParseExact(period, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month) ||
            month > new DateOnly(now.Year, now.Month, 1))
            throw new BusinessException("INVALID_PERIOD", "El periodo debe tener formato YYYY-MM y no superar el mes actual.");

        var user = ValidateUser(userCode);
        var amounts = BonusCalculator.Calculate(employee.MonthlySalary, employee.TaxRate, bonusTypeId, value);

        return new Bonus
        {
            DepartmentCode = employee.DepartmentCode,
            EmployeeNumber = employee.EmployeeNumber,
            Period = period,
            BonusTypeId = bonusTypeId,
            Value = value,
            ReasonCode = reason.ReasonCode,
            MonthlySalary = employee.MonthlySalary,
            TaxRate = employee.TaxRate,
            GrossAmount = amounts.GrossAmount,
            TaxAmount = amounts.TaxAmount,
            NetAmount = amounts.NetAmount,
            StateCode = BonusStates.Registered,
            CreatedBy = user,
            CreatedAt = now
        };
    }

    public void Cancel(string userCode, DateTimeOffset now)
    {
        RecordModification(userCode, now);
        StateCode = BonusStates.Cancelled;
    }

    public void Delete(string userCode, DateTimeOffset now)
    {
        RecordModification(userCode, now);
        IsDeleted = true;
    }

    private void RecordModification(string userCode, DateTimeOffset now)
    {
        if (StateCode != BonusStates.Registered || IsDeleted)
            throw new BusinessException("BONUS_NOT_REGISTERED", "Solo se puede anular o eliminar un bono registrado.");
        var user = ValidateUser(userCode);
        if (now < (ModifiedAt ?? CreatedAt))
            throw new BusinessException("INVALID_MODIFICATION_DATE", "La modificación no puede ser anterior al registro.");
        ModifiedBy = user;
        ModifiedAt = now;
    }

    private static string ValidateUser(string userCode)
    {
        if (string.IsNullOrWhiteSpace(userCode) || userCode.Trim().Length > 50)
            throw new BusinessException("INVALID_USER", "El usuario es obligatorio y admite hasta 50 caracteres.");
        return userCode.Trim();
    }
}
