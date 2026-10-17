using BonusTest.Domain.Constants;
using BonusTest.Domain.Entities;
using BonusTest.Domain.Exceptions;
using BonusTest.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BonusTest.Tests;

[Collection("SQL Server")]
public class BonusPersistenceTests
{
    private const string SkipSql = "Defina ConnectionStrings__BonusTest para ejecutar las pruebas SQL.";
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.FromHours(-5));
    public static bool SqlServerConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ConnectionStrings__BonusTest"));

    [Fact(Skip = SkipSql, SkipUnless = nameof(SqlServerConfigured))]
    public async Task Esquema_tiene_clave_compuesta_restricciones_y_unicidad_filtrada()
    {
        await using var db = Context();
        await db.Database.OpenConnectionAsync(Token);
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM sys.foreign_keys
            WHERE parent_object_id = OBJECT_ID('dbo.Bonus') AND is_disabled = 0 AND is_not_trusted = 0;
            SELECT pc.name FROM sys.foreign_key_columns fc
            JOIN sys.columns pc ON pc.object_id = fc.parent_object_id AND pc.column_id = fc.parent_column_id
            WHERE fc.parent_object_id = OBJECT_ID('dbo.Bonus') AND fc.referenced_object_id = OBJECT_ID('dbo.Employee')
            ORDER BY fc.constraint_column_id;
            SELECT is_unique, filter_definition FROM sys.indexes
            WHERE object_id = OBJECT_ID('dbo.Bonus') AND name = 'UX_Bonus_Employee_Period';
            SELECT TYPE_NAME(system_type_id), precision, scale FROM sys.columns
            WHERE object_id = OBJECT_ID('dbo.Bonus') AND name = 'Value';
            SELECT COUNT(*) FROM sys.check_constraints
            WHERE parent_object_id = OBJECT_ID('dbo.Bonus') AND is_disabled = 0 AND is_not_trusted = 0;
            SELECT c.name FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID('dbo.Bonus') AND i.name = 'UX_Bonus_Employee_Period' AND ic.key_ordinal > 0
            ORDER BY ic.key_ordinal;
            """;
        await using var reader = await command.ExecuteReaderAsync(Token);
        Assert.True(await reader.ReadAsync(Token));
        Assert.Equal(4, reader.GetInt32(0));
        Assert.True(await reader.NextResultAsync(Token));
        Assert.True(await reader.ReadAsync(Token));
        Assert.Equal("DepartmentCode", reader.GetString(0));
        Assert.True(await reader.ReadAsync(Token));
        Assert.Equal("EmployeeNumber", reader.GetString(0));
        Assert.False(await reader.ReadAsync(Token));
        Assert.True(await reader.NextResultAsync(Token));
        Assert.True(await reader.ReadAsync(Token));
        Assert.True(reader.GetBoolean(0));
        Assert.Contains("0203", reader.GetString(1));
        Assert.Contains("IsDeleted", reader.GetString(1));
        Assert.True(await reader.NextResultAsync(Token));
        Assert.True(await reader.ReadAsync(Token));
        Assert.Equal("decimal", reader.GetString(0));
        Assert.Equal((byte)28, reader.GetByte(1));
        Assert.Equal((byte)14, reader.GetByte(2));
        Assert.True(await reader.NextResultAsync(Token));
        Assert.True(await reader.ReadAsync(Token));
        Assert.True(reader.GetInt32(0) >= 6);
        Assert.True(await reader.NextResultAsync(Token));
        foreach (var column in new[] { "DepartmentCode", "EmployeeNumber", "Period" })
        {
            Assert.True(await reader.ReadAsync(Token));
            Assert.Equal(column, reader.GetString(0));
        }
        Assert.False(await reader.ReadAsync(Token));
    }

    [Fact(Skip = SkipSql, SkipUnless = nameof(SqlServerConfigured))]
    public async Task Permite_otro_periodo_y_otro_empleado()
    {
        await using var db = Context();
        await using var transaction = await db.Database.BeginTransactionAsync(Token);
        db.Add(await Register(db, "prueba"));
        db.Add(await Register(db, "prueba", period: "2000-02"));
        db.Add(await Register(db, "prueba", employeeNumber: 102));
        await db.SaveChangesAsync(Token);
        Assert.Equal(3, await db.Set<Bonus>().CountAsync(x => x.CreatedBy == "prueba", Token));
    }

    [Fact(Skip = SkipSql, SkipUnless = nameof(SqlServerConfigured))]
    public async Task Conserva_el_redondeo_del_monto_fijo_al_persistir()
    {
        await using var db = Context();
        await using var transaction = await db.Database.BeginTransactionAsync(Token);
        var bonus = await Register(db, "prueba", 0.125m, type: BonusTypes.FixedAmount);
        db.Add(bonus);
        await db.SaveChangesAsync(Token);
        db.ChangeTracker.Clear();
        var saved = await db.Set<Bonus>().SingleAsync(x => x.BonusId == bonus.BonusId, Token);
        Assert.Equal(0.125m, saved.Value);
        Assert.Equal(0.13m, saved.GrossAmount);
        Assert.Equal(0.01m, saved.TaxAmount);
        Assert.Equal(0.12m, saved.NetAmount);
    }

    [Fact(Skip = SkipSql, SkipUnless = nameof(SqlServerConfigured))]
    public async Task Persiste_el_bono_y_oculta_el_eliminado_sin_borrarlo()
    {
        await using var db = Context();
        await using var transaction = await db.Database.BeginTransactionAsync(Token);
        var bonus = await Register(db, "prueba");
        db.Add(bonus);
        await db.SaveChangesAsync(Token);
        var id = bonus.BonusId;
        db.ChangeTracker.Clear();

        var saved = await db.Set<Bonus>().SingleAsync(x => x.BonusId == id, Token);
        Assert.Equal(10m, saved.Value);
        Assert.Equal(5000m, saved.MonthlySalary);
        Assert.Equal(8m, saved.TaxRate);
        Assert.Equal(500m, saved.GrossAmount);
        Assert.Equal(40m, saved.TaxAmount);
        Assert.Equal(460m, saved.NetAmount);
        Assert.Equal(BonusStates.Registered, saved.StateCode);
        Assert.Equal(Now, saved.CreatedAt);
        Assert.Equal(Now.Offset, saved.CreatedAt.Offset);
        Assert.Equal(8, saved.RowVersion.Length);
        saved.Delete("segundo", Now.AddMinutes(1));
        await db.SaveChangesAsync(Token);
        db.ChangeTracker.Clear();

        Assert.False(await db.Set<Bonus>().AnyAsync(x => x.BonusId == id, Token));
        var deleted = await db.Set<Bonus>().IgnoreQueryFilters().SingleAsync(x => x.BonusId == id, Token);
        Assert.True(deleted.IsDeleted);
        Assert.Equal("segundo", deleted.ModifiedBy);
        Assert.Equal(Now.AddMinutes(1), deleted.ModifiedAt);
        db.Add(await Register(db, "reemplazo"));
        await db.SaveChangesAsync(Token);
        Assert.Equal(2, await db.Set<Bonus>().IgnoreQueryFilters().CountAsync(x => x.Period == "2000-01", Token));
    }

    [Fact(Skip = SkipSql, SkipUnless = nameof(SqlServerConfigured))]
    public async Task Anulado_libera_el_periodo_y_pagado_sigue_ocupandolo()
    {
        await using var db = Context();
        await using var transaction = await db.Database.BeginTransactionAsync(Token);
        var cancelled = await Register(db, "prueba");
        db.Add(cancelled);
        await db.SaveChangesAsync(Token);
        cancelled.Cancel("segundo", Now.AddMinutes(1));
        await db.SaveChangesAsync(Token);
        var replacement = await Register(db, "reemplazo");
        db.Add(replacement);
        await db.SaveChangesAsync(Token);

        // Estado de prueba para verificar la regla, sin implementar la operación de pago.
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.Bonus SET StateCode = '0202', ModifiedBy = N'prueba', ModifiedAt = {Now.AddMinutes(2)} WHERE BonusId = {replacement.BonusId}", Token);
        db.ChangeTracker.Clear();
        var paid = await db.Set<Bonus>().SingleAsync(x => x.BonusId == replacement.BonusId, Token);
        Assert.Throws<BusinessException>(() => paid.Cancel("otro", Now.AddMinutes(3)));
        Assert.Throws<BusinessException>(() => paid.Delete("otro", Now.AddMinutes(3)));
        db.Add(await Register(db, "duplicado"));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Token));
        Assert.True(IsDuplicate(error));
    }

    [Fact(Skip = SkipSql, SkipUnless = nameof(SqlServerConfigured))]
    public async Task Rechaza_datos_inconsistentes_desde_sql()
    {
        await using var db = Context();
        await using var transaction = await db.Database.BeginTransactionAsync(Token);
        var bonus = await Register(db, "prueba");
        db.Add(bonus);
        await db.SaveChangesAsync(Token);
        string[] invalidChanges =
        [
            "DepartmentCode = 'VENTA'", "ReasonCode = '999999'", "StateCode = '0101'",
            "BonusTypeId = 3", "Period = '2026-13'", "Period = '0000-01'", "Period = '2026-1 '",
            "Value = 0", "Value = -1", "Value = 100.01", "MonthlySalary = 0", "TaxRate = -1",
            "GrossAmount = 499.99", "TaxAmount = 39.99", "NetAmount = 459.99", "CreatedBy = N' '",
            "ModifiedBy = N'prueba'", "ModifiedAt = CreatedAt", "StateCode = '0203'",
            "ModifiedBy = N'prueba', ModifiedAt = DATEADD(second, -1, CreatedAt)",
            "StateCode = '0203', IsDeleted = 1, ModifiedBy = N'prueba', ModifiedAt = CreatedAt"
        ];
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.Parameters.Add(new SqlParameter("@id", bonus.BonusId));
        foreach (var change in invalidChanges)
        {
            command.CommandText = "UPDATE dbo.Bonus SET " + change + " WHERE BonusId = @id";
            var error = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync(Token));
            Assert.Equal(547, error.Number);
        }
    }

    [Fact(Skip = SkipSql, SkipUnless = nameof(SqlServerConfigured))]
    public async Task Conserva_porcentaje_e_importes_en_el_limite_de_precision()
    {
        await using var db = Context();
        await using var transaction = await db.Database.BeginTransactionAsync(Token);
        await db.Database.ExecuteSqlRawAsync("UPDATE dbo.Employee SET MonthlySalary = 99999999999999.99 WHERE DepartmentCode = 'TECH1' AND EmployeeNumber = 101", Token);
        var bonus = await Register(db, "prueba", 50.00000000000001m);
        db.Add(bonus);
        await db.SaveChangesAsync(Token);
        db.ChangeTracker.Clear();
        var saved = await db.Set<Bonus>().SingleAsync(x => x.BonusId == bonus.BonusId, Token);
        Assert.Equal(50.00000000000001m, saved.Value);
        Assert.Equal(50000000000000.00m, saved.GrossAmount);
        Assert.Equal(4000000000000.00m, saved.TaxAmount);
        Assert.Equal(46000000000000.00m, saved.NetAmount);
    }

    [Fact(Skip = SkipSql, SkipUnless = nameof(SqlServerConfigured))]
    public async Task Dos_altas_simultaneas_solo_permiten_un_bono_vigente()
    {
        var user = "prueba-" + Guid.NewGuid().ToString("N");
        try
        {
            await using var first = Context();
            await using var second = Context();
            await first.Database.OpenConnectionAsync(Token);
            await second.Database.OpenConnectionAsync(Token);
            first.Add(await Register(first, user));
            second.Add(await Register(second, user));
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<bool> Save(AppDbContext db)
            {
                await start.Task;
                try { await db.SaveChangesAsync(Token); return true; }
                catch (DbUpdateException error) when (IsDuplicate(error)) { return false; }
            }
            var attempts = new[] { Save(first), Save(second) };
            start.SetResult();
            var results = await Task.WhenAll(attempts);
            Assert.Single(results, x => x);
            Assert.Single(results, x => !x);
            await using var verification = Context();
            Assert.Equal(1, await verification.Set<Bonus>().CountAsync(x => x.CreatedBy == user, Token));
        }
        finally
        {
            await using var cleanup = Context();
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Bonus WHERE CreatedBy = {user}", CancellationToken.None);
        }
    }

    [Fact(Skip = SkipSql, SkipUnless = nameof(SqlServerConfigured))]
    public async Task Una_version_antigua_no_puede_sobrescribir_la_anulacion()
    {
        var user = "prueba-" + Guid.NewGuid().ToString("N");
        try
        {
            await using var first = Context();
            var bonus = await Register(first, user);
            first.Add(bonus);
            await first.SaveChangesAsync(Token);
            await using var second = Context();
            var stale = await second.Set<Bonus>().SingleAsync(x => x.BonusId == bonus.BonusId, Token);
            bonus.Cancel("primero", Now.AddMinutes(1));
            await first.SaveChangesAsync(Token);
            stale.Delete("segundo", Now.AddMinutes(2));
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Token));
            await using var verification = Context();
            var saved = await verification.Set<Bonus>().SingleAsync(x => x.BonusId == bonus.BonusId, Token);
            Assert.Equal(BonusStates.Cancelled, saved.StateCode);
            Assert.False(saved.IsDeleted);
            Assert.Equal("primero", saved.ModifiedBy);
        }
        finally
        {
            await using var cleanup = Context();
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Bonus WHERE CreatedBy = {user}", CancellationToken.None);
        }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer(Environment.GetEnvironmentVariable("ConnectionStrings__BonusTest")).Options);

    private static async Task<Bonus> Register(AppDbContext db, string user, decimal value = 10m,
        int type = BonusTypes.SalaryPercentage, string period = "2000-01", int employeeNumber = 101)
    {
        var employee = await db.Employees.AsNoTracking().SingleAsync(x => x.DepartmentCode == "TECH1" && x.EmployeeNumber == employeeNumber, Token);
        var reason = await db.BonusReasons.AsNoTracking().SingleAsync(x => x.ReasonCode == "000001", Token);
        return Bonus.Register(employee, period, type, value, reason, user, Now);
    }

    private static bool IsDuplicate(DbUpdateException error) =>
        error.InnerException is SqlException { Number: 2601 or 2627 };
}
