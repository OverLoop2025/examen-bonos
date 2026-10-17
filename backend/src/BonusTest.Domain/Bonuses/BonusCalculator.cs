using System.Numerics;
using BonusTest.Domain.Constants;
using BonusTest.Domain.Exceptions;

namespace BonusTest.Domain.Bonuses;

public readonly record struct BonusAmounts(decimal GrossAmount, decimal TaxAmount, decimal NetAmount);

public static class BonusCalculator
{
    public static BonusAmounts Calculate(decimal monthlySalary, decimal taxRate, int bonusTypeId, decimal value)
    {
        if (monthlySalary <= 0 || monthlySalary > 99999999999999.99m || Round(monthlySalary) != monthlySalary)
            throw new BusinessException("INVALID_SALARY", "El sueldo mensual no es válido.");
        if (taxRate < 0 || taxRate > 100 || Round(taxRate) != taxRate)
            throw new BusinessException("INVALID_TAX_RATE", "El porcentaje de retención no es válido.");
        if (bonusTypeId is not (BonusTypes.FixedAmount or BonusTypes.SalaryPercentage))
            throw new BusinessException("INVALID_BONUS_TYPE", "El tipo de bono no es válido.");
        if (value <= 0 || (bonusTypeId == BonusTypes.FixedAmount ? value > monthlySalary : value > 100))
            throw new BusinessException("INVALID_BONUS_VALUE", "El monto debe ser mayor que cero y no superar el sueldo; el porcentaje, hasta 100.");
        if (decimal.Round(value, 14) != value)
            throw new BusinessException("INVALID_BONUS_PRECISION", "El valor admite hasta 14 decimales.");

        var gross = bonusTypeId == BonusTypes.FixedAmount ? Round(value) : PercentageOf(monthlySalary, value);
        var tax = PercentageOf(gross, taxRate);
        return new BonusAmounts(gross, tax, gross - tax);
    }

    private static decimal PercentageOf(decimal amount, decimal percentage)
    {
        // El producto en centavos evita perder precisión antes del redondeo final.
        const decimal scale = 100000000000000m;
        var product = (BigInteger)(amount * 100m) * (BigInteger)(percentage * scale);
        var divisor = (BigInteger)(100m * scale);
        var cents = BigInteger.DivRem(product, divisor, out var remainder);
        if (remainder * 2 >= divisor)
            cents++;
        return (decimal)cents / 100m;
    }

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
