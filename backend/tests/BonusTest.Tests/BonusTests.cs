using BonusTest.Domain.Constants;
using BonusTest.Domain.Entities;
using BonusTest.Domain.Exceptions;

namespace BonusTest.Tests;

public class BonusTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.FromHours(-5));

    [Fact]
    public void Registra_con_importes_estado_y_auditoria()
    {
        var employee = Employee();
        var bonus = Register(employee: employee);
        employee.MonthlySalary = 9000m;
        employee.TaxRate = 10m;

        Assert.Equal("TECH1", bonus.DepartmentCode);
        Assert.Equal(101, bonus.EmployeeNumber);
        Assert.Equal("2026-10", bonus.Period);
        Assert.Equal(BonusStates.Registered, bonus.StateCode);
        Assert.Equal(BonusTypes.SalaryPercentage, bonus.BonusTypeId);
        Assert.Equal("000001", bonus.ReasonCode);
        Assert.Equal(10m, bonus.Value);
        Assert.Equal(5000m, bonus.MonthlySalary);
        Assert.Equal(8m, bonus.TaxRate);
        Assert.Equal(500m, bonus.GrossAmount);
        Assert.Equal(40m, bonus.TaxAmount);
        Assert.Equal(460m, bonus.NetAmount);
        Assert.Equal("operador", bonus.CreatedBy);
        Assert.Equal(Now, bonus.CreatedAt);
        Assert.Null(bonus.ModifiedBy);
        Assert.Null(bonus.ModifiedAt);
        Assert.False(bonus.IsDeleted);
    }

    [Theory]
    [InlineData("2026-11")]
    [InlineData("2026-1")]
    [InlineData("2026-00")]
    [InlineData("2026-13")]
    [InlineData("0000-01")]
    [InlineData("2026-10-01")]
    [InlineData(" 2026-10")]
    [InlineData("")]
    [InlineData(null)]
    public void Rechaza_periodos_invalidos_o_futuros(string? period)
    {
        Assert.Throws<BusinessException>(() => Register(period: period!));
    }

    [Theory]
    [InlineData("2025-12")]
    [InlineData("0001-01")]
    [InlineData("2026-10")]
    public void Acepta_el_mes_actual_y_periodos_anteriores(string period)
    {
        Assert.Equal(period, Register(period: period).Period);
    }

    [Fact]
    public void Usa_el_mes_del_instante_recibido_en_el_limite_de_mes()
    {
        var endOfMonth = new DateTimeOffset(2026, 9, 30, 23, 59, 59, TimeSpan.FromHours(-5));
        Assert.Throws<BusinessException>(() => Bonus.Register(Employee(), "2026-10", 2, 10m, Reason(), "operador", endOfMonth));
        Assert.Equal("2026-10", Bonus.Register(Employee(), "2026-10", 2, 10m, Reason(), "operador", endOfMonth.AddSeconds(1)).Period);
    }

    [Fact]
    public void Rechaza_empleado_cesado_y_motivo_inactivo()
    {
        var employee = Employee();
        employee.EmployeeStateCode = EmployeeStates.Terminated;
        Assert.Throws<BusinessException>(() => Register(employee: employee));
        var reason = Reason();
        reason.State = false;
        Assert.Throws<BusinessException>(() => Register(reason: reason));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Exige_el_usuario_que_opera(string? user)
    {
        Assert.Throws<BusinessException>(() => Register(user: user!));
    }

    [Fact]
    public void Valida_la_longitud_del_usuario_antes_de_persistir()
    {
        Assert.Throws<BusinessException>(() => Register(user: new string('a', 51)));
        Assert.Equal(new string('a', 50), Register(user: new string('a', 50)).CreatedBy);
    }

    [Fact]
    public void Anula_un_registrado_y_conserva_creacion_e_importes()
    {
        var bonus = Register();
        bonus.Cancel("segundo", Now.AddMinutes(1));

        Assert.Equal(BonusStates.Cancelled, bonus.StateCode);
        Assert.False(bonus.IsDeleted);
        Assert.Equal("segundo", bonus.ModifiedBy);
        Assert.Equal(Now.AddMinutes(1), bonus.ModifiedAt);
        Assert.Equal("operador", bonus.CreatedBy);
        Assert.Equal(Now, bonus.CreatedAt);
        Assert.Equal(460m, bonus.NetAmount);
        Assert.Throws<BusinessException>(() => bonus.Cancel("otro", Now.AddMinutes(2)));
        Assert.Throws<BusinessException>(() => bonus.Delete("otro", Now.AddMinutes(2)));
    }

    [Fact]
    public void Elimina_logicamente_solo_una_vez_sin_cambiar_el_estado()
    {
        var bonus = Register();
        bonus.Delete("segundo", Now.AddMinutes(1));

        Assert.True(bonus.IsDeleted);
        Assert.Equal(BonusStates.Registered, bonus.StateCode);
        Assert.Equal("segundo", bonus.ModifiedBy);
        Assert.Equal(Now.AddMinutes(1), bonus.ModifiedAt);
        Assert.Throws<BusinessException>(() => bonus.Cancel("otro", Now.AddMinutes(2)));
        Assert.Throws<BusinessException>(() => bonus.Delete("otro", Now.AddMinutes(2)));
    }

    [Fact]
    public void Una_operacion_invalida_no_altera_el_bono()
    {
        var bonus = Register();
        Assert.Throws<BusinessException>(() => bonus.Cancel(" ", Now.AddMinutes(1)));
        Assert.Throws<BusinessException>(() => bonus.Delete("otro", Now.AddSeconds(-1)));
        Assert.Equal(BonusStates.Registered, bonus.StateCode);
        Assert.False(bonus.IsDeleted);
        Assert.Null(bonus.ModifiedAt);
        Assert.Null(bonus.ModifiedBy);
    }

    private static Bonus Register(string period = "2026-10", Employee? employee = null,
        BonusReason? reason = null, string user = "operador") =>
        Bonus.Register(employee ?? Employee(), period, BonusTypes.SalaryPercentage, 10m,
            reason ?? Reason(), user, Now);

    private static Employee Employee() => new()
    {
        DepartmentCode = "TECH1", EmployeeNumber = 101, MonthlySalary = 5000m,
        TaxRate = 8m, EmployeeStateCode = EmployeeStates.Active
    };

    private static BonusReason Reason() => new()
    {
        ReasonCode = "000001", ReasonDescription = "Cumplimiento de metas", State = true
    };
}
