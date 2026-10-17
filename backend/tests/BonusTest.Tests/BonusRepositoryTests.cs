using System.Data.Common;
using BonusTest.Application.Bonuses;
using BonusTest.Domain.Entities;
using BonusTest.Domain.Exceptions;
using BonusTest.Infrastructure.Persistence;
using BonusTest.Infrastructure.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BonusTest.Tests;

[Collection("SQL Server")]
public class BonusRepositoryTests
{
    private const string SkipSql = "Defina ConnectionStrings__BonusTest para ejecutar las pruebas SQL.";
    public static bool SqlServerConfigured => BonusPersistenceTests.SqlServerConfigured;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact(Skip = SkipSql, SkipUnless = nameof(SqlServerConfigured))]
    public async Task Listado_ejecuta_count_y_offset_fetch_con_filtros_en_sql_server()
    {
        var capture = new CaptureSql();
        await using var db = Context(capture);
        await using var transaction = await db.Database.BeginTransactionAsync(Token);
        var employee = await db.Employees.AsNoTracking().SingleAsync(x => x.DepartmentCode == "TECH1" && x.EmployeeNumber == 101, Token);
        var reason = await db.BonusReasons.AsNoTracking().SingleAsync(x => x.ReasonCode == "000001", Token);
        var bonuses = Enumerable.Range(0, 3).Select(_ =>
        {
            var bonus = Bonus.Register(employee, "1940-01", 2, 10m, reason, "consulta", DateTimeOffset.Now);
            bonus.Cancel("consulta", DateTimeOffset.Now);
            return bonus;
        }).ToArray();
        db.Bonuses.AddRange(bonuses);
        await db.SaveChangesAsync(Token);
        capture.Commands.Clear();

        var page = await new BonusRepository(db).ListAsync(new BonusFilter
        {
            DepartmentCode = "TECH1", EmployeeNumber = 101, Period = "1940-01", StateCode = "0203", PageIndex = 2, PageSize = 1
        }, Token);
        Assert.Equal(3, page.TotalRecords);
        Assert.Single(page.Data);
        Assert.Equal(bonuses[1].BonusId, page.Data[0].BonusId);
        Assert.Contains("COUNT(*)", capture.Commands[0]);
        var sql = capture.Commands[1];
        Assert.Contains("ORDER BY [b].[BonusId] DESC", sql);
        Assert.Contains("OFFSET", sql);
        Assert.Contains("FETCH NEXT", sql);
        Assert.Contains("[b].[DepartmentCode] =", sql);
        Assert.Contains("[b].[EmployeeNumber] =", sql);
        Assert.Contains("[b].[StateCode] =", sql);
        Assert.Contains("[b].[Period] =", sql);
        Assert.Contains("[b].[IsDeleted]", sql);
        Assert.Contains("JOIN [Employee]", sql);
        Assert.Contains("JOIN [Department]", sql);
        Assert.Contains("JOIN [BonusReason]", sql);
        Assert.Contains("JOIN [BonusType]", sql);
        Assert.Contains("JOIN [State]", sql);
    }

    [Fact(Skip = SkipSql, SkipUnless = nameof(SqlServerConfigured))]
    public async Task Repositorio_traduce_version_antigua_a_conflicto_sin_sobrescribir()
    {
        var user = "repo-" + Guid.NewGuid().ToString("N");
        try
        {
            await using var first = Context();
            var repository = new BonusRepository(first);
            var bonus = await repository.RegisterAsync(new RegisterBonusRequest("TECH1", 102, "1941-01", 2, 10m, "000001"), user, DateTimeOffset.Now, Token);
            await using var second = Context();
            var staleRepository = new BonusRepository(second);
            var stale = await staleRepository.FindAsync(bonus.BonusId, Token);
            Assert.NotNull(stale);
            bonus.Cancel(user, DateTimeOffset.Now);
            await repository.SaveAsync(Token);
            stale.Delete(user, DateTimeOffset.Now);
            var conflict = await Assert.ThrowsAsync<BusinessException>(() => staleRepository.SaveAsync(Token));
            Assert.Equal("BONUS_CONFLICT", conflict.Code);
            await using var verify = Context();
            var saved = await verify.Bonuses.SingleAsync(x => x.BonusId == bonus.BonusId, Token);
            Assert.Equal("0203", saved.StateCode);
            Assert.False(saved.IsDeleted);
        }
        finally { await Cleanup(user); }
    }

    [Fact(Skip = SkipSql, SkipUnless = nameof(SqlServerConfigured))]
    public async Task Registro_conserva_empleado_y_motivo_hasta_confirmar_la_transaccion()
    {
        var user = "repo-" + Guid.NewGuid().ToString("N");
        var pause = new PauseSave();
        await using var db = Context(pause);
        var registration = new BonusRepository(db).RegisterAsync(
            new RegisterBonusRequest("TECH1", 103, "1942-01", 2, 10m, "000001"), user, DateTimeOffset.Now, Token);
        try
        {
            await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
            await using var competing = Context();
            await using var transaction = await competing.Database.BeginTransactionAsync(Token);
            foreach (var sql in new[]
            {
                "SET LOCK_TIMEOUT 200; UPDATE dbo.Employee SET MonthlySalary = MonthlySalary WHERE DepartmentCode = 'TECH1' AND EmployeeNumber = 103",
                "SET LOCK_TIMEOUT 200; UPDATE dbo.BonusReason SET State = State WHERE ReasonCode = '000001'"
            })
            {
                var blocked = await Assert.ThrowsAsync<SqlException>(() => competing.Database.ExecuteSqlRawAsync(sql, Token));
                Assert.Equal(1222, blocked.Number);
            }
            await competing.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT -1", Token);
        }
        finally
        {
            pause.Resume.TrySetResult();
            try { await registration; }
            finally { await Cleanup(user); }
        }
    }

    private static AppDbContext Context(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer(Environment.GetEnvironmentVariable("ConnectionStrings__BonusTest"))
        .AddInterceptors(interceptors).Options);

    [Theory(Skip = SkipSql, SkipUnless = nameof(SqlServerConfigured))]
    [InlineData("2001-01-01T04:59:59+00:00", false)]
    [InlineData("2001-01-01T05:00:00+00:00", true)]
    public async Task Servicio_usa_el_mes_de_Lima_aunque_el_sistema_este_en_UTC(string instant, bool accepted)
    {
        var user = "reloj-" + Guid.NewGuid().ToString("N");
        try
        {
            await using var db = Context();
            var repository = new BonusRepository(db);
            var service = new BonusService(repository, new UtcClock(DateTimeOffset.Parse(instant)));
            var request = new RegisterBonusRequest("TECH1", 101, "2001-01", 2, 10m, "000001");
            if (!accepted)
            {
                var error = await Assert.ThrowsAsync<BusinessException>(() => service.RegisterAsync(request, user, Token));
                Assert.Equal("INVALID_PERIOD", error.Code);
            }
            else
            {
                var result = await service.RegisterAsync(request, user, Token);
                var saved = await repository.FindAsync(result.BonusId, Token);
                Assert.NotNull(saved);
                Assert.Equal(TimeSpan.FromHours(-5), saved.CreatedAt.Offset);
                Assert.Equal(500m, result.GrossAmount);
            }
        }
        finally { await Cleanup(user); }
    }

    private sealed class UtcClock(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    [Fact(Skip = SkipSql, SkipUnless = nameof(SqlServerConfigured))]
    public async Task Error_despues_de_guardar_revierte_pago_detalle_y_auditoria()
    {
        var user = "rollback-" + Guid.NewGuid().ToString("N");
        try
        {
            int id;
            await using (var setup = Context())
                id = (await new BonusRepository(setup).RegisterAsync(new RegisterBonusRequest("TECH1", 101, "1961-01", 2, 10m, "000001"), user, DateTimeOffset.Now, Token)).BonusId;
            await using (var failing = Context(new FailAfterSave()))
                await Assert.ThrowsAsync<InvalidOperationException>(() => new BonusRepository(failing).PayAsync(id, user, DateTimeOffset.Now, Token));
            await using var verify = Context();
            var saved = await verify.Bonuses.AsNoTracking().SingleAsync(x => x.BonusId == id, Token);
            Assert.Equal("0201", saved.StateCode);
            Assert.Null(saved.ModifiedBy);
            Assert.Null(saved.ModifiedAt);
            Assert.False(await verify.BonusAllocations.AnyAsync(x => x.BonusId == id, Token));
        }
        finally { await Cleanup(user); }
    }

    [Fact(Skip = SkipSql, SkipUnless = nameof(SqlServerConfigured))]
    public async Task Pago_con_version_antigua_no_deja_detalle_ni_sobrescribe_anulacion()
    {
        var user = "pago-" + Guid.NewGuid().ToString("N");
        try
        {
            await using var db = Context();
            var repo = new BonusRepository(db);
            var bonus = await repo.RegisterAsync(new RegisterBonusRequest("TECH1", 101, "1961-02", 2, 10m, "000001"), user, DateTimeOffset.Now, Token);
            await using var stale = Context();
            await new BonusRepository(stale).FindAsync(bonus.BonusId, Token);
            bonus.Cancel(user, DateTimeOffset.Now);
            await repo.SaveAsync(Token);
            var error = await Assert.ThrowsAsync<BusinessException>(() => new BonusRepository(stale).PayAsync(bonus.BonusId, user, DateTimeOffset.Now, Token));
            Assert.Equal("BONUS_CONFLICT", error.Code);
            await using var verify = Context();
            Assert.Equal("0203", (await verify.Bonuses.SingleAsync(x => x.BonusId == bonus.BonusId, Token)).StateCode);
            Assert.False(await verify.BonusAllocations.AnyAsync(x => x.BonusId == bonus.BonusId, Token));
        }
        finally { await Cleanup(user); }
    }

    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            var db = (AppDbContext)eventData.Context!;
            var id = db.ChangeTracker.Entries<Bonus>().Single().Entity.BonusId;
            Assert.Equal("0202", await db.Bonuses.AsNoTracking().Where(x => x.BonusId == id).Select(x => x.StateCode).SingleAsync(cancellationToken));
            Assert.Equal(2, await db.BonusAllocations.CountAsync(x => x.BonusId == id, cancellationToken));
            throw new InvalidOperationException("Fallo provocado antes de confirmar la transacción.");
        }
    }

    private static async Task Cleanup(string user)
    {
        await using var db = Context();
        await db.BonusAllocations.Where(x => db.Bonuses.IgnoreQueryFilters().Any(b => b.BonusId == x.BonusId && b.CreatedBy == user)).ExecuteDeleteAsync(CancellationToken.None);
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Bonus WHERE CreatedBy = {user}", CancellationToken.None);
    }

    private sealed class CaptureSql : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class PauseSave : SaveChangesInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Resume.Task.WaitAsync(cancellationToken);
            return result;
        }
    }
}
