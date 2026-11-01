# Spec 04 — Persistencia (PostgreSQL), migraciones, datos semilla y mensajería

## Objetivo
Persistir los tres módulos en PostgreSQL con EF Core, crear la base de datos mediante migraciones,
cargar datos semilla coherentes y hacer funcionar Outbox → bus en memoria → Inbox, más los jobs de expiración.

## DbContexts
| Módulo | DbContext | Schema |
|---|---|---|
| Quotes | `QuotesDbContext` | `quotes` |
| EventLifecycle | `EventLifecycleDbContext` | `event_lifecycle` |
| HallSchedule | `HallScheduleDbContext` | `hall_schedule` |

- `UseSnakeCaseNamingConvention()`; tabla de historial de migraciones dentro de cada schema (`MigrationsHistoryTable("__ef_migrations_history", schema)`).
- Configuraciones Fluent por entidad. Strongly-typed IDs con `HasConversion`. VOs como owned/complex types
  (`TimeSlot` → columnas `date`, `start_time`, `end_time`; `Money` → `amount numeric(12,2)` + `currency varchar(3)`).
- `ExtraServiceItem`, `ScheduleBlock`, `History`, `CustomerCredit.Movements`, transacciones del depósito: tablas hijas.
- Enums como `varchar` legibles.
- Concurrencia optimista con `xmin` en las raíces (`IsRowVersion` de Npgsql).
- Cada DbContext tiene `outbox_messages` e `inbox_messages` en su schema.

## Defensa en BD contra solapamientos (HallSchedule)
- Migración con `CREATE EXTENSION IF NOT EXISTS btree_gist;`.
- Columnas `buffered_start` / `buffered_end` (`timestamp without time zone`, hora local) en `schedule_blocks`.
- Restricción de exclusión: `EXCLUDE USING gist (hall_id WITH =, tsrange(buffered_start, buffered_end, '[)') WITH &&) WHERE (status = 'Active')`.
- Una violación (`23P01`) se traduce a `ConflictException` ⇒ HTTP 409.

## Invariantes reforzadas en BD (defensa en profundidad)
- Implementar en `InitialCreate` todas las restricciones de la columna **BD** de `business-rules.md §9`
  (`CHECK` vía `ToTable(t => t.HasCheckConstraint(...))`, exclusión e índice único parcial vía `migrationBuilder.Sql`).
- Nombrar cada restricción con su ID: `ck_hall_events_inv04_attendance`, `ex_schedule_blocks_inv18_overlap`, etc.
- Traducir errores de Postgres `23514` (check) y `23P01` (exclusion) a `ConflictException` con el `code` del `INV` correspondiente (mapear por nombre de constraint) ⇒ HTTP 409.
- La materialización desde EF no debe saltarse invariantes: usar campos privados / constructores privados, nunca setters públicos para EF.

## Unit of Work, Outbox e Inbox
- `SaveChanges` del módulo: (1) despacha domain events de los agregados trackeados; (2) sus handlers añaden integration events al outbox;
  (3) un solo `SaveChanges`/transacción para agregados + outbox.
- `OutboxMessage`: `id`, `type` (nombre del contrato), `content jsonb`, `occurred_on_utc`, `processed_on_utc`, `attempts`, `error`.
- `OutboxProcessor<TDbContext>` (`BackgroundService`, uno por módulo): cada 1 s toma hasta 20 pendientes (`FOR UPDATE SKIP LOCKED`),
  deserializa y publica en `InMemoryEventBus`; marca procesado o registra error (máx. 5 intentos).
- `InMemoryEventBus`: resuelve `IIntegrationEventHandler<T>` en un scope nuevo por handler.
- Inbox: decorador que registra (`message_id`, `handler`) en `inbox_messages` del módulo consumidor y omite duplicados.
- Registro global de eventos publicados (para el panel de demo): tabla `integration_event_log` en schema `public` **o**
  consulta unificada de los tres outbox; elige y justifica.

## Jobs
- `QuoteExpirationJob` y `HallEventExpirationJob` (`BackgroundService`, cada 30 s) usando `IClock` (respetan el reloj de demo).
- Endpoint `POST /api/demo/jobs/run` que los ejecuta inmediatamente (útil tras adelantar el reloj).

## Migraciones
Por módulo, en `Infrastructure/Persistence/Migrations`:
1. `InitialCreate` — esquema completo.
2. `SeedDemoData` — datos de `business-rules.md §8` con `migrationBuilder.Sql(...)`, fechas relativas a `CURRENT_DATE`
   y timestamps relativos a `now()`. IDs fijos documentados. Totales calculados a mano según BR-06..08 (déjalos comentados en la migración).

`Nodo.Api` aplica `MigrateAsync()` de los tres contextos al arrancar cuando `Database:MigrateOnStartup=true` (activo en `Development` y `Docker`;
orden: HallSchedule, Quotes, EventLifecycle), con reintentos mientras Postgres termina de arrancar.
`scripts/reset.*` deja la BD limpia y re-sembrada dentro de Docker.

## Repositorios
Implementaciones EF de todos los repositorios del dominio y de las interfaces de query (read models con `AsNoTracking` y proyecciones).

## Tests (unit, sin BD)
- `OutboxProcessor`: serialización/deserialización de cada integration event (round-trip) para detectar contratos rotos.
- Decorador de Inbox con un fake de almacenamiento: segunda entrega no invoca el handler.
- **No** tests de integración contra Postgres.

## Criterios de aceptación
- [ ] `./scripts/reset.sh` (todo en Docker) crea los 3 schemas, tablas, restricciones y datos semilla sin errores.
- [ ] Un `INSERT` manual con aforo 90, depósito 60 % o saldo negativo es rechazado por su `CHECK` con nombre `inv..`.
- [ ] En pgAdmin se ven E1..E6, Q1..Q7, `customer_credits` de TechSol = 600 y bloqueos coherentes.
- [ ] Insertar a mano en SQL un bloqueo activo solapado falla por la restricción de exclusión.
- [ ] Adelantar el reloj 5 días y ejecutar jobs marca Q1 como `Expired`, E1 como `Expired` y libera su bloqueo (consistencia eventual vía outbox).
- [ ] Los outbox quedan con `processed_on_utc` poblado tras unos segundos.
