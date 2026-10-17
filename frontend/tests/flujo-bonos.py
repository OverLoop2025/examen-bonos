import asyncio
import json
import os
import subprocess
import sys
import tempfile
import uuid
from pathlib import Path
from urllib.parse import urlsplit
from playwright.async_api import async_playwright, expect

url = os.getenv('BASE_URL', 'http://127.0.0.1:4200').rstrip('/')
operator = 'web-' + uuid.uuid4().hex
snapshot_file = Path(tempfile.gettempdir()) / ('bonos-' + os.getenv('COMPOSE_PROJECT_NAME', 'local') + '.json')


def sql(query):
    return subprocess.check_output(['docker', 'compose', 'exec', '-T', 'db',
        '/opt/mssql-tools18/bin/sqlcmd', '-S', 'localhost', '-U', 'sa', '-C', '-b', '-I',
        '-d', 'BonusTest', '-f65001', '-y0', '-w65535', '-Q', 'SET NOCOUNT ON; ' + query], text=True).strip()


def seed():
    return [json.loads(''.join(sql(f'SELECT * FROM dbo.{table} ORDER BY {order} FOR JSON PATH;').splitlines()))
        for table, order in [('State', 'StateCode'), ('Department', 'DepartmentCode'),
            ('Employee', 'DepartmentCode,EmployeeNumber'), ('EmployeeProjectHours', 'EmployeeProjectHoursId'),
            ('BonusType', 'BonusTypeId'), ('BonusReason', 'ReasonCode')]]


def check_seed():
    assert seed() == json.loads(snapshot_file.read_text()), 'Los datos iniciales cambiaron.'
    assert sql('SELECT COUNT(*) FROM dbo.Bonus;') == '0'
    assert sql('SELECT COUNT(*) FROM dbo.BonusAllocation;') == '0'
    assert not sql('DBCC CHECKCONSTRAINTS WITH ALL_CONSTRAINTS, NO_INFOMSGS;')


async def main():
    assert sql('SELECT COUNT(*) FROM dbo.Bonus;') == '0', 'Usa una base dedicada sin bonos personales.'
    original = seed()
    assert [len(table) for table in original] == [5, 2, 5, 10, 2, 4]
    snapshot_file.write_text(json.dumps(original))
    async with async_playwright() as p:
        browser = await p.chromium.launch()
        page = await browser.new_page(viewport={'width': 1440, 'height': 1000})
        errors, requests = [], []
        page.on('pageerror', lambda e: errors.append(str(e)))
        page.on('request', lambda r: requests.append(r.url) if '/api/v1/' in r.url else None)

        async def identify(route):
            assert route.request.headers.get('usercode') == 'candidato'
            await route.continue_(headers={**route.request.headers, 'usercode': operator})
        await page.route('**/api/v1/**', identify)

        async def register(period, fixed=False, status=201):
            await page.get_by_role('button', name='Nuevo registro').click()
            d = page.get_by_role('dialog')
            await d.get_by_label('Área del empleado').select_option('TECH1')
            await d.get_by_label('Número del empleado', exact=True).fill('101')
            await d.get_by_role('button', name='Buscar empleado', exact=True).click()
            await expect(d.get_by_text('Andrea Torres Vega', exact=True)).to_be_visible()
            await d.get_by_label('Periodo del bono').fill(period)
            await d.get_by_label('Tipo de bono').select_option(label='Monto fijo' if fixed else 'Porcentaje del sueldo')
            await d.get_by_label('Monto fijo' if fixed else 'Porcentaje (%)', exact=True).fill('0.125' if fixed else '10')
            await d.get_by_label('Motivo').select_option('000001')
            assert await d.locator('.preview dd').all_text_contents() == (['0.13', '0.01', '0.12'] if fixed else ['500.00', '40.00', '460.00'])
            async with page.expect_response(lambda r: r.url == url + '/api/v1/Bonus' and r.request.method == 'POST') as response:
                await d.get_by_role('button', name='Guardar bono').click()
            response = await response.value
            assert response.status == status, await response.text()
            if status == 201:
                await expect(d).to_have_count(0)
                return (await response.json())['bonusId']
            await d.get_by_role('button', name='Cancelar', exact=True).click()

        async def action(name, method, suffix):
            page.once('dialog', lambda d: d.accept())
            async with page.expect_response(lambda r: r.request.method == method and r.url.endswith(suffix)) as response:
                await page.locator('tbody tr').first.get_by_role('button', name=name, exact=True).click()
            assert (await response.value).status == 204

        try:
            await page.goto(url + '/bonuses')
            await expect(page.get_by_text('No hay bonos para los filtros seleccionados.')).to_be_visible()
            paid = await register('1999-01')
            await expect(page.locator('tbody tr')).to_have_count(1)
            page.once('dialog', lambda d: d.dismiss())
            await page.get_by_role('button', name='Pagar', exact=True).click()
            assert sql(f'SELECT StateCode FROM dbo.Bonus WHERE BonusId={paid};') == '0201'
            await action('Pagar', 'POST', '/pay')
            await expect(page.locator('tbody').get_by_text('Pagado', exact=True)).to_be_visible()
            for name in ('Pagar', 'Anular', 'Eliminar'):
                await expect(page.locator('tbody').get_by_role('button', name=name, exact=True)).to_be_disabled()
            assert sql(f'SELECT CAST(SUM(Amount) AS varchar(30)) FROM dbo.BonusAllocation WHERE BonusId={paid};') == '500.00'
            assert sql(f"SELECT COUNT(*) FROM dbo.Bonus WHERE BonusId={paid} AND StateCode='0202' AND ModifiedBy=N'{operator}' AND ModifiedAt>=CreatedAt;") == '1'
            await register('1999-01', status=409)
            deleted = await register('1999-02', fixed=True)
            await expect(page.locator('tbody tr')).to_have_count(2)
            await action('Eliminar', 'DELETE', '/' + str(deleted))
            await expect(page.locator('tbody tr')).to_have_count(1)
            assert sql(f'SELECT CAST(IsDeleted AS int) FROM dbo.Bonus WHERE BonusId={deleted};') == '1'
            cancelled = await register('1999-03')
            await expect(page.locator('tbody tr')).to_have_count(2)
            await action('Anular', 'PUT', '/cancel')
            await expect(page.locator('tbody').get_by_text('Anulado', exact=True)).to_be_visible()
            assert sql(f'SELECT StateCode FROM dbo.Bonus WHERE BonusId={cancelled};') == '0203'
            await page.set_viewport_size({'width': 390, 'height': 844})
            assert await page.evaluate('document.documentElement.scrollWidth <= innerWidth')
            assert requests and all(urlsplit(r).netloc == urlsplit(url).netloc for r in requests)
            assert not errors, errors
        finally:
            sql(f"DELETE a FROM dbo.BonusAllocation a JOIN dbo.Bonus b ON b.BonusId=a.BonusId WHERE b.CreatedBy=N'{operator}'; DELETE FROM dbo.Bonus WHERE CreatedBy=N'{operator}';")
            await browser.close()
    check_seed()
    print('Registro, cálculo, pago, anulación, eliminación y conservación de datos comprobados.')


if '--check' in sys.argv:
    check_seed()
    print('Semilla intacta y sin registros de prueba.')
else:
    asyncio.run(main())
