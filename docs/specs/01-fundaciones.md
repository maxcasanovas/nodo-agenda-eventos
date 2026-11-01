# Spec 01 — Fundaciones de la solución

## Objetivo
Dejar la solución compilando con la estructura de Clean Architecture por módulo, los building blocks
compartidos, el host API con Scalar, Docker con Postgres y los tests de arquitectura que vigilan las
reglas de dependencia. Sin lógica de negocio todavía.

## Alcance
1. **Solución** `Nodo.slnx` en la raíz con todos los proyectos de `CLAUDE.md § Arquitectura` (vacíos salvo lo indicado aquí).
2. `Directory.Build.props`: `net10.0`, `Nullable=enable`, `ImplicitUsings=enable`, `LangVersion=latest`,
   `TreatWarningsAsErrors=true` en proyectos `*.Domain` y `Nodo.SharedKernel`.
3. `Directory.Packages.props` con Central Package Management y todas las versiones del stack.
4. Referencias entre proyectos exactamente según las reglas de dependencia.
5. **`Nodo.SharedKernel`**:
   - `Entity<TId>`, `AggregateRoot<TId>` (lista privada de domain events, `RaiseDomainEvent`, `DequeueDomainEvents`).
   - `ValueObject` base o convención `record`.
   - `IDomainEvent` (con `OccurredOnUtc`), `DomainException(string code, string message)`.
   - `Money` (Amount decimal ≥ 0, Currency "BOB" por defecto; `Add`, `Subtract` sin negativos, `Multiply(decimal)`,
     `Percentage(int)`, redondeo 2 decimales `AwayFromZero`; mezclar monedas lanza `DomainException`).
   - `ITimeZoneProvider`-free helper: constante `NodoTimeZone = "America/La_Paz"` y conversión UTC↔local.
6. **`Nodo.BuildingBlocks.Application`**:
   - `IClock { DateTime UtcNow; DateOnly TodayLocal; DateTime LocalNow; }`.
   - `ICommand`, `ICommand<TResult>`, `IQuery<TResult>` y sus handlers sobre MediatR.
   - `ValidationBehavior` (FluentValidation) y `LoggingBehavior`.
   - `IIntegrationEvent` (`Id`, `OccurredOnUtc`), `IEventBus`, `IIntegrationEventHandler<T>`.
   - `NotFoundException`, `ConflictException`.
7. **`Nodo.BuildingBlocks.Infrastructure`**:
   - `DemoClock : IClock` (singleton) con desplazamiento acumulable (`Advance(TimeSpan)`, `Reset()`), único lugar donde se usa `DateTime.UtcNow`.
   - Solo esqueletos/interfaces de Outbox/Inbox (se implementan en spec 04).
8. **`Nodo.Api`**:
   - Controllers, `AddOpenApi()` + `MapScalarApiReference()` en `/scalar` (título "Nodo · Agenda y Eventos API", tema a elección).
   - Registro de MediatR leyendo `MediatR:LicenseKey` de configuración (opcional).
   - `IExceptionHandler` global que mapea a `ProblemDetails` según `CLAUDE.md § Convenciones` (incluye `code`).
   - CORS para `http://localhost:5173`. Puerto `http://localhost:5080`.
   - `DemoController`: `GET /api/demo/clock`, `POST /api/demo/clock/advance` (`{ days, hours }`), `POST /api/demo/clock/reset`.
   - `GET /health` simple.
9. **Docker (obligatorio desde esta fase)**:
   - `src/Nodo.Api/Dockerfile` multi-stage (restore con caché de capas copiando primero `*.csproj`, `Directory.*.props`; publish Release; imagen `aspnet:10.0`, usuario no root, puerto 8080).
   - `docker-compose.yml`: `db` (postgres:17, db/user/pass `nodo`, volumen nombrado, healthcheck), `api` (5080→8080, `depends_on` db healthy,
     healthcheck `/health`, `ASPNETCORE_ENVIRONMENT=Docker`), `pgadmin` (profile `tools`, 5050). El servicio `web` se agrega en spec 06
     (deja el placeholder comentado).
   - `appsettings.Docker.json` + `.env.example`; connection string local en `appsettings.Development.json`.
   - Scalar habilitado en `Development` y `Docker`.
10. **Scripts**: `scripts/reset.sh` y `scripts/reset.ps1` (`docker compose down -v` + `docker compose up --build -d`).
11. **Tests**:
    - `Nodo.ArchitectureTests` con NetArchTest: Domain no depende de EF/MediatR/AspNetCore ni de otros módulos; Application no depende de Infrastructure; ningún módulo referencia Domain/Application/Infrastructure de otro; Contracts no depende de Domain.
    - Proyectos de unit tests vacíos por módulo + tests de `Money` en un proyecto `Nodo.SharedKernel.UnitTests` (o dentro de ArchitectureTests si prefieres; justifícalo).
12. `.editorconfig`, `.gitignore` (.NET + Node), `README.md` mínimo, `docs/PROGRESS.md` iniciado.

## Fuera de alcance
Agregados de negocio, DbContexts, migraciones, frontend.

## Criterios de aceptación
- [ ] `dotnet build Nodo.slnx` sin errores ni warnings.
- [ ] `dotnet test` en verde, incluyendo ≥ 4 reglas de arquitectura y ≥ 6 tests de `Money` (suma, resta negativa, monedas distintas, porcentaje, redondeo, igualdad).
- [ ] `docker compose up --build` levanta `db` y `api` sanos (`docker compose ps` muestra *healthy*) y `http://localhost:5080/scalar` responde.
- [ ] `dotnet run --project src/Nodo.Api` (con `docker compose up -d db`) también funciona en local.
- [ ] `DomainException` base preparada para recibir el `code` de los `INV-xx` del catálogo (`business-rules.md §9`); crea una clase estática
      `InvariantCodes` por módulo con las constantes del catálogo (se usan en la fase 02).
- [ ] `POST /api/demo/clock/advance` con `{ "days": 2 }` cambia lo que devuelve `GET /api/demo/clock`.
- [ ] Una `DomainException` lanzada en un endpoint de prueba devuelve 422 con `code` (luego elimina el endpoint de prueba).
- [ ] No existe `DateTime.Now`/`DateTime.UtcNow` fuera de `DemoClock` (verifícalo con búsqueda).
