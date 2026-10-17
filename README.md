# Bonos de desempeño

Permite buscar empleados activos, registrar bonos por monto fijo o porcentaje del sueldo, consultar sus importes y pagarlos, anularlos o eliminarlos. El cálculo y las validaciones se realizan en el servidor.

## Antes de empezar

- [Git](https://git-scm.com/downloads), [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0) y [Node.js 24.15 o posterior de la rama 24](https://nodejs.org/en/download).
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) abierto, con contenedores Linux y al menos 4 GB de memoria disponibles. La imagen de SQL Server requiere un equipo **x64**.
- Un navegador actualizado. Se comprobó con Chromium 156, Firefox 157 y los motores WebKit 18.2 y 27.2.

Los comandos siguientes usan **Bash**, disponible en Linux y en WSL para Windows. Comprueba que `dotnet --version`, `node --version` y `docker compose version` funcionan en esa terminal.

## 1. Descargar y configurar

```bash
git clone https://github.com/OverLoop2025/examen-bonos.git
cd examen-bonos
cp .env.example .env
```

Edita `.env` y escribe tu contraseña entre las comillas de `SA_PASSWORD`. Usa al menos 12 caracteres con mayúsculas, minúsculas, números y algún símbolo; evita comillas y `;`. Ese archivo es local y no se publica.

Si el puerto 14333 está ocupado, cambia `SQL_PORT` por uno libre antes de continuar.

## 2. Preparar la base de datos

```bash
docker compose up -d --wait
docker compose exec -T db /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -f 65001 -i /scripts/01_schema_seed.sql
docker compose exec -T db /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -f 65001 -i /scripts/02_bonus.sql
docker compose exec -T db /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -f 65001 -i /scripts/03_bonus_allocation.sql
```

El primer comando espera a que SQL Server esté listo. Los scripts se ejecutan **una sola vez, en ese orden**, sobre una base nueva: `01` crea `BonusTest` y sus datos; `02` añade los bonos y `03` el detalle por proyecto. Si ya ejecutaste `01` y `02`, aplica únicamente `03`.

Los datos se conservan al cerrar Docker. Para volver a abrir el proyecto, usa `docker compose up -d --wait` sin repetir los scripts ni cambiar la contraseña del volumen existente.

## 3. Iniciar la aplicación

Desde la raíz del proyecto, abre dos terminales.

**Terminal 1: API**

```bash
source .env
export ConnectionStrings__BonusTest="Server=127.0.0.1,$SQL_PORT;Database=BonusTest;User Id=sa;Password=$SA_PASSWORD;TrustServerCertificate=True"
dotnet run --project backend/src/BonusTest.Api --configuration Release --launch-profile http
```

**Terminal 2: pantalla**

```bash
npm --prefix frontend ci
npm --prefix frontend start -- --host 127.0.0.1 --port 4200
```

Abre [la pantalla de bonos](http://localhost:4200/bonuses). La API y su documentación están en [Swagger](http://localhost:5106/swagger). Mantén libres los puertos **4200 y 5106**; la pantalla ya apunta a esa API.

Deja ambas terminales abiertas mientras uses la aplicación. Si cierras una, vuelve a ejecutar su comando.

Para una primera prueba, busca **Tecnología / 101** y registra un bono del **10 %** para el mes actual. Debe mostrar **500.00 de bruto, 40.00 de retención y 460.00 de neto**. Puedes pagarlo, anularlo o eliminarlo desde la tabla, previa confirmación.

Al pulsar **Pagar**, el bruto se reparte en **375.00 para Portal de clientes y 125.00 para App móvil**. El bono pasa a Pagado y sus tres acciones quedan deshabilitadas. El detalle se conserva en `BonusAllocation`.

## Pruebas y compilación

Con SQL Server y la API anteriores en ejecución, abre otra terminal en la raíz. Usa una base dedicada a pruebas, con la semilla original y sin bonos de uso personal.

```bash
source .env
export ConnectionStrings__BonusTest="Server=127.0.0.1,$SQL_PORT;Database=BonusTest;User Id=sa;Password=$SA_PASSWORD;TrustServerCertificate=True"
export BONUS_API_URL=http://localhost:5106
dotnet test --solution backend/BonusTest.slnx --configuration Release
npm --prefix frontend test
```

Las pruebas incluyen reglas, cálculos, esquema, persistencia y operaciones HTTP contra SQL Server. Revierten o limpian sus propios registros. Si falta la conexión o `BONUS_API_URL`, algunas pruebas se omiten: eso **no** equivale a verificar el módulo completo.

Para compilar ambos proyectos:

```bash
dotnet build backend/BonusTest.slnx --configuration Release
npm --prefix frontend run build
```

Para detenerlos, pulsa `Ctrl+C` en las dos terminales y ejecuta `docker compose stop`.

## Decisiones y alcance

- SQL Server 2022, .NET 10 con Entity Framework Core y Angular 22 standalone. Se conservan las capas Domain, Application, Infrastructure y Api del proyecto base.
- Índice único filtrado para impedir bonos vigentes duplicados, incluso simultáneos. `RowVersion` protege las modificaciones concurrentes. El borrado es lógico.
- Sueldo, retención e importes quedan guardados con el bono. La aritmética es decimal, con redondeo a dos decimales y medio hacia arriba; la vista previa conserva esa precisión sin sustituir al servidor.
- Periodos y auditoría usan la hora de **Lima**, independientemente del huso del equipo. La paginación y los filtros se ejecutan en SQL Server.
- `POST /api/v1/Bonus/{bonusId}/pay` guarda detalle, estado Pagado y auditoría en una transacción. Agrupa las horas por nombre de proyecto y ordena por su primera fila; el último recibe el saldo exacto. Conserva nombre, horas e importe del momento del pago. Rechaza horas inválidas o un remanente negativo por redondeo, sin guardar cambios.
- El operador llega en `UserCode`; la pantalla envía `candidato`, configurado en `frontend/src/environments/environment.ts`. Es una identificación de la prueba, no un inicio de sesión.
- Se incluyen el módulo obligatorio y los dos extras del enunciado: pago con distribución y pruebas unitarias del cálculo.
