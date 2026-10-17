using System.Globalization;
using BonusTest.Domain.Bonuses;
using BonusTest.Domain.Constants;
using BonusTest.Domain.Exceptions;

namespace BonusTest.Tests;

public class BonusCalculatorTests
{
    [Theory]
    [InlineData(2, "5000", "8", "10", "500", "40", "460")]
    [InlineData(1, "5000", "8", "0.125", "0.13", "0.01", "0.12")]
    [InlineData(1, "100", "10", "1.25", "1.25", "0.13", "1.12")]
    [InlineData(1, "2500", "0", "2500", "2500", "0", "2500")]
    [InlineData(2, "5000", "100", "100", "5000", "5000", "0")]
    [InlineData(2, "5000", "8", "0.125", "6.25", "0.50", "5.75")]
    [InlineData(2, "5000", "8", "12.345678901234", "617.28", "49.38", "567.90")]
    [InlineData(1, "100", "8", "0.001", "0", "0", "0")]
    [InlineData(2, "99999999999999.99", "100", "100", "99999999999999.99", "99999999999999.99", "0")]
    public void Calcula_y_redondea_segun_el_enunciado(int type, string salary, string taxRate,
        string value, string gross, string tax, string net)
    {
        var result = BonusCalculator.Calculate(Number(salary), Number(taxRate), type, Number(value));

        Assert.Equal(Number(gross), result.GrossAmount);
        Assert.Equal(Number(tax), result.TaxAmount);
        Assert.Equal(Number(net), result.NetAmount);
    }

    [Theory]
    [InlineData(1, "5000", "8", "0")]
    [InlineData(1, "5000", "8", "-0.01")]
    [InlineData(1, "5000", "8", "5000.001")]
    [InlineData(2, "5000", "8", "0")]
    [InlineData(2, "5000", "8", "100.0001")]
    [InlineData(3, "5000", "8", "10")]
    [InlineData(1, "0", "8", "1")]
    [InlineData(1, "5000", "-1", "10")]
    [InlineData(1, "5000", "100.01", "10")]
    public void Rechaza_valores_fuera_de_las_reglas(int type, string salary, string taxRate, string value)
    {
        Assert.Throws<BusinessException>(() =>
            BonusCalculator.Calculate(Number(salary), Number(taxRate), type, Number(value)));
    }

    [Fact]
    public void No_convierte_una_fraccion_menor_al_medio_en_un_empate_al_multiplicar()
    {
        var result = BonusCalculator.Calculate(99999999999999.99m, 0m,
            BonusTypes.SalaryPercentage, 50.00000000000001m);

        Assert.Equal(50000000000000.00m, result.GrossAmount);
        Assert.Equal(50000000000000.00m, result.NetAmount);
    }

    [Fact]
    public void No_redondea_una_entrada_que_no_se_puede_conservar()
    {
        Assert.Throws<BusinessException>(() =>
            BonusCalculator.Calculate(5000m, 8m, BonusTypes.SalaryPercentage, 0.123456789012345m));
    }

    private static decimal Number(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
