using BonusTest.Domain.Bonuses;
using BonusTest.Domain.Entities;
using BonusTest.Domain.Exceptions;

namespace BonusTest.Tests;

public class BonusPaymentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.FromHours(-5));
    private static EmployeeProjectHours Hours(int id, string name, decimal hours) =>
        new() { EmployeeProjectHoursId = id, ProjectName = name, Hours = hours };

    [Fact]
    public void Reparte_proporcionalmente_y_el_ultimo_recibe_el_remanente()
    {
        var result = BonusDistributor.Distribute(100m, [Hours(1, "A", 1), Hours(2, "B", 1), Hours(3, "C", 1)]);
        Assert.Equal(new[] { 33.33m, 33.33m, 33.34m }, result.Select(x => x.Amount));
        Assert.Equal(new[] { 1, 2, 3 }, result.Select(x => x.LineNumber));
    }

    [Fact]
    public void Redondea_el_medio_hacia_arriba_y_cuadra_el_saldo()
    {
        var result = BonusDistributor.Distribute(0.25m, [Hours(1, "A", 1), Hours(2, "B", 1)]);
        Assert.Equal(new[] { 0.13m, 0.12m }, result.Select(x => x.Amount));
    }

    [Fact]
    public void Agrupa_proyectos_repetidos_y_ordena_por_su_primera_fila()
    {
        var result = BonusDistributor.Distribute(500m, [Hours(3, " A ", 40), Hours(2, "B", 40), Hours(1, "A", 80)]);
        Assert.Equal(new[] { "A", "B" }, result.Select(x => x.ProjectName));
        Assert.Equal(new[] { 120m, 40m }, result.Select(x => x.Hours));
        Assert.Equal(new[] { 375m, 125m }, result.Select(x => x.Amount));
    }

    [Fact]
    public void Un_proyecto_recibe_todo_y_un_bruto_cero_no_genera_negativos()
    {
        Assert.Equal(500m, Assert.Single(BonusDistributor.Distribute(500m, [Hours(1, "A", 0.01m)])).Amount);
        Assert.All(BonusDistributor.Distribute(0m, [Hours(1, "A", 1), Hours(2, "B", 1)]), x => Assert.Equal(0m, x.Amount));
    }

    [Fact]
    public void Conserva_precision_con_el_bruto_y_las_horas_maximas()
    {
        var result = BonusDistributor.Distribute(99999999999999.99m, [Hours(1, "A", 999999.99m), Hours(2, "B", 0.01m)]);
        Assert.Equal(new[] { 99999998999999.99m, 1000000m }, result.Select(x => x.Amount));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1000000)]
    public void Rechaza_horas_fuera_del_rango_original(int hours)
    {
        Assert.Equal("INVALID_PROJECT_HOURS", Assert.Throws<BusinessException>(() =>
            BonusDistributor.Distribute(100m, [Hours(1, "A", hours)])).Code);
    }

    [Fact]
    public void Rechaza_horas_ausentes_fraccion_invalida_o_nombre_vacio()
    {
        foreach (var rows in new EmployeeProjectHours[][] { [], [Hours(1, "A", 0.001m)], [Hours(1, " ", 1)], [Hours(1, new string('a', 101), 1)] })
            Assert.Equal("INVALID_PROJECT_HOURS", Assert.Throws<BusinessException>(() => BonusDistributor.Distribute(100m, rows)).Code);
    }

    [Fact]
    public void Un_remanente_negativo_no_altera_estado_auditoria_ni_detalle()
    {
        var bonus = Register(0.02m);
        var rows = Enumerable.Range(1, 4).Select(i => Hours(i, i.ToString(), 1));
        Assert.Equal("INVALID_DISTRIBUTION", Assert.Throws<BusinessException>(() => bonus.Pay(rows, "pagador", Now)).Code);
        Assert.Equal("0201", bonus.StateCode);
        Assert.Null(bonus.ModifiedBy);
        Assert.Empty(bonus.Allocations);
    }

    [Fact]
    public void Pago_conserva_importes_creacion_y_registra_quien_y_cuando()
    {
        var bonus = Register();
        bonus.Pay([Hours(1, "A", 120), Hours(2, "B", 40)], " pagador ", Now.AddMinutes(1));
        Assert.Equal("0202", bonus.StateCode);
        Assert.Equal("pagador", bonus.ModifiedBy);
        Assert.Equal(Now.AddMinutes(1), bonus.ModifiedAt);
        Assert.Equal("creador", bonus.CreatedBy);
        Assert.Equal(Now, bonus.CreatedAt);
        Assert.Equal(500m, bonus.GrossAmount);
        Assert.Equal(40m, bonus.TaxAmount);
        Assert.Equal(460m, bonus.NetAmount);
        Assert.Equal(new[] { 375m, 125m }, bonus.Allocations.Select(x => x.Amount));
        Assert.Throws<BusinessException>(() => bonus.Pay([Hours(1, "A", 1)], "otro", Now.AddMinutes(2)));
        Assert.Throws<BusinessException>(() => bonus.Cancel("otro", Now.AddMinutes(2)));
        Assert.Throws<BusinessException>(() => bonus.Delete("otro", Now.AddMinutes(2)));
        Assert.Equal(2, bonus.Allocations.Count);
    }

    [Fact]
    public void No_paga_anulados_eliminados_ni_operadores_o_fechas_invalidos()
    {
        var cancelled = Register(); cancelled.Cancel("otro", Now);
        var deleted = Register(); deleted.Delete("otro", Now);
        foreach (var bonus in new[] { cancelled, deleted })
            Assert.Equal("BONUS_NOT_REGISTERED", Assert.Throws<BusinessException>(() => bonus.Pay([], "otro", Now)).Code);
        var valid = Register();
        Assert.Throws<BusinessException>(() => valid.Pay([Hours(1, "A", 1)], " ", Now));
        Assert.Throws<BusinessException>(() => valid.Pay([Hours(1, "A", 1)], "otro", Now.AddSeconds(-1)));
        Assert.Empty(valid.Allocations);
        Assert.Null(valid.ModifiedAt);
    }

    private static Bonus Register(decimal value = 500m) => Bonus.Register(
        new Employee { DepartmentCode = "TECH1", EmployeeNumber = 101, EmployeeStateCode = "0101", MonthlySalary = 5000m, TaxRate = 8m },
        "2000-01", 1, value, new BonusReason { ReasonCode = "000001", State = true }, "creador", Now);
}
