using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BonusTest.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BonusTest.Tests;

[Collection("SQL Server")]
public class BonusApiTests : IAsyncLifetime
{
    private const string SkipApi = "Defina BONUS_API_URL y ConnectionStrings__BonusTest para probar la API con SQL Server.";
    private readonly string user = "api-" + Guid.NewGuid().ToString("N");
    private readonly HttpClient client = new();
    public static bool ApiConfigured => BonusPersistenceTests.SqlServerConfigured &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BONUS_API_URL"));
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        if (ApiConfigured)
            client.BaseAddress = new Uri(Environment.GetEnvironmentVariable("BONUS_API_URL")!);
        return ValueTask.CompletedTask;
    }

    [Fact(Skip = SkipApi, SkipUnless = nameof(ApiConfigured))]
    public async Task Busca_empleado_por_clave_compuesta_y_rechaza_cesados_o_inexistentes()
    {
        using var response = await client.GetAsync("api/v1/Bonus/employee/TECH1/101", Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var employee = await Json(response);
        Assert.Equal(101, employee.GetProperty("employeeNumber").GetInt32());
        Assert.Equal("TECH1", employee.GetProperty("departmentCode").GetString());
        Assert.Equal(5000m, employee.GetProperty("monthlySalary").GetDecimal());
        Assert.Equal(8m, employee.GetProperty("taxRate").GetDecimal());
        Assert.False(string.IsNullOrWhiteSpace(employee.GetProperty("fullName").GetString()));
        Assert.True(employee.TryGetProperty("hireDate", out _));
        foreach (var path in new[] { "VENTA/202", "VENTA/101", "TECH1/999" })
        {
            using var invalid = await client.GetAsync("api/v1/Bonus/employee/" + path, Token);
            await Conflict(invalid, "EMPLOYEE_NOT_ACTIVE");
        }
    }

    [Fact(Skip = SkipApi, SkipUnless = nameof(ApiConfigured))]
    public async Task Registra_anula_y_elimina_con_auditoria_e_importes_persistidos()
    {
        var before = DateTimeOffset.UtcNow;
        using var response = await Send(HttpMethod.Post, "api/v1/Bonus", Request());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = await Json(response);
        var id = data.GetProperty("bonusId").GetInt32();
        Assert.Equal(500m, data.GetProperty("grossAmount").GetDecimal());
        Assert.Equal(40m, data.GetProperty("taxAmount").GetDecimal());
        Assert.Equal(460m, data.GetProperty("netAmount").GetDecimal());
        await using var db = Context();
        var saved = await db.Bonuses.AsNoTracking().SingleAsync(x => x.BonusId == id, Token);
        Assert.Equal(user, saved.CreatedBy);
        Assert.InRange(saved.CreatedAt, before, DateTimeOffset.UtcNow);
        Assert.Equal("0201", saved.StateCode);
        Assert.Null(saved.ModifiedBy);
        using var cancel = await Send(HttpMethod.Put, $"api/v1/Bonus/{id}/cancel", operatorCode: user + "-c");
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
        saved = await db.Bonuses.AsNoTracking().SingleAsync(x => x.BonusId == id, Token);
        Assert.Equal("0203", saved.StateCode);
        Assert.Equal(user + "-c", saved.ModifiedBy);
        Assert.Equal(user, saved.CreatedBy);
        Assert.True(saved.ModifiedAt >= saved.CreatedAt);
        using var forbidden = await Send(HttpMethod.Delete, $"api/v1/Bonus/{id}");
        await Conflict(forbidden, "BONUS_NOT_REGISTERED");

        using var replacement = await Send(HttpMethod.Post, "api/v1/Bonus", Request());
        Assert.Equal(HttpStatusCode.Created, replacement.StatusCode);
        var secondId = (await Json(replacement)).GetProperty("bonusId").GetInt32();
        using var delete = await Send(HttpMethod.Delete, $"api/v1/Bonus/{secondId}", operatorCode: user + "-d");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.False(await db.Bonuses.AnyAsync(x => x.BonusId == secondId, Token));
        var deleted = await db.Bonuses.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.BonusId == secondId, Token);
        Assert.True(deleted.IsDeleted);
        Assert.Equal(user + "-d", deleted.ModifiedBy);
        Assert.Equal("0201", deleted.StateCode);
    }

    private static object Request(string period = "1950-01", int type = 2, decimal value = 10m,
        string department = "TECH1", int number = 101, string reason = "000001") =>
        new { departmentCode = department, employeeNumber = number, period, bonusTypeId = type, value, reasonCode = reason };

    [Theory(Skip = SkipApi, SkipUnless = nameof(ApiConfigured))]
    [InlineData("1951-01", 2, 0, "TECH1", 101, "000001", "INVALID_BONUS_VALUE")]
    [InlineData("1951-01", 2, -1, "TECH1", 101, "000001", "INVALID_BONUS_VALUE")]
    [InlineData("1951-01", 2, 100.01, "TECH1", 101, "000001", "INVALID_BONUS_VALUE")]
    [InlineData("1951-01", 1, 5000.01, "TECH1", 101, "000001", "INVALID_BONUS_VALUE")]
    [InlineData("1951-01", 3, 10, "TECH1", 101, "000001", "INVALID_BONUS_TYPE")]
    [InlineData("1951-01", 2, 10, "VENTA", 202, "000001", "EMPLOYEE_NOT_ACTIVE")]
    [InlineData("1951-01", 2, 10, "VENTA", 101, "000001", "EMPLOYEE_NOT_ACTIVE")]
    [InlineData("1951-01", 2, 10, "TECH1", 101, "000004", "REASON_NOT_ACTIVE")]
    [InlineData("1951-01", 2, 10, "TECH1", 101, "999999", "REASON_NOT_ACTIVE")]
    [InlineData("1951-1", 2, 10, "TECH1", 101, "000001", "INVALID_PERIOD")]
    [InlineData("1951-13", 2, 10, "TECH1", 101, "000001", "INVALID_PERIOD")]
    [InlineData("0000-01", 2, 10, "TECH1", 101, "000001", "INVALID_PERIOD")]
    [InlineData("9999-12", 2, 10, "TECH1", 101, "000001", "INVALID_PERIOD")]
    public async Task Rechaza_registros_invalidos_sin_persistir(string period, int type, decimal value,
        string department, int number, string reason, string code)
    {
        using var response = await Send(HttpMethod.Post, "api/v1/Bonus", Request(period, type, value, department, number, reason));
        await Conflict(response, code);
        await using var db = Context();
        Assert.False(await db.Bonuses.IgnoreQueryFilters().AnyAsync(x => x.CreatedBy == user, Token));
    }

    [Fact(Skip = SkipApi, SkipUnless = nameof(ApiConfigured))]
    public async Task Acepta_limites_y_redondea_monto_fijo_desde_http()
    {
        foreach (var (period, type, value, gross, tax, net) in new[]
        {
            ("1952-01", 1, 0.125m, 0.13m, 0.01m, 0.12m),
            ("1952-02", 1, 5000m, 5000m, 400m, 4600m),
            ("1952-03", 2, 100m, 5000m, 400m, 4600m)
        })
        {
            using var response = await Send(HttpMethod.Post, "api/v1/Bonus", Request(period, type, value));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var data = await Json(response);
            Assert.Equal(gross, data.GetProperty("grossAmount").GetDecimal());
            Assert.Equal(tax, data.GetProperty("taxAmount").GetDecimal());
            Assert.Equal(net, data.GetProperty("netAmount").GetDecimal());
            var id = data.GetProperty("bonusId").GetInt32();
            await using var db = Context();
            var saved = await db.Bonuses.AsNoTracking().SingleAsync(x => x.BonusId == id, Token);
            Assert.Equal(value, saved.Value);
            Assert.Equal(gross, saved.GrossAmount);
            Assert.Equal(tax, saved.TaxAmount);
            Assert.Equal(net, saved.NetAmount);
        }
    }

    [Fact(Skip = SkipApi, SkipUnless = nameof(ApiConfigured))]
    public async Task Header_es_obligatorio_y_el_cliente_no_puede_asignar_campos_internos()
    {
        using var missing = await client.PostAsJsonAsync("api/v1/Bonus", Request(), Token);
        await Conflict(missing, "INVALID_USER");
        using var longUser = await Send(HttpMethod.Post, "api/v1/Bonus", Request(), new string('x', 51));
        await Conflict(longUser, "INVALID_USER");
        foreach (var field in new[] { "stateCode", "grossAmount", "monthlySalary", "taxRate", "taxAmount", "netAmount",
            "createdAt", "createdBy", "modifiedAt", "modifiedBy", "userCode", "isDeleted", "bonusId", "rowVersion" })
        {
            var body = JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(Request()))!;
            body[field] = "alterado";
            using var response = await Send(HttpMethod.Post, "api/v1/Bonus", body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.DoesNotContain("BonusTest.", await response.Content.ReadAsStringAsync(Token));
        }
        using var precision = await Send(HttpMethod.Post, "api/v1/Bonus", Request(value: 0.000000000000001m));
        await Conflict(precision, "INVALID_BONUS_PRECISION");
    }

    [Fact(Skip = SkipApi, SkipUnless = nameof(ApiConfigured))]
    public async Task Listado_filtra_y_pagina_con_descripciones_y_orden_estable()
    {
        var ids = new List<int>();
        foreach (var (department, number) in new[] { ("TECH1", 101), ("TECH1", 102), ("TECH1", 103), ("VENTA", 201) })
        {
            using var created = await Send(HttpMethod.Post, "api/v1/Bonus", Request("1953-01", department: department, number: number));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            ids.Add((await Json(created)).GetProperty("bonusId").GetInt32());
        }
        using var cancelled = await Send(HttpMethod.Put, $"api/v1/Bonus/{ids[1]}/cancel");
        Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        using var deleted = await Send(HttpMethod.Delete, $"api/v1/Bonus/{ids[2]}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var first = await client.GetAsync("api/v1/Bonus?period=1953-01&pageSize=2", Token);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var page = await Json(first);
        Assert.Equal(3, page.GetProperty("totalRecords").GetInt32());
        Assert.Equal(2, page.GetProperty("totalPages").GetInt32());
        Assert.Equal(1, page.GetProperty("pageNumber").GetInt32());
        Assert.Equal(2, page.GetProperty("pageSize").GetInt32());
        var rows = page.GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal(new[] { ids[3], ids[1] }, rows.Select(x => x.GetProperty("bonusId").GetInt32()));
        foreach (var name in new[] { "stateDescription", "bonusTypeDescription", "reasonDescription", "fullName", "departmentName" })
            Assert.False(string.IsNullOrWhiteSpace(rows[0].GetProperty(name).GetString()));
        using var second = await client.GetAsync("api/v1/Bonus?period=1953-01&pageSize=2&pageIndex=2", Token);
        var last = await Json(second);
        Assert.Equal(ids[0], Assert.Single(last.GetProperty("data").EnumerateArray()).GetProperty("bonusId").GetInt32());
        foreach (var (filter, count) in new[]
        {
            ("departmentCode=TECH1", 2), ("employeeNumber=101", 1), ("stateCode=0201", 2),
            ("departmentCode=TECH1&employeeNumber=102&stateCode=0203", 1),
            ("departmentCode=VENTA&employeeNumber=101", 0)
        })
        {
            using var filtered = await client.GetAsync("api/v1/Bonus?period=1953-01&" + filter, Token);
            Assert.Equal(count, (await Json(filtered)).GetProperty("totalRecords").GetInt32());
        }
        using var beyond = await client.GetAsync("api/v1/Bonus?period=1953-01&pageIndex=2147483647&pageSize=50", Token);
        Assert.Equal(HttpStatusCode.OK, beyond.StatusCode);
        Assert.Empty((await Json(beyond)).GetProperty("data").EnumerateArray());
        using var normalized = await client.GetAsync("api/v1/Bonus?period=1953-01&pageIndex=0&pageSize=0", Token);
        var defaults = await Json(normalized);
        Assert.Equal(1, defaults.GetProperty("pageNumber").GetInt32());
        Assert.Equal(10, defaults.GetProperty("pageSize").GetInt32());
        using var maximum = await client.GetAsync("api/v1/Bonus?period=1953-01&pageSize=999", Token);
        Assert.Equal(50, (await Json(maximum)).GetProperty("pageSize").GetInt32());
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null, string? operatorCode = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("UserCode", operatorCode ?? user);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request, Token);
    }

    [Fact(Skip = SkipApi, SkipUnless = nameof(ApiConfigured))]
    public async Task Altas_simultaneas_devuelven_un_exito_y_conflictos_sin_duplicados()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> Register()
        {
            await start.Task;
            return await Send(HttpMethod.Post, "api/v1/Bonus", Request("1954-01"));
        }
        var pending = Enumerable.Range(0, 6).Select(_ => Register()).ToArray();
        start.SetResult();
        var results = await Task.WhenAll(pending);
        try
        {
            Assert.Single(results, x => x.StatusCode == HttpStatusCode.Created);
            foreach (var conflict in results.Where(x => x.StatusCode != HttpStatusCode.Created))
                await Conflict(conflict, "BONUS_DUPLICATE");
            await using var db = Context();
            Assert.Equal(1, await db.Bonuses.CountAsync(x => x.CreatedBy == user, Token));
        }
        finally { foreach (var result in results) result.Dispose(); }
    }

    [Fact(Skip = SkipApi, SkipUnless = nameof(ApiConfigured))]
    public async Task Anular_y_eliminar_simultaneamente_no_sobrescribe_el_estado_ganador()
    {
        using var created = await Send(HttpMethod.Post, "api/v1/Bonus", Request("1954-02"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await Json(created)).GetProperty("bonusId").GetInt32();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> Change(HttpMethod method, string path)
        {
            await start.Task;
            return await Send(method, path);
        }
        var pending = new[] { Change(HttpMethod.Put, $"api/v1/Bonus/{id}/cancel"), Change(HttpMethod.Delete, $"api/v1/Bonus/{id}") };
        start.SetResult();
        var results = await Task.WhenAll(pending);
        try
        {
            Assert.Single(results, x => x.StatusCode == HttpStatusCode.NoContent);
            Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
            var code = (await Json(results.Single(x => x.StatusCode == HttpStatusCode.Conflict))).GetProperty("code").GetString();
            Assert.Contains(code, new[] { "BONUS_CONFLICT", "BONUS_NOT_REGISTERED", "BONUS_NOT_FOUND" });
            await using var db = Context();
            var saved = await db.Bonuses.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.BonusId == id, Token);
            Assert.True(saved.IsDeleted ^ (saved.StateCode == "0203"));
            Assert.Equal(user, saved.ModifiedBy);
        }
        finally { foreach (var result in results) result.Dispose(); }
    }

    [Fact(Skip = SkipApi, SkipUnless = nameof(ApiConfigured))]
    public async Task Pagado_impide_duplicado_anulacion_y_eliminacion_sin_endpoint_de_pago()
    {
        using var created = await Send(HttpMethod.Post, "api/v1/Bonus", Request("1955-01"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await Json(created)).GetProperty("bonusId").GetInt32();
        await using var db = Context();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.Bonus SET StateCode = '0202', ModifiedBy = {user}, ModifiedAt = SYSDATETIMEOFFSET() WHERE BonusId = {id}", Token);
        using var cancel = await Send(HttpMethod.Put, $"api/v1/Bonus/{id}/cancel");
        await Conflict(cancel, "BONUS_NOT_REGISTERED");
        using var delete = await Send(HttpMethod.Delete, $"api/v1/Bonus/{id}");
        await Conflict(delete, "BONUS_NOT_REGISTERED");
        using var duplicate = await Send(HttpMethod.Post, "api/v1/Bonus", Request("1955-01"));
        await Conflict(duplicate, "BONUS_DUPLICATE");
        using var list = await client.GetAsync("api/v1/Bonus?period=1955-01&stateCode=0202", Token);
        Assert.Equal(id, Assert.Single((await Json(list)).GetProperty("data").EnumerateArray()).GetProperty("bonusId").GetInt32());
    }

    [Fact(Skip = SkipApi, SkipUnless = nameof(ApiConfigured))]
    public async Task Operaciones_invalidas_no_alteran_auditoria_y_borrado_libera_periodo()
    {
        foreach (var method in new[] { HttpMethod.Put, HttpMethod.Delete })
        {
            var path = "api/v1/Bonus/2147483647" + (method == HttpMethod.Put ? "/cancel" : "");
            using var missing = await Send(method, path);
            await Conflict(missing, "BONUS_NOT_FOUND");
        }
        using var created = await Send(HttpMethod.Post, "api/v1/Bonus", Request("1955-02"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await Json(created)).GetProperty("bonusId").GetInt32();
        foreach (var method in new[] { HttpMethod.Put, HttpMethod.Delete })
        {
            var path = $"api/v1/Bonus/{id}" + (method == HttpMethod.Put ? "/cancel" : "");
            using var invalid = await Send(method, path, operatorCode: new string('x', 51));
            await Conflict(invalid, "INVALID_USER");
        }
        await using var db = Context();
        var saved = await db.Bonuses.AsNoTracking().SingleAsync(x => x.BonusId == id, Token);
        Assert.Null(saved.ModifiedAt);
        Assert.Null(saved.ModifiedBy);
        using var deleted = await Send(HttpMethod.Delete, $"api/v1/Bonus/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var repeated = await Send(HttpMethod.Delete, $"api/v1/Bonus/{id}");
        await Conflict(repeated, "BONUS_NOT_FOUND");
        using var replacement = await Send(HttpMethod.Post, "api/v1/Bonus", Request("1955-02"));
        Assert.Equal(HttpStatusCode.Created, replacement.StatusCode);
    }

    [Fact(Skip = SkipApi, SkipUnless = nameof(ApiConfigured))]
    public async Task Cuerpo_o_parametros_no_convertibles_responden_400()
    {
        using var invalidBody = await client.PostAsync("api/v1/Bonus", new StringContent("{", System.Text.Encoding.UTF8, "application/json"), Token);
        Assert.Equal(HttpStatusCode.BadRequest, invalidBody.StatusCode);
        using var invalidQuery = await client.GetAsync("api/v1/Bonus?employeeNumber=texto", Token);
        Assert.Equal(HttpStatusCode.BadRequest, invalidQuery.StatusCode);
        using var nullBody = await client.PostAsJsonAsync<object?>("api/v1/Bonus", null, Token);
        Assert.Equal(HttpStatusCode.BadRequest, nullBody.StatusCode);
    }

    [Fact(Skip = SkipApi, SkipUnless = nameof(ApiConfigured))]
    public async Task Periodo_actual_se_acepta_y_mes_siguiente_se_rechaza_con_el_reloj_del_servidor()
    {
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Lima"));
        var current = now.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        var future = now.AddMonths(1).ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        using var accepted = await Send(HttpMethod.Post, "api/v1/Bonus", Request(current, number: 102));
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        using var rejected = await Send(HttpMethod.Post, "api/v1/Bonus", Request(future, number: 102));
        await Conflict(rejected, "INVALID_PERIOD");
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Token);

    private static async Task Conflict(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var data = await Json(response);
        Assert.Equal(code, data.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("description").GetString()));
        Assert.Equal(2, data.EnumerateObject().Count());
    }

    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer(Environment.GetEnvironmentVariable("ConnectionStrings__BonusTest")).Options);

    [Fact(Skip = SkipApi, SkipUnless = nameof(ApiConfigured))]
    public async Task Swagger_describe_las_cinco_operaciones_y_sus_respuestas()
    {
        using var response = await client.GetAsync("swagger/v1/swagger.json", Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var paths = (await Json(response)).GetProperty("paths");
        Assert.Equal("201", Assert.Single(paths.GetProperty("/api/v1/Bonus").GetProperty("post").GetProperty("responses").EnumerateObject()).Name);
        foreach (var (path, method, code) in new[]
        {
            ("/api/v1/Bonus", "get", "200"),
            ("/api/v1/Bonus/employee/{departmentCode}/{employeeNumber}", "get", "200"),
            ("/api/v1/Bonus/{bonusId}/cancel", "put", "204"),
            ("/api/v1/Bonus/{bonusId}", "delete", "204")
        })
            Assert.True(paths.GetProperty(path).GetProperty(method).GetProperty("responses").TryGetProperty(code, out _));
        Assert.DoesNotContain(paths.EnumerateObject(), path => path.Name.EndsWith("/pay", StringComparison.Ordinal));
        var list = paths.GetProperty("/api/v1/Bonus").GetProperty("get").GetProperty("responses").GetProperty("200");
        Assert.True(list.GetProperty("content").GetProperty("application/json").TryGetProperty("schema", out _));
    }

    public async ValueTask DisposeAsync()
    {
        client.Dispose();
        if (!ApiConfigured) return;
        await using var db = Context();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Bonus WHERE CreatedBy = {user}", CancellationToken.None);
    }
}
