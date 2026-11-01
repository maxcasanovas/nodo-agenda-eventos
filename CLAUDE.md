# CLAUDE.md — Nodo · Agenda y Eventos

Prototipo funcional del subdominio **Agenda y Eventos** del caso Nodo (coworking), diseñado con DDD
y Clean Architecture como monolito modular. Proyecto académico (Maestría en Ingeniería de Software,
módulo DDD) que debe demostrarse en video: priorizar claridad del modelo y que el ciclo completo funcione.

## ⚠️ Criterios que se evalúan

1. **Las invariantes son lo más importante del prototipo.** Cada invariante del catálogo
   (`business-rules.md §9`, IDs `INV-xx`) debe:
   - vivir en el agregado raíz o en un Value Object (jamás en controller, handler, validator ni en el front);
   - ser imposible de saltar: no hay constructores públicos, setters públicos ni métodos que dejen el agregado en estado inválido
     (*always-valid domain model*); los VOs no se pueden crear inválidos;
   - lanzar `DomainException` con el `code` del catálogo y mensaje en español;
   - tener tests unitarios del caso válido, del límite y del inválido;
   - estar reforzada en la BD con `CHECK`/`EXCLUDE` cuando aplique (defensa en profundidad, no reemplazo);
   - ser demostrable desde la UI (Laboratorio de invariantes, spec 06) y desde Scalar (respuesta 422 con su `code`).
   FluentValidation solo valida **forma** (campos requeridos, formatos). Si dudas si algo es invariante, trátalo como invariante.
2. **Todo debe correr empaquetado en Docker.** `docker compose up --build` levanta Postgres, API y frontend, aplica
   migraciones y seeds, y deja la app usable sin pasos manuales. Ejecutar fuera de Docker es solo comodidad de desarrollo.

## Fuentes de verdad (leer antes de cada fase)

1. `docs/domain/business-rules.md` — glosario ES→EN, reglas BR-xx, máquina de estados, tarifas,
   datos externos hardcodeados, context map implementado y datos semilla. **Si algo de un spec
   contradice este archivo, gana este archivo; si encuentras una contradicción, pregúntame.**
2. `docs/specs/NN-*.md` — un spec por fase. Implementa **solo** la fase que te indique.
3. `docs/PROGRESS.md` — bitácora de avance. Actualízala al cerrar cada fase.

## Flujo de trabajo (Spec-Driven Development)

- Trabaja **una fase a la vez**. No adelantes trabajo de fases futuras ni "dejes listo" código que no pide el spec.
- Antes de codificar, presenta un plan corto (archivos a crear, decisiones) y espera mi OK.
- Si el spec es ambiguo o falta una regla, **pregunta**; no inventes reglas de negocio.
- Al terminar una fase:
  1. `dotnet build` sin warnings nuevos y `dotnet test` en verde (y `npm run build` + `npm run lint` si tocaste el front).
     Desde la fase 01, `docker compose up --build` también debe seguir funcionando.
  2. Recorre los criterios de aceptación del spec uno por uno y marca cuáles se cumplen.
  3. Actualiza `docs/PROGRESS.md` (qué se hizo, desviaciones, deuda pendiente).
  4. Propón un mensaje de commit (Conventional Commits) y **detente**.
- No agregues paquetes NuGet/npm que no estén en la sección Stack sin preguntarme.

## Stack

**Backend**
- .NET 10, C# con `Nullable` y `ImplicitUsings` habilitados, `TreatWarningsAsErrors` en proyectos de dominio.
- Central Package Management (`Directory.Packages.props`) + `Directory.Build.props`.
- ASP.NET Core con **Controllers**, OpenAPI nativo (`Microsoft.AspNetCore.OpenApi`) + **Scalar** (`Scalar.AspNetCore`).
- **MediatR** (licencia Community, gratuita). La clave se lee de configuración (`MediatR:LicenseKey`)
  vía user-secrets/variable de entorno; **nunca** se commitea. Sin clave debe seguir funcionando.
- FluentValidation para validación de comandos (pipeline behavior).
- EF Core 10 + `Npgsql.EntityFrameworkCore.PostgreSQL` + `EFCore.NamingConventions` (snake_case).
- PostgreSQL 17.

**Contenedores**
- `docker-compose.yml` en la raíz con servicios `db` (postgres:17, healthcheck `pg_isready`), `api`
  (Dockerfile multi-stage `mcr.microsoft.com/dotnet/sdk:10.0` → `aspnet:10.0`, `depends_on: db` con `condition: service_healthy`,
  healthcheck en `/health`), `web` (Dockerfile multi-stage `node:22-alpine` → `nginx:alpine`, proxy `/api` → `api:8080`)
  y `pgadmin` bajo el profile `tools`.
- La API arranca con `ASPNETCORE_ENVIRONMENT=Docker`, aplica migraciones al iniciar (`Database:MigrateOnStartup=true`) y expone Scalar también en ese entorno.
- Configuración solo por variables de entorno (`ConnectionStrings__Nodo`, `Api__SelfBaseUrl`, `MediatR__LicenseKey` vía `.env` no commiteado; incluir `.env.example`).
- `.dockerignore` en raíz y en `web/`.

**Frontend** (`web/`)
- Vite + React 19 + TypeScript (strict).
- TanStack Query v5, React Router, Tailwind CSS v4 + shadcn/ui, FullCalendar (plugins MIT: daygrid, timegrid, interaction).
- Textos de UI en **español**.

**Tests**
- Solo unit tests: xUnit, Shouldly (aserciones), NSubstitute (dobles). **No** tests de integración ni Testcontainers.
- NetArchTest.Rules para tests de arquitectura (son unit tests: no tocan BD).

## Arquitectura

Monolito modular. Cada Bounded Context es un módulo con 4 proyectos (Clean Architecture):

```
src/
  BuildingBlocks/
    Nodo.SharedKernel/                    # Entity, AggregateRoot, ValueObject, IDomainEvent, DomainException, Money, StronglyTypedId
    Nodo.BuildingBlocks.Application/      # IClock, abstracciones CQRS sobre MediatR, behaviors, IIntegrationEvent, IEventBus
    Nodo.BuildingBlocks.Infrastructure/   # Outbox/Inbox, InMemoryEventBus, procesador de outbox, DemoClock, interceptores EF
  Modules/
    Quotes/          Nodo.Quotes.{Domain,Application,Infrastructure,Contracts}
    EventLifecycle/  Nodo.EventLifecycle.{Domain,Application,Infrastructure,Contracts}
    HallSchedule/    Nodo.HallSchedule.{Domain,Application,Infrastructure,Contracts}
  External/
    Nodo.External.Simulated/              # Contextos externos hardcodeados (Espacios, Membresías, Pasarela, Facturación, Acceso, Notificaciones, Catering)
  Nodo.Api/                               # Host: controllers, Scalar, DI, manejo de errores, CORS, migraciones al arrancar
web/                                      # React
tests/
  Nodo.Quotes.UnitTests/  Nodo.EventLifecycle.UnitTests/  Nodo.HallSchedule.UnitTests/  Nodo.ArchitectureTests/
```

### Reglas de dependencia (verificadas por `Nodo.ArchitectureTests`)

- `*.Domain` → solo `Nodo.SharedKernel`. Sin EF, sin MediatR, sin ASP.NET, sin paquetes externos.
- `*.Application` → su Domain, `BuildingBlocks.Application` y los `*.Contracts` (propios y de otros módulos).
- `*.Infrastructure` → su Application, `BuildingBlocks.Infrastructure`, EF Core y `Nodo.External.Simulated` (solo vía adaptadores/ACL).
- `*.Contracts` → solo tipos primitivos/DTOs y `BuildingBlocks.Application` (para `IIntegrationEvent`). Es el **Published Language**.
- Un módulo **nunca** referencia Domain/Application/Infrastructure de otro módulo. La comunicación entre módulos es:
  - **Síncrona**: interfaces de API de módulo publicadas en `*.Contracts` (ej. `IHallScheduleModuleApi`).
  - **Asíncrona**: integration events vía Outbox → InMemoryEventBus → handlers del consumidor (con Inbox para idempotencia).
- `Nodo.Api` es el único proyecto que conoce a todos (composition root).

### Reglas de diseño táctico

- Agregados ricos: setters privados, constructores privados + métodos de fábrica, colecciones expuestas como `IReadOnlyCollection`.
- **Toda invariante vive en el agregado o en un Value Object**, nunca en controllers ni handlers. Los handlers orquestan: cargan, invocan, guardan.
- IDs fuertemente tipados (`readonly record struct HallEventId(Guid Value)`).
- Value Objects inmutables (`record` o clase con igualdad estructural), validan en su creación.
- Dinero siempre `decimal` vía `Money` (moneda BOB). Prohibido `double`/`float` para montos. Redondeo a 2 decimales `MidpointRounding.AwayFromZero`.
- **Tiempo**: el dominio recibe `DateTime nowUtc` como parámetro; la capa de aplicación lo obtiene de `IClock`. Prohibido `DateTime.Now/UtcNow` fuera de la implementación de `IClock`.
- Fechas de evento en hora local del salón (America/La_Paz): `DateOnly` + `TimeOnly`. Marcas de tiempo técnicas en UTC (`timestamptz`).
- Un comando = una transacción por módulo. Los domain events se despachan dentro de la transacción y sus handlers escriben integration events en el Outbox.
- Las violaciones de reglas lanzan `DomainException(code, message)`; `code` estable en inglés (ej. `EVENT.DURATION_OUT_OF_RANGE`), `message` en español (se muestra en la UI).
- Repositorios: interfaz en Domain, implementación EF en Infrastructure. Consultas de lectura (read models) pueden ir directo con EF `AsNoTracking` desde Application vía interfaces de query.

### Persistencia

- Un `DbContext` por módulo, cada uno en su **schema** de Postgres: `quotes`, `event_lifecycle`, `hall_schedule`.
- Mapeo Fluent en `Infrastructure/Persistence/Configurations`; nada de data annotations en el dominio. VOs como owned types / complex types.
- Migraciones por módulo en `Infrastructure/Persistence/Migrations`, con su propia tabla de historial dentro de su schema.
- Datos semilla mediante una migración dedicada por módulo (`SeedDemoData`), con GUIDs fijos y fechas relativas a `CURRENT_DATE`.
- Concurrencia optimista con `xmin` en los agregados.

## Convenciones

- **Código en inglés** (tipos, métodos, propiedades, rutas de API, tablas). Glosario ES→EN en `business-rules.md`; respétalo al pie de la letra.
- Textos para el usuario (mensajes de error de dominio, UI) en **español**.
- Comentarios en inglés, solo cuando expliquen el *porqué* o refieran una regla (`// BR-14`).
- Rutas REST en inglés, kebab-case, plural: `/api/hall-events/{id}/approve`.
- Errores HTTP como `ProblemDetails`: 400 validación, 404 no encontrado, 409 concurrencia/solapamiento, 422 regla de dominio (incluir `code`).
- Nombres de tests: `Method_Scenario_ExpectedResult`. Un test por regla BR relevante como mínimo.

## Comandos

```bash
# Forma oficial (evaluación / video)
docker compose up --build                     # web http://localhost:3000 · API http://localhost:5080 · Scalar /scalar
docker compose --profile tools up -d pgadmin  # pgAdmin http://localhost:5050 (opcional)
./scripts/reset.sh                            # docker compose down -v + up --build (BD limpia con seeds)

# Desarrollo local (opcional)
docker compose up -d db
dotnet build Nodo.slnx && dotnet test Nodo.slnx
dotnet run --project src/Nodo.Api             # http://localhost:5080
cd web && npm install && npm run dev          # http://localhost:5173 (proxy de Vite a la API)
```

Migraciones (ejemplo para un módulo):

```bash
dotnet ef migrations add <Name> --context QuotesDbContext \
  --project src/Modules/Quotes/Nodo.Quotes.Infrastructure \
  --startup-project src/Nodo.Api --output-dir Persistence/Migrations
```

## Lo que NO se construye

Autenticación/identidad (el rol se elige en un selector del front), pasarela de pagos real, envío real de
correos, RabbitMQ real, tests de integración, salas de reunión ni reservas ordinarias (Core Domain fuera de alcance).
