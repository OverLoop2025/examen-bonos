# Bonos de desempeño

Módulo de Recursos Humanos para registrar bonos por periodo. Busca al empleado, calcula bruto, retención y neto, y permite pagar, anular o eliminar cada bono. Al pagar, el bruto se reparte entre los proyectos del empleado según sus horas.

## Iniciar en un paso

Solo necesitas [Docker Desktop](https://www.docker.com/products/docker-desktop/) abierto, con al menos 4 GB de memoria. No hace falta instalar .NET, Node ni SQL Server.

```bash
git clone https://github.com/OverLoop2025/examen-bonos.git
cd examen-bonos
docker compose up --build -d --wait
```

Cuando el comando termine, abre **http://localhost:4200/bonuses**. La API se puede revisar en http://localhost:5106/swagger.

- La primera vez descarga imágenes y paquetes, así que tarda unos minutos y necesita internet.
- La base se crea sola: los scripts `01_schema_seed.sql`, `02_bonus.sql` y `03_bonus_allocation.sql` se ejecutan en ese orden, solo si su tabla aún no existe.
- Si los puertos 4200, 5106 o 14333 están ocupados, copia `.env.example` como `.env` y cámbialos ahí. En ese archivo también puedes definir otra contraseña para SQL Server (si lo haces, defínela antes del primer arranque).

| Para | Comando |
|------|---------|
| Detener | `docker compose stop` |
| Volver a iniciar | `docker compose up -d --wait` |
| Borrar todo, incluidos los datos | `docker compose down -v` |

## Empleados de prueba

En el buscador se escribe el área y **solo el número**, por ejemplo Tecnología y `101`.

| Área | Número | Empleado | Estado | Sueldo | Retención |
|------|--------|----------|--------|--------|-----------|
| Tecnología (TECH1) | 101 | Andrea Torres Vega | Activo | 5,000.00 | 8 % |
| Tecnología (TECH1) | 102 | Carlos Ruiz Medina | Activo | 3,000.00 | 8 % |
| Tecnología (TECH1) | 103 | Lucía Flores Paz | Activo | 8,000.00 | 10 % |
| Ventas (VENTA) | 201 | Jorge Salas León | Activo | 2,500.00 | 0 % |
| Ventas (VENTA) | 202 | Rosa Medina Quispe | Cesado | 2,000.00 | 8 % |

## Recorrido sugerido

1. Pulsa **Nuevo bono**, elige Tecnología, escribe `101` y busca. Aparecen el nombre, la fecha de ingreso, el sueldo y la retención.
2. Elige el mes actual o uno anterior, **Porcentaje del sueldo**, valor `10` y un motivo. La vista previa muestra **500.00** de bruto, **40.00** de retención y **460.00** de neto. Al guardar, el modal se cierra y el listado se actualiza.
3. En la fila, abre **Acciones (⋯)** y elige **Pagar**. Tras confirmar, el bono pasa a Pagado y se guardan 375.00 para *Portal de clientes* y 125.00 para *App móvil* en `BonusAllocation`.
4. Registra otros bonos para probar **Anular** y **Eliminar**. Solo los bonos en estado Registrado tienen acciones, y todas piden confirmación. Eliminar quita el bono del listado, pero el registro sigue en la tabla.
5. Prueba también los casos que deben fallar:

| Intento | Respuesta |
|---------|-----------|
| Ventas `202` (cesada) | Solo se aceptan empleados activos. |
| Tecnología `999` | No existe un empleado con esa área y número. |
| Mismo empleado y periodo con un bono vigente | El empleado ya tiene un bono vigente en ese periodo. |
| Periodo posterior al mes actual, monto mayor al sueldo o porcentaje mayor a 100 | No se permite guardar. |

Los filtros son opcionales: **Buscar** los aplica y **Limpiar** los quita. Cada página muestra 10 bonos y el total de registros.

## Sin Docker

Necesitas SQL Server 2019 o posterior (también sirve LocalDB), [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0) y [Node.js 24](https://nodejs.org/) (24.15 o posterior).

1. Ejecuta en orden `database/01_schema_seed.sql`, `02_bonus.sql` y `03_bonus_allocation.sql`, por ejemplo desde SQL Server Management Studio.
2. La API usa `Server=localhost;Database=BonusTest;Trusted_Connection=True;TrustServerCertificate=True;`. Si tu servidor es otro, por ejemplo `(localdb)\MSSQLLocalDB`, cámbialo en `backend/src/BonusTest.Api/appsettings.json` o en la variable de entorno `ConnectionStrings__BonusTest`.
3. Inicia la API: `dotnet run --project backend/src/BonusTest.Api --launch-profile http`
4. En otra terminal, inicia la pantalla: `cd frontend`, luego `npm ci` y `npm start`. Abre http://localhost:4200/bonuses.

## Pruebas

```bash
dotnet test --solution backend/BonusTest.slnx
npm --prefix frontend test
```

Las pruebas del cálculo y del reparto no necesitan base de datos. Las de persistencia y API se ejecutan cuando existen `ConnectionStrings__BonusTest` y `BONUS_API_URL`; si faltan, aparecen como omitidas.

En cada envío, [GitHub Actions](https://github.com/OverLoop2025/examen-bonos/actions) construye todo desde cero, recorre la pantalla en un navegador real, ejecuta ambas suites y comprueba que los datos iniciales no cambien ([comprobar.yml](.github/workflows/comprobar.yml)).

## Decisiones técnicas

- **Capas.** Las reglas viven en Domain: registro, cambios de estado, cálculo y reparto. Application coordina, Infrastructure usa EF Core y Api solo traduce HTTP. Ningún controlador tiene reglas.
- **Tabla Bonus.** Guarda el valor ingresado, el sueldo y la retención del momento, además de los importes calculados, para que el histórico no cambie si cambia el sueldo. Tiene clave foránea compuesta hacia Employee y otras hacia tipo, motivo y estado, más restricciones CHECK que repiten las reglas.
- **Un bono vigente por periodo.** Lo garantiza un índice único filtrado (empleado y periodo, sin Anulados ni eliminados), incluso con dos registros simultáneos. `RowVersion` evita que dos cambios se pisen.
- **Dinero.** Cada importe se redondea a 2 decimales con el medio hacia arriba. La vista previa usa aritmética decimal exacta para mostrar lo mismo que el servidor, pero el servidor siempre vuelve a validar y calcular.
- **Hora de Lima.** El mes actual y la auditoría usan la hora de Lima. Si el sistema no trae zonas horarias, se usa UTC-5, que es equivalente porque Lima no cambia de horario.
- **Listado.** Los filtros y la paginación se resuelven en SQL Server. Cada fila trae las descripciones listas para mostrar.
- **Pago.** Reparte el bruto según las horas, redondea cada línea y el último proyecto recibe la diferencia para cuadrar exacto. El detalle y el cambio a Pagado se guardan en una sola transacción.
- **Pantalla.** Angular 22 con componentes standalone y el CDK de Angular para el menú de acciones; no usa una librería visual pesada. Los errores de la API los muestra el interceptor. El empleado se busca por área y número, como pide el enunciado, para que también se vea la validación de empleados cesados.
- **Operador.** La pantalla envía el header `UserCode` con el valor `candidato`. No hay inicio de sesión.

## Pendiente

- Inicio de sesión real: hoy el operador es fijo.
- La pantalla no muestra el detalle del reparto de un bono pagado. Queda guardado en `BonusAllocation`.
- Un índice para el listado, si el volumen crece. Con estos datos basta el índice único.
- No se probó en Mac con chip Apple. La imagen de SQL Server es x64, así que allí depende de la emulación de Docker Desktop (Rosetta).
