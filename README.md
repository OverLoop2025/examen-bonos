# Bonos de desempeño

Busca empleados activos, registra bonos, calcula sus importes y permite pagarlos, anularlos o eliminarlos. El pago reparte el bruto entre los proyectos del empleado.

## Iniciar la aplicación

Necesitas **Git**, **Docker con Compose 2.20 o posterior**, un equipo **x64** y al menos **6 GB de memoria disponibles para Docker**. En Windows o Mac Intel, abre Docker Desktop con contenedores Linux. El navegador debe estar actualizado.

No necesitas instalar .NET, Node ni SQL Server en el equipo: Docker construye y ejecuta los tres servicios.

1. Descarga el proyecto:

   ```bash
   git clone https://github.com/OverLoop2025/examen-bonos.git
   cd examen-bonos
   cp .env.example .env
   ```

2. Edita `.env` y escribe una contraseña en `SA_PASSWORD`, entre las comillas. Usa al menos 12 caracteres con mayúsculas, minúsculas, números y algún símbolo; evita comillas y `;`. Si algún puerto está ocupado, cambia `SQL_PORT`, `API_PORT` o `WEB_PORT`.

3. Inicia todo desde la raíz:

   ```bash
   docker compose up --build -d --wait
   ```

   La primera vez necesita internet para descargar las imágenes y los paquetes. El comando termina cuando la web puede consultar la API y SQL Server. Los scripts `01_schema_seed.sql`, `02_bonus.sql` y `03_bonus_allocation.sql` se aplican en orden, únicamente si falta su tabla. No hay que ejecutarlos a mano.

4. Abre [la aplicación](http://localhost:4200/bonuses). [Swagger](http://localhost:5106/swagger) permite revisar la API. Si cambiaste un puerto, úsalo en estos enlaces.

Los comandos sirven en Bash y PowerShell. Los datos permanecen en un volumen; **no cambies su contraseña al reiniciar**. Para detener o volver a iniciar:

```bash
docker compose stop
docker compose up -d --wait
```

Desde otro equipo de la misma red, usa `http://IP_DEL_SERVIDOR:4200/bonuses` y permite el puerto de la web en el firewall. Las consultas usan esa misma dirección; no dependen del `localhost` del visitante.

Si algo no arranca, revisa `docker compose ps -a` y `docker compose logs --tail 80`. No borres el volumen para resolver un problema: contiene los registros.

## Primera prueba

La tabla empieza vacía. Pulsa **Nuevo bono**, selecciona **Tecnología**, escribe solo **101** y busca al empleado. Debe aparecer Andrea Torres Vega, sueldo 5,000.00 y retención 8 %.

Elige el mes actual o uno anterior, **Porcentaje del sueldo**, valor **10** y un motivo. La vista previa debe mostrar **500.00 de bruto, 40.00 de retención y 460.00 de neto**. Guarda y comprueba la fila.

**Pagar**, previa confirmación, guarda **375.00 para Portal de clientes y 125.00 para App móvil** y pasa a Pagado. El detalle queda en `BonusAllocation`. Un Pagado ya no permite pagar, anular ni eliminar.

Para probar **Anular** y **Eliminar**, registra bonos en otros meses: son alternativas desde Registrado. Los filtros son opcionales; **Buscar** los aplica y **Limpiar** los quita. Cada página contiene diez registros.

## Comprobaciones

El flujo **Comprobar aplicación**, en [GitHub Actions](https://github.com/OverLoop2025/examen-bonos/actions), construye un entorno nuevo, prueba la interfaz real, ejecuta las pruebas de backend y frontend y comprueba que las 28 filas iniciales no cambien. No usa archivos locales ni credenciales personales. Sus comandos están en [.github/workflows/comprobar.yml](.github/workflows/comprobar.yml).

La interfaz se comprobó con Chromium 156, Firefox 157 y los motores WebKit 18.2 y 27.2, en escritorio y móvil.

## Desarrollo local opcional

Para trabajar sin contenedores de aplicación, instala [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0) y [Node.js 24.15 o posterior de la rama 24](https://nodejs.org/en/download). Puedes mantener la base preparada por Docker. Estos comandos usan Bash.

Detén antes `api` y `web` de Docker si usan los mismos puertos: `docker compose stop api web`.

**Terminal 1: API**

```bash
source .env
export ConnectionStrings__BonusTest="Server=127.0.0.1,$SQL_PORT;Database=BonusTest;User Id=sa;Password=$SA_PASSWORD;TrustServerCertificate=True"
dotnet run --project backend/src/BonusTest.Api --configuration Release --launch-profile http
```

**Terminal 2: pantalla**

```bash
npm --prefix frontend ci
npm --prefix frontend start -- --host 0.0.0.0 --port 4200
```

Deja ambas terminales abiertas. El proxy de desarrollo apunta a la API del puerto 5106. Para las pruebas, abre una tercera terminal y prepara la misma conexión:

```bash
source .env
export ConnectionStrings__BonusTest="Server=127.0.0.1,$SQL_PORT;Database=BonusTest;User Id=sa;Password=$SA_PASSWORD;TrustServerCertificate=True"
export BONUS_API_URL=http://localhost:5106
dotnet test --solution backend/BonusTest.slnx --configuration Release
npm --prefix frontend test
dotnet build backend/BonusTest.slnx --configuration Release
npm --prefix frontend run build
```

Sin conexión SQL o `BONUS_API_URL`, algunas pruebas backend se omiten; eso no equivale a verificar todo el módulo.

## Decisiones y alcance

- SQL Server 2022, .NET 10 con Entity Framework Core y Angular 22 standalone. Se conservan Domain, Application, Infrastructure y Api.
- Índice único filtrado contra bonos vigentes duplicados, incluso simultáneos. `RowVersion` protege cambios concurrentes; el borrado es lógico.
- Sueldo, retención e importes se conservan con el bono. Cálculos decimales y redondeo a dos decimales con el medio hacia arriba; la vista previa no sustituye al servidor.
- Periodos y auditoría usan Lima. Paginación y filtros se ejecutan en SQL Server.
- El pago agrupa horas por nombre y ordena por su primera fila. El último proyecto recibe el saldo exacto; nombre, horas e importe quedan guardados junto con estado y auditoría en una transacción. Horas inválidas o un remanente negativo rechazan la operación sin guardar cambios.
- `UserCode` identifica al operador; la pantalla envía `candidato`. No es un inicio de sesión.
- Se incluyen los requisitos obligatorios y los dos extras. El servidor requiere x64: la imagen de SQL Server no admite ARM. El cliente solo necesita un navegador compatible. Docker usa Development para mostrar Swagger; `APP_ENVIRONMENT=Production` lo desactiva.
