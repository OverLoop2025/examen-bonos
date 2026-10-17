using System.Numerics;
using BonusTest.Domain.Entities;
using BonusTest.Domain.Exceptions;

namespace BonusTest.Domain.Bonuses;

public static class BonusDistributor
{
    public static List<BonusAllocation> Distribute(decimal grossAmount, IEnumerable<EmployeeProjectHours> hours)
    {
        var rows = hours.OrderBy(x => x.EmployeeProjectHoursId).ToList();
        if (rows.Count == 0 || rows.Any(x => x.Hours <= 0 || x.Hours > 999999.99m ||
            decimal.Round(x.Hours, 2) != x.Hours || string.IsNullOrWhiteSpace(x.ProjectName) || x.ProjectName.Trim().Length > 100))
            throw new BusinessException("INVALID_PROJECT_HOURS", "El empleado debe tener proyectos con nombre y horas positivas válidas.");
        if (grossAmount < 0 || grossAmount > 99999999999999.99m || decimal.Round(grossAmount, 2) != grossAmount)
            throw new BusinessException("INVALID_DISTRIBUTION", "El bruto no es válido para repartir.");

        var projects = rows.GroupBy(x => x.ProjectName.Trim(), StringComparer.Ordinal)
            .Select(g => new { Name = g.Key, Hours = g.Sum(x => x.Hours) }).ToList();
        var totalHours = (BigInteger)(projects.Sum(x => x.Hours) * 100m);
        var remaining = grossAmount;
        var result = new List<BonusAllocation>();
        foreach (var project in projects)
        {
            var amount = remaining;
            if (result.Count < projects.Count - 1)
            {
                // La división en centavos conserva el medio exacto antes de redondear.
                var cents = BigInteger.DivRem((BigInteger)(grossAmount * 100m) * (BigInteger)(project.Hours * 100m), totalHours, out var rest);
                if (rest * 2 >= totalHours) cents++;
                amount = (decimal)cents / 100m;
            }
            remaining -= amount;
            if (amount < 0 || remaining < 0)
                throw new BusinessException("INVALID_DISTRIBUTION", "El redondeo supera el bruto. No se puede repartir sin un importe negativo.");
            result.Add(new BonusAllocation(result.Count + 1, project.Name, project.Hours, amount));
        }
        return result;
    }
}
