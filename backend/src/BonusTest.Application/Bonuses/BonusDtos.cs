using System.Text.Json.Serialization;

namespace BonusTest.Application.Bonuses;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegisterBonusRequest(string DepartmentCode, int EmployeeNumber, string Period,
    int BonusTypeId, decimal Value, string ReasonCode);

public sealed record EmployeeDto(string DepartmentCode, int EmployeeNumber, string FullName,
    DateTime HireDate, decimal MonthlySalary, decimal TaxRate);

public sealed record BonusCreatedDto(int BonusId, string StateCode, decimal GrossAmount,
    decimal TaxAmount, decimal NetAmount);

public sealed record BonusDto(int BonusId, string DepartmentCode, string DepartmentName,
    int EmployeeNumber, string FullName, string Period, int BonusTypeId, string BonusTypeDescription,
    decimal Value, string ReasonCode, string ReasonDescription, string StateCode, string StateDescription,
    decimal GrossAmount, decimal TaxAmount, decimal NetAmount);
