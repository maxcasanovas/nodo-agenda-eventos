# Progreso del prototipo

| Fase | Spec | Estado | Fecha | Notas |
|---|---|---|---|---|
| 01 | Fundaciones | Completada | 2026-10-31 | 22 proyectos, Docker verificado (db + api healthy) |
| 02 | Dominio | Completada | 2026-10-31 | 3 módulos, INV-01..22, 451 tests en verde (436 de dominio/arquitectura + 15 de SharedKernel) |
| 03 | Aplicación | Completada | 2026-11-01 | 3 módulos de Application, 3 Contracts, 616 tests en verde (incluye 5 tests nuevos de dominio) |
| 04 | Persistencia y mensajería | Completada | 2026-11-01 | 3 DbContexts, 6 migraciones, outbox/inbox/bus/jobs funcionando; 720 tests en verde; criterios de BD verificados en Docker |
| 05 | Externos, ACL y API | Completada | 2026-11-01 | `External.Simulated` (7 contextos), 5 adaptadores ACL/Conformist/OHS, 34 operaciones REST documentadas, Laboratorio de invariantes (22 probes); 851 tests en verde; ciclo completo verificado en Docker (61/61 comprobaciones) |
| 06 | Frontend | Completada | 2026-11-01 | SPA React 19 en `web/` (4 roles, calendario, panel externo, Laboratorio de invariantes), servicio `web` en Docker (puerto 3000); `npm run lint` y `npm run build` sin avisos; guion completo verificado en navegador real (23/23) |
| 07 | Cierre y demo | Completada | 2026-11-01 | README, guion de demo, arquitectura y matriz de invariantes (con test que la contrasta con el catálogo); clon limpio, build en frío, recorrido del guion y reinicio verificados; 856 tests en verde |

## Fase 01 — resumen
- `Nodo.slnx` con 22 proyectos: 3 building blocks, 12 de módulos, `External.Simulated`, `Nodo.Api` y 5 de tests.
- SharedKernel (`Entity`, `AggregateRoot`, `ValueObject`, `DomainException`, `Money`, `NodoTimeZone`), building blocks de Application/Infrastructure, host API con Scalar y `GlobalExceptionHandler` (400/404/409/422 con `code`), reloj de demo, Docker (db + api), scripts de reset.

## Fase 02 — resumen
- **HallSchedule.Domain**: `HallSchedule` (un agregado por salón y día), `ScheduleBlock`, `TimeSlot`, `EquipmentAllocation`, `AvailabilityChecker`, 3 domain events, `IHallScheduleRepository`.
- **Quotes.Domain**: `Quote` (Issue / Accept / Expire), `TimeSlot`, `Attendance`, `DegressiveRate`, `ExtraServiceLine`, `PriceBreakdown`, `EventPricingCalculator`, `BookingPolicy`, 3 domain events, `IQuoteRepository`.
- **EventLifecycle.Domain**: `HallEvent` (máquina de estados completa, historial), `GuaranteeDeposit`, `ExtraServiceItem`, `HistoryEntry`, `ManagementDecision`, `CancellationPolicy`, `HallEventFactory`, `CustomerCredit`, 13 domain events, repositorios.
- Modelo *always-valid*: constructores privados, factorías, sin setters públicos, colecciones de solo lectura. Verificado por reflexión (`DomainModelProtectionTests`, 82 casos que cubren todo agregado/entidad/VO presente y futuro).
- Cada comando valida antes de mutar; tras una `DomainException` el agregado conserva estado, historial y no levanta eventos (comprobado en la matriz de estados y en tests por invariante).

## Fase 03 — resumen
- **Puertos compartidos** (`BuildingBlocks.Application`): `IUnitOfWork`, `IOutbox`, `IInbox`, `IDomainEventDispatcher` (+ `MediatRDomainEventDispatcher`), `DomainEventNotification<T>` y `DomainEventHandler<T>` (el dominio sigue sin conocer MediatR). Cada módulo declara sus marcadores (`IQuotesUnitOfWork`, `IEventLifecycleOutbox`, …) para escribir en su propio DbContext y schema.
- **Contracts**: 3 eventos de integración de Quotes, 13 de EventLifecycle, 3 de HallSchedule (sufijo `IntegrationEvent`, solo primitivos), `IHallScheduleModuleApi` (`ScheduleResult` con `Success` + `Code` + `Reason`) e `IEventLifecycleModuleApi`.
- **HallSchedule.Application**: implementación de `IHallScheduleModuleApi`, handlers de integración (`HallEventFirmlyConfirmed` ⇒ consolidar; `EventRequestRejected`, `HallEventExpired`, `HallEventCancelledWithCredit` ⇒ liberar), queries `GetHallCalendar` y `CheckAvailability`, 3 mappers dominio→integración.
- **Quotes.Application**: `PreviewQuote`, `IssueQuote`, `AcceptQuote`, `ExpireDueQuotes`, `GetQuote`, `ListQuotes`, 3 mappers.
- **EventLifecycle.Application**: `QuoteAcceptedHandler` (policy, idempotente por inbox), `ApproveEventRequest`, `RejectEventRequest`, `AccreditDeposit`, `AssignExtraServicesOnConfirmed` (policy sobre el evento de dominio), `StartHallEvent`, `CompleteHallEvent`, `CancelHallEvent`, `RescheduleHallEvent`, `ExpireOverdueHallEvents`, queries (`GetHallEvent`, `ListHallEvents`, `GetPendingApprovals`, `GetCustomerCredit`, `GetTodayOperations`), módulo API de resúmenes y 13 mappers.
- Cada comando lleva un validador FluentValidation (solo forma). Las queries de lectura salen de puertos `I*Queries` (implementación EF en la fase 04); los handlers añaden el nombre del cliente y las horas restantes.
- **Pruebas**: contenedor DI real con MediatR + dispatcher real + fakes en memoria. Cada caso de uso corre en su propio *scope*, como una petición o un mensaje (los servicios scoped no se filtran entre llamadas).

## Fase 04 — resumen
- **`BuildingBlocks.Infrastructure`** (genérico): `EfUnitOfWork<TContext>` (despacha los eventos de dominio en bucle hasta que no quedan, los handlers agregan al outbox y un solo `SaveChanges` confirma agregados + outbox), `InvariantConstraintMap` (23514/23P01/23505 ⇒ `ConflictException` con el `code` del INV, leído del nombre de la restricción; concurrencia ⇒ 409), `EfOutbox`/`EfInbox`, `OutboxProcessor<TContext>` (cada 1 s, 20 mensajes, `FOR UPDATE SKIP LOCKED`, 5 intentos), `InMemoryEventBus` (un scope por handler, decorado con el inbox del módulo del handler), `IntegrationEventSerializer`, `IntegrationEventLog`, `PeriodicJob`.
- **Por módulo**: `QuotesDbContext` (`quotes`), `EventLifecycleDbContext` (`event_lifecycle`), `HallScheduleDbContext` (`hall_schedule`), cada uno con su historial de migraciones, `outbox_messages` e `inbox_messages` en su schema; configuraciones Fluent, repositorios EF, implementaciones de las `I*Queries` y los puertos de UoW / outbox / inbox; `QuoteExpirationJob` y `HallEventExpirationJob` (cada 30 s, respetan el reloj de demo).
- **Migraciones** (`InitialCreate` + `SeedDemoData` por módulo). `Nodo.Api` aplica `MigrateAsync()` de los tres al arrancar (HallSchedule, Quotes, EventLifecycle) con hasta 15 reintentos.
- **API de demo**: `POST /api/demo/jobs/run` (ejecuta los dos jobs ya), `GET /api/demo/events?limit=` (registro unificado de eventos de integración con su estado de entrega).
- **Nodo.Api** ya registra MediatR sobre los tres ensamblados de Application, `AddXxxApplication()` y `AddXxxInfrastructure()` (se adelantó de la fase 05 porque los jobs y el outbox lo necesitan para correr).

### Semillas (`SeedDemoData`, D = hoy en La Paz)
| Cotización | Evento | Cliente | Fecha y franja | Total (BR-06..08) | Estado |
|---|---|---|---|---|---|
| Q1 | — | Ana (5 %) | D+12, 09–13, 40 pers. | 1.710 | Issued |
| Q2 | — | Carlos (plan vencido) | D+8, 10–12, 30 pers., emitida D−6 | 1.000 | Expired |
| Q3 | E1 | Fundación Crecer | D+10, 14–18, 60 pers. | 1.800 | E1 `Requested`, bloqueo provisional |
| Q4 | E2 | TechSol (10 %) | D+14, 09–15, 70 pers. | 2.160 | E2 `ApprovedByManagement` 30 % = 648, pagado 324; provisional |
| Q5 | E3 | Ana (5 %) | D+7, 10–14, 35 pers. | 3.085 (proyector 150 + 35 coffee × 35) | E3 `Confirmed` 20 % = 617 pagado; definitivo con proyector |
| Q6 | E4 | TechSol | D−5, 08–16, 80 pers. | 2.520 | E4 `Completed` 50 % = 1.260; definitivo |
| Q7 | E5 | TechSol | D+20, 10–15, 50 pers. | 2.000 (luces 200) | E5 `Cancelled`, crédito 600; bloqueo liberado |
| Q8 | E6 | Carlos | D+9, 14–17, 25 pers. | 1.350 | E6 `Rejected`; bloqueo liberado |

`customer_credits` de TechSol = 600 con su movimiento `Granted`. Los totales están comentados a mano en cada migración.

## Fase 05 — resumen
- **`Nodo.External.Simulated`**: Espacios, Membresías, Pasarela de Pagos, Facturación, Control de Acceso, Catering y Notificaciones, cada uno con su propio vocabulario (centavos, `ACTIVE`, `merchant_ref`…). Solo conoce los `*.Contracts` (regla de arquitectura nueva). Consumidores downstream por evento de integración, idempotentes: credencial de acceso (PIN de 6 dígitos, vigencia desde 1 h antes hasta el fin, aforo = asistentes), libro de Facturación (depósito, saldo a favor, liquidación final), orden de catering (solo líneas de catering), link de pago por el depósito o la diferencia tras reprogramar, y notificaciones en español para los 19 eventos.
- **Adaptadores** (en la Infrastructure de cada consumidor): `Quotes` (membresía = Conformist, catálogo de Espacios, directorio de clientes), `HallSchedule` y `EventLifecycle` (stock de equipamiento) y el **ACL de la pasarela** (`IPaymentGatewayAcl`: payload del proveedor → `AccreditDepositCommand`; centavos → Bs, `DECLINED` no llega al dominio, forma inválida → 400 con el campo).
- **API REST** (34 operaciones, todas con resumen, descripción, etiqueta, ejemplos de petición/respuesta y respuestas de error con ejemplos reales): cotizaciones, eventos, calendario y disponibilidad, saldo a favor, `POST /api/integrations/payment-gateway/webhook`, `/api/external/*` (membresías, links de pago y su botón de pago, credenciales, ledger, catering, notificaciones), `/api/demo/*` (reloj, jobs, `integration-events`). Cada operación lista las INV-xx que puede disparar (`[Invariants]`). `ProblemMapping` es la única fuente del mapeo error → HTTP.
- **Laboratorio de invariantes**: `GET /api/demo/invariants` (catálogo de las 22) y `POST /api/demo/invariants/{id}/probe`. Cada probe ejecuta el caso de uso real dentro de una transacción compartida por los 3 DbContexts que **siempre se revierte**, con reloj desplazado solo en ese scope; el estado de la demo queda idéntico (verificado: conteos de BD, outbox y notificaciones iguales antes y después). 19 se rechazan con su `code` exacto; INV-03, INV-15 e INV-22 son "por diseño" (ningún endpoint puede violarlas) y el probe lo explica.
- **Cambio de dominio necesario**: reprogramar ahora recibe solo la nueva fecha y hora de inicio y conserva la duración (`HallEvent.SlotMovedTo`, BR-23); `HallEventRescheduledWithCredit` lleva el depósito pendiente y su plazo para que la pasarela cree el link por la diferencia.

### Criterios de aceptación (spec 05) — verificados en Docker con `docker compose up --build`
- [x] Ciclo completo: preview → emitir → aceptar → solicitud → aprobar 30 % → pago parcial por webhook HTTP → pago del resto → `Confirmed` → credencial, orden de catering, ledger y bloqueo definitivo.
- [x] Rama A (rechazo + liberación del bloqueo), rama B (cancelación: todo lo acreditado pasa a saldo a favor, sin devolución, credencial revocada, ledger) y rama C (reprogramación C1 con saldo suficiente y C2 con diferencia + nuevo link de pago).
- [x] `txn_id` repetido: 200 sin acreditar de nuevo (dos reenvíos); `DECLINED` no acredita; `merchant_ref` inválido 400; evento inexistente 404.
- [x] 422 con `code` y mensaje en español para cada regla probada (depósito 19 %, motivo corto, cancelación repetida, reprogramación no permitida, inicio fuera de ventana, duración de 9 h…).
- [x] Reloj de demo: iniciar y completar el evento, liquidación final = total − depósito, línea de tiempo completa.
- [x] Eventos de integración: los tres módulos publican, todos entregados, ninguno con errores.
- [x] 22 probes sin alterar el estado; OpenAPI sin advertencias y con todas las operaciones documentadas (auditado contra el contenedor).
- [x] Tests: ACL de pagos, adaptadores de membresía y espacios, ACL de acceso (ventana y aforo), ledger, catering, links de pago, notificaciones, catálogo de invariantes contra `InvariantCodes`, mapeo de errores, ejemplos de OpenAPI, reloj acotado y reglas de arquitectura de `External.Simulated`.

## Fase 06 — resumen
- **Stack** (`web/`): Vite 8, React 19.3, TypeScript strict, TanStack Query 5, React Router 8, Tailwind 4, componentes estilo shadcn/ui (Radix: diálogo y pestañas), Sonner (toasts), FullCalendar 6.1.22 (`timegrid` + `interaction`; la serie 7 aún es RC), `lucide-react`.
- **Estructura** como el spec: `api/` (cliente `fetch` tipado a mano por recurso; `ApiProblem` convierte el `ProblemDetails` en mensaje en español + `code`), `features/{quotes,events,approvals,operations,calendar,external-panel,invariants}`, `components/`, `context/` (rol y cliente en `localStorage`), `lib/`.
- **Pantallas**: Cotizar (desglose en vivo vía `/api/quotes/preview`, aviso de disponibilidad, aforo sin tope en el front para ver el 422), Mis cotizaciones / Cotizaciones (cuenta regresiva con el reloj de demo, "Vencida" sin poder aceptar), Mis eventos / Eventos (filtro por estado), detalle del evento (línea de tiempo, depósito con barra, extras, saldo a favor, cancelar con aviso de "sin reembolso", reprogramar con disponibilidad), bandeja de Dirección (slider 20–50 % con el monto, rechazo con motivo), Eventos de hoy de Operaciones (checklist de equipos / catering / credencial), calendario (ámbar provisional, verde definitivo, buffers rayados, clic en hueco → Cotizar), panel de sistemas externos (6 pestañas, pago parcial y "Rechazar pago") y Laboratorio de invariantes ("Intentar violar", "Probar todas" → `n/n protegidas`).
- **Reloj de demo** en el header: +1 h, +1 día, +4 días, Restablecer, Ejecutar jobs. Avanzar el reloj ejecuta además los jobs de expiración, para que lo vencido se vea al instante.
- **Polling** cada 2,5 s en eventos, calendario, panel externo y bandejas (la consistencia eventual se ve en pantalla).
- **Docker**: `web/Dockerfile` (`node:22-alpine` → `nginx:alpine`), `nginx.conf` (fallback SPA + proxy `/api/` → `api:8080`), `web/.dockerignore` y servicio `web` en `docker-compose.yml` (3000, `depends_on: api` healthy). El calendario se carga bajo demanda (chunk aparte).

### Criterios de aceptación (spec 06) — navegador real (Edge headless) contra `http://localhost:3000`
- [x] Calendario con E1 y E2 provisionales, E3 definitivo y buffers.
- [x] Guion completo: Ana cotiza (3.260 con proyector y coffee break) y acepta → Dirección aprueba 30 % (978) → pasarela en dos pagos (400 y el resto) → Confirmado, credencial con PIN, orden de catering y depósito en el ledger → +20 días → Operaciones inicia y finaliza → ledger con `LiquidacionFinalCompletada` por Bs 2.282.
- [x] Rama A (rechazo con motivo corto: 422 visible), rama B (Fundación cancela un evento confirmado: aviso sin reembolso y saldo a favor) y rama C (TechSol reprograma E5 con sus Bs 600).
- [x] Cotización vencida tras +4 días: estado "Vencida" y ningún "Aceptar" habilitado.
- [x] Errores de regla con el mensaje en español y el código de la API (aforo 90 → 422).
- [x] Laboratorio: "Probar todas" → **22/22 protegidas**; "Intentar violar" por fila con código, mensaje y HTTP 422.
- [x] Todo funciona tras `docker compose up --build`, sin nada fuera de Docker.
- [x] `npm run lint` (0 avisos) y `npm run build` sin errores. La verificación en navegador se hizo con Playwright instalado **fuera del repo**; no se añadieron tests de front (CLAUDE.md: solo unit tests de backend).

## Fase 07 — resumen
- **`README.md`**: qué es, mapa de módulos, context map (Mermaid con U/D, ACL, Conformist, OHS/PL, Published Language), requisitos,
  arranque en pocos comandos con todas las URLs (app, API, Scalar, PostgreSQL, pgAdmin), las 9 decisiones **[Decisión]** con su
  justificación, los valores inventados, y la tabla de trazabilidad documento → código (rutas verificadas una por una).
- **`docs/demo-script.md`**: 10 pasos (~9–10 min), cada uno con qué se hace en pantalla, qué decir y el concepto DDD. El bloque de
  invariantes incluye Laboratorio, cuatro violaciones desde formularios reales, una en Scalar y SQL para pgAdmin (probado).
- **`docs/architecture.md`**: capas por módulo, context map, flujo Outbox/Inbox y la secuencia «confirmar evento en firme».
- **`docs/invariants.md`**: matriz `INV-01..22` → regla → dueño → `code` → tests → restricción de BD → sonda. El test
  `InvariantDocumentationTests` (5 tests) la contrasta con `InvariantCatalog`: mismos ids, mismos `code`, refuerzo en BD coherente con los
  nombres de restricción (`..._invNN_...`) y que cada clase de test citada exista. Comprobado que falla si se altera un `code` en el documento.
- Los 5 diagramas Mermaid se validaron con el parser de Mermaid.
- **Cambios de código derivados de recorrer el guion** (no son nuevas funciones: corrigen discrepancias con los specs):
  - Dirección: campo numérico junto al slider del depósito, para poder escribir 60 % y ver el 422 (spec 07, bloque de invariantes).
  - «Cancelar» también se ofrece desde `Requested` (BR-21 lo permite; el front no lo hacía).
  - Avisos (toasts) abajo a la derecha: arriba tapaban los botones del reloj de demo.
  - El descuento de membresía se muestra como `− Bs 90,00` (antes `Bs -90,00`).
  - Fixtures de tests sin `DateTime.UtcNow` (fechas fijas).

### Verificación final
- [x] **Clon limpio**: copia del repo sin `bin`, `obj`, `node_modules`, `dist` ni `.env`; **solo** `docker compose up --build` deja `http://localhost:3000` operativo con semillas. También con **build en frío** (`docker compose build --no-cache`, 38 s en esta máquina).
- [x] **`docker compose down -v && docker compose up --build`** reproduce el estado inicial: instantánea de eventos, cotizaciones, bloqueos y saldos idéntica (0 diferencias) tras un recorrido completo que la altera (21 → 37 filas), y sin mensajes pendientes en los outbox.
- [x] `dotnet build Nodo.slnx` → **0 advertencias, 0 errores**; `dotnet test Nodo.slnx` → **856 tests en verde** (15 SharedKernel · 108 arquitectura · 121 Quotes · 300 EventLifecycle · 81 HallSchedule · 231 infraestructura/API); `npm run lint` sin avisos y `npm run build` sin errores.
- [x] **Guion recorrido completo** en navegador real contra la copia limpia: 21/21 comprobaciones (pasos 2–8). Único error de consola: los 422 provocados a propósito. En la API: ninguna excepción no controlada; los `warn` son el aviso de licencia de MediatR y las reglas de dominio rechazadas a propósito. (Al crear la BD vacía EF registra 2 `fail` al consultar su tabla de historial antes de crearla.)
- [x] **Revisión de reglas** (búsquedas sobre el código): `DateTime.Now/UtcNow` solo en `DemoClock`; sin `double`/`float` en montos (los únicos son `HoursRemaining` y `OffsetHours`, horas); sin `set`/`init` públicos en agregados, VOs ni SharedKernel (más `DomainModelProtectionTests` por reflexión); ningún módulo referencia los internos de otro (`DependencyRulesTests`).

### Deuda conocida
1. **Sin tests de integración ni de front** (CLAUDE.md: solo unitarios). Los criterios de BD (`CHECK`, `EXCLUDE`, índice parcial) y la UI se verificaron a mano / con un navegador automatizado externo al repositorio.
2. **409 transitorio al reprogramar justo después de cancelar** (≲ 2 s): la liberación del bloqueo es eventual (outbox) y el índice único parcial de INV-19 rechaza el segundo bloqueo. Reintentar un instante después funciona.
3. **Mover el reloj de demo es irreversible para lo vencido**: «Restablecer» devuelve el reloj, pero las cotizaciones y solicitudes que ya expiraron siguen expiradas. Por eso el guion indica `./scripts/reset.sh` antes de cada toma.
4. **El reloj conserva la hora actual** al adelantar días: para ejecutar un evento hay que elegir su hora de inicio cerca de la hora local, o ajustar con «+1 h» (documentado en el guion).
5. **Estado de los contextos externos en memoria**: credenciales, ledger, órdenes de catering y notificaciones se pierden al reiniciar la API (las semillas no los crean; por diseño, son simulaciones).
6. **Sin autenticación** (fuera de alcance): el rol es un selector de la UI y la API no valida permisos (BR-24).
7. **Un salón único**: el `HallId` está fijo en las semillas y en el front (`HALL_ID`).
8. **FullCalendar 6 y TypeScript 5.9** hasta que salgan versiones estables de FullCalendar 7 y de `typescript-eslint` para TS ≥ 6.1.
9. **Fuera de alcance por spec**: despliegue en la nube y CI/CD.

## Desviaciones respecto a los specs

**Fase 07**
1. **Sin script de verificación en el repositorio** (decisión del plan): la verificación del guion con navegador automatizado se hizo con herramientas externas al repo; el documento queda como referencia reproducible a mano.
2. **Guion reordenado respecto a la lista del spec** en un punto: el vencimiento por reloj va al final de la fase I (adelantar 4 días vence solicitudes y cotizaciones pendientes de forma permanente), seguido de «Restablecer» y de una cotización nueva para continuar.

**Fase 06**
1. **FullCalendar 6.1.22 y no 7**: los plugins de la serie 7 siguen en RC; React 19 está soportado en la 6.
2. **TypeScript 5.9** (no 7): `typescript-eslint` aún no soporta TS ≥ 6.1.
3. **Eventos completados** salen de "Eventos de hoy" (la API solo lista confirmados y en curso); se confirma con el toast "Evento finalizado" y el evento sigue visible en Eventos.
4. **Reprogramar** solo se ofrece sobre eventos `Cancelled` (la regla de negocio exige saldo a favor generado por una cancelación); el 422 de la API explica cualquier otro caso.

**Fase 05** (las 2 preguntas del plan se resolvieron con las recomendaciones)
1. **INV-17 (descuento de membresía ≤ 10 %)**: ningún dato semilla puede violarla, así que el probe inyecta temporalmente un socio con 15 % en `MembershipsContext` (`AddTemporarily`, se retira al terminar) y pide una cotización real; el dominio responde 422.
2. **INV-21**: el probe recorre la secuencia completa (aprobar → pagar → confirmar → intentar iniciar fuera de ventana) con el reloj desplazado solo en su scope.
3. **Reprogramación**: el comando pasa de `(newDate, newStart, newEnd)` a `(newDate, newStart)`; la duración se conserva por regla de negocio, no por convención del cliente.
4. **`Api__SelfBaseUrl = http://localhost:8080`** en Docker: la pasarela simulada llama a su propio webhook desde dentro del contenedor (el 5080 solo existe en el host).
5. **`Microsoft.EntityFrameworkCore.Relational` fijado a 10.0.14** (mismo que EF Core): Npgsql arrastraba la 10.0.4 y aparecía el warning MSB3277 al referenciar la Api desde los tests. No es un paquete nuevo.
6. **Tests de infraestructura referencian `Nodo.Api`** (con `InternalsVisibleTo`) para probar catálogo, probes, mapeo de errores y ejemplos sin duplicar código; siguen siendo unit tests sin BD.

**Comportamiento a conocer (no es un defecto)**: la liberación del bloqueo tras cancelar es eventual (outbox, ~1–2 s). Reprogramar el mismo evento dentro de ese intervalo recibe un 409 transitorio (`SCHEDULE.DUPLICATE_BLOCK`, INV-19 defendida por el índice parcial único); reintentando un instante después funciona.

**Fase 04** (las 4 preguntas del plan fueron aprobadas con las recomendaciones)
1. **Transacciones del depósito**: columna `deposit_transaction_ids text[]` dentro de `hall_events` en vez de una tabla hija (el spec la pedía hija). Es parte del valor inmutable de `GuaranteeDeposit`; el dominio impide acreditar dos veces el mismo id y `xmin` serializa los pagos concurrentes.
2. **Q8 añadida** a las semillas (el spec lista Q1..Q7): E6 también necesita la cotización aceptada de la que nació.
3. **Registro global de eventos = unión de los tres outbox** (no una tabla `integration_event_log`): los outbox no se purgan y ya guardan tipo, contenido y estado; una tabla aparte duplicaría escrituras y exigiría una transacción entre schemas.
4. **Fechas de las semillas con la fecha local de La Paz** (`now() AT TIME ZONE 'America/La_Paz'`) y no `CURRENT_DATE`: en Docker `CURRENT_DATE` es UTC y entre las 20:00 y las 24:00 locales difiere de la que usa la app. E1 se solicita "ahora" para que su plazo (fin del día local) esté por delante.
5. **Value objects como *complex types* de EF, no owned**: un owned se rastrea por identidad y `GuaranteeDeposit.Accredit` / `CancellationPolicy` reutilizan el mismo `Money` en varios sitios; EF dejaba en `null` los compartidos y `AccreditDeposit` fallaba con `NullReferenceException` justo después de guardar. Lo detectó el sondeo contra la BD real, no los tests unitarios. Complex type = semántica por valor. Excepción: los `Money` dentro de las colecciones owned (`quote_extra_services`, `customer_credit_movements`) siguen siendo `OwnsOne` porque EF no admite complex dentro de owned; ahí cada elemento crea su propio `Money`.
6. **`Attendance`** se persiste con un *value converter* que pasa por `Attendance.Create`: una fila corrupta nunca se convierte en un VO inválido.
7. **Columnas `buffered_start` / `buffered_end` generadas por PostgreSQL** (`GENERATED ALWAYS AS … STORED`) en lugar de rellenarlas la aplicación: nadie puede olvidarlas o calcularlas mal, y se evita el desborde de `TimeOnly` a las 24:00.
8. **Restricciones**: `CHECK` por `HasCheckConstraint` y el índice parcial único (INV-19) por `HasFilter`; `EXCLUDE` (INV-18) y `btree_gist` por SQL en `InitialCreate`. Además del catálogo: INV-01 también en `quotes` y `schedule_blocks`, INV-04 también en `quotes`, y `ck_schedule_blocks_same_day`.
9. **Inbox como decorador genérico** (`InboxIntegrationEventHandler`, en `BuildingBlocks.Application`): registra el mensaje en el DbContext del consumidor y lo confirma con el mismo `SaveChanges` del handler (atómico); si el handler falla no queda marcado y se reintenta. Por eso `QuoteAcceptedHandler` ya no llama al inbox él mismo (cambio de la fase 03; sus tests ahora usan el decorador).
10. **Cambios aditivos al dominio / aplicación**: `IAggregateRoot` (SharedKernel), constructores privados sin parámetros en los VOs con VOs anidados (`PriceBreakdown`, `ExtraServiceLine`, `GuaranteeDeposit`, `CreditMovement`), `HallSchedule` se reconstruye con `(hallId, date)` y expone `HallId`/`Date` como propiedades reales, `ConflictException.Code`, `IUnitOfWork.HasPendingChanges`, `IHallScheduleInbox`. Los tests de reflexión siguen exigiendo cero constructores y setters públicos.
11. **Contenedor DI**: `ValidateOnBuild = false` hasta la fase 05 (faltan los adaptadores externos).
12. **`dotnet-ef` como herramienta local** (`dotnet-tools.json`, v10.0.14) para generar migraciones; no es un paquete de la solución.
13. **Proyecto nuevo `Nodo.Infrastructure.UnitTests`** (aprobado): contratos, inbox, bus, traductor de restricciones y modelo de EF.
14. **Corrección de un fallo de la fase 01**: `scripts/reset.ps1` fallaba en Windows PowerShell 5.1 porque `docker` escribe su progreso en stderr y el script tenía `$ErrorActionPreference = 'Stop'`; ahora decide por el código de salida.
15. La **traza del historial** (`hall_event_history`) se carga en el orden físico de las filas; son filas inmutables que solo se insertan, pero no hay un `ORDER BY` explícito al hidratar el agregado.
16. El **snapshot de migraciones** se generó antes de pasar los VOs a complex types; el esquema relacional es idéntico, por eso EF no detecta cambios y el test de sincronización pasa. La próxima migración lo regenera.

**Fase 03** (las 3 recomendaciones del plan fueron aprobadas)
1. **Cambios aditivos al dominio** (con tests):
   - `Quote.Accept(nowUtc, todayLocal)` exige la ventana BR-05 (`QUOTE.DATE_OUT_OF_RANGE`): una cotización que no podría convertirse en solicitud no se acepta.
   - `MembershipDiscountPolicy.Effective(isActive, planPercent)`: BR-07 ("solo con membresía activa") vive en el dominio, no en un handler.
   - `HallEvent.EnsureCanReschedule(newSlot, todayLocal)`: las precondiciones de la reprogramación, para fallar antes de tocar el crédito o el calendario; `Reschedule` la reutiliza.
   - `IQuoteRepository.ListDueForExpirationAsync`, `IHallEventRepository.ListOverdueAsync` (jobs) y `AvailabilityChecker.Check(..., excludeEventId)` (lo pide el contrato del spec).
2. **Eventos de integración**: se publican los 19 eventos de dominio (3 + 13 + 3), no solo los 6 de §7. `EventRequestRejected`, `HallEventCancelledWithCredit` y `HallEventExpired` llevan además `HallId` y `Date` (HallSchedule los necesita para localizar la agenda del día). Los montos son `decimal` en BOB, sin moneda en el payload.
3. **`ISpacesCatalog` y `ICustomerDirectory` se definen por módulo** con solo lo que cada uno necesita (cada módulo ve a los contextos externos con su propio modelo). `IMembershipDirectory` solo en Quotes.
4. **`performedBy` en Quotes** (`IssueQuote`, `AcceptQuote`) se valida como requerido pero no se persiste: `Quote` no tiene campo de auditoría (solo `HallEvent` tiene historial).
5. **Servicio extra o salón desconocido al cotizar** ⇒ `NotFoundException` (404). Cantidad por defecto: 1 para equipos, aforo para catering (BR-08).
6. **Orden de operaciones**:
   - `QuoteAcceptedHandler`: inbox ⇒ crea el evento y **guarda** ⇒ bloqueo síncrono ⇒ si falla, rechaza con "Sin disponibilidad al registrar la solicitud" y guarda. Guardar primero evita bloqueos sin evento.
   - `AcceptQuote`: `quote.Accept` (valida vigencia/estado/ventana) ⇒ comprobación de disponibilidad ⇒ guardar. Así una cotización vencida da `QUOTE.EXPIRED`, no un error de calendario.
   - `RescheduleHallEvent`: `EnsureCanReschedule` ⇒ `CheckAvailability` (excluyendo el propio evento) ⇒ aplicar saldo ⇒ `Reschedule` ⇒ bloqueo definitivo si queda `Confirmed`, provisional si no ⇒ guardar. Si algo falla antes de guardar no se persiste nada.
7. **`CancelHallEvent`** admite motivo opcional; si no generó crédito (nada pagado) no se abre `CustomerCredit`.
8. **Los asignados a EF no existen aún**: nada de lo que implementan los puertos (repositorios, unit of work, outbox/inbox, queries, adaptadores externos) está hecho; los tests usan fakes.
9. **`Nodo.Api` no registra todavía** los ensamblados de Application ni `AddXxxApplication()`: sin los adaptadores de las fases 04/05 el contenedor fallaría al validar en `Development`. Se cablea en la fase 05.
10. **Paquetes**: `Microsoft.Extensions.DependencyInjection` en los 3 proyectos de tests de módulo (aprobado); `FluentValidation.DependencyInjectionExtensions` en los 3 `*.Application` (ya estaba en el stack). `BuildingBlocks.Application` pasa a referenciar `SharedKernel`.
11. **Dobles de prueba compartidos** en `tests/Shared/TestDoubles.cs`, enlazados (no es un proyecto nuevo) en los 3 proyectos de tests de módulo.

**Fase 01**
- Raíz del repo: `D:\Proyectos\Nodo\nodo-specs\nodo-specs` (hay una carpeta anidada); confirmado.
- Tests de `Money` en `Nodo.SharedKernel.UnitTests` en vez de `ArchitectureTests`.
- Tests de arquitectura leen los `.csproj` para las reglas de referencias (el compilador descarta referencias sin uso).
- Referencias `Application → Contracts` de otros módulos: Quotes→HallSchedule, EventLifecycle→Quotes+HallSchedule, HallSchedule→EventLifecycle.
- El Dockerfile instala `curl` solo para el healthcheck. Docker Desktop se instaló en esta máquina vía winget.

**Fase 02** (las propuestas 1–6 fueron aprobadas antes de implementar)
1. **Códigos compartidos**: Quotes y HallSchedule reutilizan los strings `EVENT.DURATION_OUT_OF_RANGE`, `EVENT.ATTENDANCE_OUT_OF_RANGE` y `EVENT.EXTRA_SERVICE_INVALID` (un solo `code` por invariante, lo lance el módulo que lo lance). `HallEvent.Reschedule` y `HallEventFactory` reutilizan `QUOTE.SLOT_OUTSIDE_OPERATING_HOURS` y `QUOTE.DATE_OUT_OF_RANGE`.
2. **Código nuevo fuera del catálogo**: `QUOTE.INVALID_STATE_TRANSITION` (aceptar una cotización ya aceptada). El catálogo no cubre ese caso.
3. **VOs duplicados por módulo** (`TimeSlot`, `Attendance`, ids): un Domain no puede referenciar el de otro. Cada copia valida lo mismo con el mismo `code`.
4. **Firmas que difieren del spec** (necesarias para auditoría o para validar):
   - `HallEvent.Start(nowUtc, performedBy)` y `Complete(nowUtc, performedBy)`: el historial necesita el actor.
   - `HallEvent.Reschedule(newSlot, creditApplied, by, nowUtc, todayLocal)`: BR-05 necesita la fecha local.
   - `HallEventFactory.FromAcceptedQuote(snapshot, nowUtc, todayLocal, equipmentStock)`: INV-13 necesita el stock.
   - Repositorios asíncronos con `CancellationToken`.
5. **`GuaranteeDeposit.Accredited` nunca supera `Required`**: lo que sobra se devuelve como excedente (`AccreditDeposit` retorna `Money`) para `CustomerCredit.Grant`. Así el excedente no se cuenta dos veces (ni en `RemainingToSettle` ni al cancelar).
6. **Plazos exclusivos**: Dirección decide estrictamente antes de las 00:00 locales del día siguiente (23:59:59 vale, 00:00:00 no); el depósito vence a las 48 h exactas (47:59:59 vale, 48:00 no).
7. **`Expire` antes de su plazo** lanza `EVENT.INVALID_STATE_TRANSITION` (es un error del llamador, no una no-operación); si ya está `Expired` es idempotente.
8. **Códigos por comando en la matriz INV-06**: donde el catálogo define un código propio se usa ese (cancelar → `CANCELLATION_NOT_ALLOWED`, acreditar → `DEPOSIT_NOT_ACCEPTED`, reprogramar → `RESCHEDULE_NOT_ALLOWED`); el resto usa `INVALID_STATE_TRANSITION`. `ConfirmFirmly` desde `Requested`/`ApprovedByManagement` sin condiciones → `CONFIRMATION_PRECONDITIONS_NOT_MET` (INV-03); desde estados posteriores → `INVALID_STATE_TRANSITION`.
9. **`CustomerCredit`** se identifica por `CustomerId` (uno por cliente). `Grant` con monto 0 se ignora; `Apply` lanza `CREDIT.INSUFFICIENT` si el saldo o el monto pedido es 0, y con saldo parcial aplica lo disponible.
10. **Cancelar sin depósito** pasa a `Cancelled` con crédito 0 y levanta igualmente `HallEventCancelledWithCredit` (con `CreditAmount = 0`).
11. **Reprogramación**: el crédito aplicado se registra como un `GuaranteeDeposit` nuevo con `TransactionId` sintético `credit-{eventId}-{n}` y el mismo porcentaje; `CreditGenerated` vuelve a 0 (ya vive en `CustomerCredit`); `ExtraServicesAssigned` vuelve a `false` para que BR-20 se reaplique.
12. **HallSchedule no valida BR-02** (horario operativo): lo validan `Quote` y `HallEvent` antes de llegar, y el catálogo no le asigna código. `TimeSlot` de HallSchedule sí valida INV-01 y calcula los buffers con enteros (minutos) para no desbordar `TimeOnly` a las 23:00.
13. **El equipo se reserva al colocar el bloqueo** (`PlaceProvisionalBlock/PlaceDefinitiveBlock` reciben las asignaciones). No se añadió un método aparte para BR-20; la fase 03 decide cuándo se pasan.
14. Los agregados y entidades tienen un constructor privado sin parámetros "solo para EF"; los VOs no (la fase 04 los mapea con constructor binding / complex types).
15. El test por reflexión vive en `Nodo.ArchitectureTests` (no triplicado en cada proyecto de módulo).

## Trazabilidad INV → clase dueña → tests

Código del test: `H` = `HallEventInvariantTests`, `L` = `HallEventLifecycleTests`, `M` = `HallEventStateMachineTests`, `C` = `CustomerCreditInvariantTests`, `D` = `GuaranteeDepositTests` (todos en `Nodo.EventLifecycle.UnitTests`); `Q` = `QuoteInvariantTests` (`Nodo.Quotes.UnitTests`); `S` = `HallScheduleInvariantTests` (`Nodo.HallSchedule.UnitTests`); `K` = `MoneyTests` (`Nodo.SharedKernel.UnitTests`). Los tests de una invariante llevan `_InvNN_` en el nombre; hay válido, límite e inválido en cada una.

| INV | Dueño en el dominio | Code | Tests |
|---|---|---|---|
| INV-01 | `TimeSlot` (los 3 módulos) | `EVENT.DURATION_OUT_OF_RANGE` | H, Q, S: 1 h y 8 h ok; 9 h, 0 h, fin < inicio y minutos ≠ 0 fallan |
| INV-02 | `GuaranteeDeposit.Create` | `EVENT.DEPOSIT_PERCENTAGE_OUT_OF_RANGE` | H, D: 20 % y 50 % ok; 19 %, 51 %, 0, 100 fallan; estado intacto |
| INV-03 | `HallEvent.ConfirmFirmly` | `EVENT.CONFIRMATION_PRECONDITIONS_NOT_MET` | H, M: sin aprobar, depósito parcial, 599,99 no confirma, 600 confirma |
| INV-04 | `Attendance` | `EVENT.ATTENDANCE_OUT_OF_RANGE` | H, Q: 1 y 80 ok; 0 y 81 fallan |
| INV-05 | `HallEvent.Cancel` + `CancellationPolicy` | `EVENT.CANCELLATION_NOT_ALLOWED` | H, L: crédito = acreditado, sin depósito = 0, estados no cancelables, 1 s antes del inicio ok / en el inicio falla |
| INV-06 | `HallEvent` (máquina de estados) | `EVENT.INVALID_STATE_TRANSITION` | M: matriz estado × comando (59 pares no permitidos), transiciones permitidas, estados finales |
| INV-07 | `HallEvent.Approve/Reject` | `EVENT.DECISION_DEADLINE_EXCEEDED` | H, L: 23:59:59 ok / 00:00:00 falla; plazo en hora local, no UTC |
| INV-08 | `HallEvent.Reject` | `EVENT.REJECTION_REASON_REQUIRED` | H: 10 caracteres ok; null, vacío, 9 caracteres, con espacios fallan |
| INV-09 | `HallEvent.AccreditDeposit` + `GuaranteeDeposit` | `EVENT.DEPOSIT_NOT_ACCEPTED` | H, D, L: solo en `ApprovedByManagement`, transacción repetida idempotente (también tras confirmar), plazo de 48 h exacto |
| INV-10 | `Money`, `GuaranteeDeposit` | `MONEY.CURRENCY_MISMATCH` / `MONEY.NEGATIVE_AMOUNT` | K, H, D, C: otra moneda, monto negativo, pago en 0 |
| INV-11 | `HallEvent.Start` | `EVENT.START_OUTSIDE_WINDOW` | H, L: inicio − 1 h ok / −1 h −1 s falla; fin − 1 s ok / fin falla |
| INV-12 | `HallEvent.Reschedule` | `EVENT.RESCHEDULE_NOT_ALLOWED` | H, L, M: solo `Cancelled` con crédito > 0; duración distinta; crédito 0 o > requerido; conserva aforo, extras y total |
| INV-13 | `HallEventFactory` + `ExtraServiceItem` (solo vía la raíz) | `EVENT.EXTRA_SERVICE_INVALID` | H, Q: equipo ≤ stock ok / > stock falla; cantidad < 1; código repetido; colección de solo lectura, sin ctor público |
| INV-14 | `Quote.Accept/IsExpiredAt` | `QUOTE.EXPIRED` | Q: 95:59 ok / 96:00 falla; ya marcada `Expired`; estado intacto |
| INV-15 | `Quote` (sin método que cambie precio/servicios) | por diseño | Q: el precio no cambia al aceptar; reflexión: los únicos métodos públicos son `Accept`, `Expire`, `IsExpiredAt` |
| INV-16 | `Quote.Issue` / `BookingPolicy` | `QUOTE.SLOT_OUTSIDE_OPERATING_HOURS` / `QUOTE.DATE_OUT_OF_RANGE` | Q, H: inicio 08:00 ok / 07:00 falla; fin 22:00 ok / 23:00 falla; hoy+3 y hoy+180 ok; hoy+2 y hoy+181 fallan; fecha local, no UTC |
| INV-17 | `PriceBreakdown` / `EventPricingCalculator` | `QUOTE.INVALID_DISCOUNT` | Q: 0, 5, 10 ok; −1 y 11 fallan; el descuento no toca los extras |
| INV-18 | `HallSchedule` + `TimeSlot.OverlapsWithBuffers` | `SCHEDULE.SLOT_OVERLAP` | S: solape directo, solo por buffer (10–12 vs 13–15 falla, 14–16 ok), en ambos sentidos, liberado no bloquea |
| INV-19 | `HallSchedule.Place*` | `SCHEDULE.DUPLICATE_BLOCK` | S: segundo bloqueo activo falla; tras liberar se puede |
| INV-20 | `AvailabilityChecker` | `SCHEDULE.EQUIPMENT_UNAVAILABLE` | S: stock exacto ok; stock + 1, código desconocido y líneas repetidas suman y fallan |
| INV-21 | `CustomerCredit.Apply` | `CREDIT.INSUFFICIENT` | C: parcial, igual al saldo, mayor al saldo (aplica lo disponible), sin saldo falla; nunca negativo |
| INV-22 | `CustomerCredit` (sin método de retiro) | por diseño | C: reflexión, solo `Open`, `Grant`, `Apply`; L: `HallEvent` no expone reembolsos |

## Trazabilidad BR → tests

| Regla | Tests (clase · patrón de nombre) |
|---|---|
| BR-01 | INV-01 (S, Q, H) |
| BR-02 | `QuoteInvariantTests.*Inv16_*OperatingHours*`, `HallEventInvariantTests.Reschedule_Br02_*`, `FromAcceptedQuote_Br02_*` |
| BR-03 | `HallScheduleInvariantTests.*Inv18_*` (buffers) |
| BR-04 | INV-04 (Q, H) |
| BR-05 | `QuoteInvariantTests.*Inv16_Date*`, `HallEventInvariantTests.Reschedule_Br05_*`, `FromAcceptedQuote_Br05_*` |
| BR-06 | `PricingTests.Calculate_Br06_*`, `FactorFor_Br06_*` (los 4 tramos y los 8 valores) |
| BR-07 | `PricingTests.Calculate_Br07_*`, `QuoteInvariantTests.*Inv17_*` |
| BR-08 | `PricingTests.Calculate_Br08_*`, `QuoteInvariantTests.Issue_Br08_*`, INV-13 |
| BR-09 | INV-14; `Expire_*` en `QuoteInvariantTests` |
| BR-10 | `IssueAndPreviewTests.IssueQuote_SlotAlreadyTaken_StillIssuesAndOnlyInformsAvailability_Br10` (informa, no bloquea), `AcceptExpireAndQueryTests.AcceptQuote_NoAvailability_FailsWithTheScheduleCodeAndSavesNothing_Br10` y `AcceptQuote_ChecksAvailabilityOfTheQuotedSlotAndEquipment_Br10` (exige) |
| BR-11 | `HallEventLifecycleTests.FromAcceptedQuote_Br11_*`, `QuoteBehaviorTests.Accept_*` (el snapshot completo) |
| BR-12 | INV-07; `Expire_Br12_*`, `Approve_Br12_*` |
| BR-13 | INV-08; `Reject_Br13_*` |
| BR-14 | INV-02; `GuaranteeDepositTests.Create_Br14_*` |
| BR-15 | INV-09, INV-10; `AccreditDeposit_Br15_*`, `Accredit_Br15_*` |
| BR-16 | INV-03; `AccreditDeposit_Br16_*` |
| BR-17 | `Expire_Br17_*`, INV-09 (48 h) |
| BR-18 | INV-11; `Start_Br18_*` |
| BR-19 | `Complete_Br19_*` |
| BR-20 | `AssignExtraServices_Br20_*` |
| BR-21 | INV-05; `Cancel_Br21_*` |
| BR-22 | `CustomerCreditInvariantTests.Grant_Br22_*` y los de INV-21/22 |
| BR-23 | INV-12; `Reschedule_Br23_*`, `History_CancellationAndReschedule_*` |
| BR-24 | Solo UI (fase 06) |

## Trazabilidad fase 03: casos clave del spec → tests

| Caso del spec | Test |
|---|---|
| Aceptar cotización sin disponibilidad falla | `AcceptExpireAndQueryTests.AcceptQuote_NoAvailability_FailsWithTheScheduleCodeAndSavesNothing_Br10` |
| `QuoteAcceptedHandler` procesado dos veces crea un solo evento | `QuoteAcceptedPolicyTests.Handle_SameMessageTwice_CreatesASingleEvent` |
| Sin disponibilidad al registrar ⇒ evento creado y rechazado por el sistema | `QuoteAcceptedPolicyTests.Handle_HallNoLongerFree_CreatesTheRequestAndRejectsItWithTheSystemReason` |
| `AccreditDeposit` que cubre confirma y registra `HallEventFirmlyConfirmed` en el outbox | `DecisionAndDepositTests.AccreditDeposit_CoveringPayment_ConfirmsAndPublishesEverythingInOneCommit` |
| Pago con excedente crea saldo a favor | `DecisionAndDepositTests.AccreditDeposit_PaymentAboveRequired_GrantsTheExcessAsCustomerCredit_Br15` |
| Cancelar confirmado crea / incrementa `CustomerCredit` | `ExecutionCancelAndExpireTests.Cancel_ConfirmedEvent_GrantsCustomerCreditEqualToWhatWasAccredited`, `Cancel_CustomerAlreadyHasCredit_IncrementsIt` |
| Reprogramar aplica saldo y deja excedente | `RescheduleTests.Reschedule_CreditCoversTheDeposit_ConfirmsWithADefinitiveBlockAndKeepsTheSurplus` |
| Bloqueo síncrono fallido en reprogramación no modifica el evento | `RescheduleTests.Reschedule_NewSlotNotAvailable_FailsBeforeTouchingAnythingAndSavesNothing`, `Reschedule_TheReservationLosesARace_ThrowsAndNothingIsPersisted` |

## Trazabilidad fase 03: evento de integración → test que verifica que llega al outbox

| Módulo | Evento de integración | Test |
|---|---|---|
| Quotes | `QuoteIssued` | `IssueAndPreviewTests.IssueQuote_Outbox_AddsQuoteIssuedIntegrationEventInTheSameCommit` |
| Quotes | `QuoteAccepted` | `AcceptExpireAndQueryTests.AcceptQuote_AvailableSlot_AcceptsAndPublishesTheFullSnapshot` |
| Quotes | `QuoteExpired` | `AcceptExpireAndQueryTests.ExpireDueQuotes_OnlyOverdueIssuedQuotesExpire_AndEachPublishesQuoteExpired` |
| EventLifecycle | `EventRequestCreated` | `QuoteAcceptedPolicyTests.Handle_AcceptedQuote_AddsEventRequestCreatedToTheOutboxInTheSameCommit` |
| EventLifecycle | `HallEventApprovedByManagement` | `DecisionAndDepositTests.Approve_RequestedEvent_ApprovesAndPublishesHallEventApprovedByManagement` |
| EventLifecycle | `EventRequestRejected` | `DecisionAndDepositTests.Reject_ValidReason_RejectsAndPublishesEventRequestRejected` |
| EventLifecycle | `GuaranteeDepositAccredited` | `DecisionAndDepositTests.AccreditDeposit_PartialPayment_DoesNotConfirm` |
| EventLifecycle | `HallEventFirmlyConfirmed` | `DecisionAndDepositTests.AccreditDeposit_CoveringPayment_ConfirmsAndPublishesEverythingInOneCommit` |
| EventLifecycle | `ExtraServicesAssigned` | `DecisionAndDepositTests.AccreditDeposit_Confirmation_AssignsTheExtraServicesInTheSameTransaction_Br20` |
| EventLifecycle | `HallEventStarted` | `ExecutionCancelAndExpireTests.Start_WithinTheWindow_StartsAndPublishesHallEventStarted` |
| EventLifecycle | `HallEventCompleted` | `ExecutionCancelAndExpireTests.Complete_InProgressEvent_CompletesAndPublishesTheAmountLeftToSettle` |
| EventLifecycle | `HallEventCancelledWithCredit` | `ExecutionCancelAndExpireTests.Cancel_ConfirmedEvent_PublishesCancelledAndCreditGrantedInOneCommit` |
| EventLifecycle | `HallEventRescheduledWithCredit` | `RescheduleTests.Reschedule_CreditCoversTheDeposit_PublishesRescheduledConfirmedCreditAppliedAndAssignedInOneCommit` |
| EventLifecycle | `HallEventExpired` | `ExecutionCancelAndExpireTests.Expire_RequestWithoutDecisionByTheEndOfTheDay_ExpiresAndPublishesHallEventExpired` |
| EventLifecycle | `CustomerCreditGranted` | `DecisionAndDepositTests.AccreditDeposit_PaymentAboveRequired_GrantsTheExcessAsCustomerCredit_Br15` |
| EventLifecycle | `CustomerCreditApplied` | `RescheduleTests.Reschedule_CreditCoversTheDeposit_PublishesRescheduledConfirmedCreditAppliedAndAssignedInOneCommit` |
| HallSchedule | `SlotProvisionallyBlocked` | `QueryAndOutboxTests.Outbox_PlacingProvisionalBlock_AddsSlotProvisionallyBlockedIntegrationEvent` |
| HallSchedule | `SlotBlockConsolidated` | `QueryAndOutboxTests.Outbox_ConsolidatingAProvisionalBlock_AddsSlotBlockConsolidatedIntegrationEvent` |
| HallSchedule | `SlotReleased` | `QueryAndOutboxTests.Outbox_ReleasingABlock_AddsSlotReleasedIntegrationEvent` |

Guardas contra regresiones, una por módulo: `Outbox_..._PublishEveryIntegrationEventOfTheModule` recorre por reflexión los tipos del proyecto `*.Contracts` y falla si algún evento de integración nunca llega al outbox; `Outbox_EveryDomainEventOfTheModule_HasAnApplicationHandlerThatMapsIt` falla si un evento de dominio nuevo no tiene mapper.

## Declaración: ningún handler contiene reglas de negocio (fase 03)
Revisados los 29 handlers, policies y APIs de módulo de `*.Application` y los 19 mappers dominio→integración (estos últimos solo copian campos). Todos hacen solo: cargar agregados, construir tipos de dominio a partir de la entrada, invocar un método del agregado o de un servicio de dominio, mover información entre agregados del mismo módulo, llamar a un puerto o a la API síncrona de otro módulo, y guardar. No validan umbrales, plazos, estados ni importes (eso lo hacen `Quote`, `HallEvent`, `GuaranteeDeposit`, `CustomerCredit`, `HallSchedule` y los VOs); donde había una regla en un handler se movió al dominio (BR-07 → `MembershipDiscountPolicy`, BR-05 en la aceptación → `Quote.Accept`, precondiciones de reprogramación → `EnsureCanReschedule`).
Decisiones de orquestación que sí quedan en Application, porque cruzan agregados o módulos y las define el spec:
- Convertir en `CustomerCredit` el excedente de un pago (BR-15), lo generado al cancelar (BR-21) y el depósito parcial de un evento vencido (BR-17): el *monto* lo calcula el dominio; el handler solo lo traslada.
- Exigir disponibilidad al aceptar (BR-10) y convertir un `ScheduleResult` fallido en `DomainException` con el mismo `code`.
- Rechazar con motivo de sistema cuando el bloqueo sincrónico falla al crear la solicitud.
- Elegir bloqueo definitivo o provisional según el estado resultante de la reprogramación.
- Valores por defecto de la *entrada* (cantidad de catering = aforo, BR-08) y aritmética de lectura (horas restantes de una cotización).

## Verificación de criterios (fase 04)
Los criterios que dependen de la base de datos real se comprobaron **a mano contra PostgreSQL en Docker** (el spec prohíbe tests de integración); lo automatizado son los 104 tests de `Nodo.Infrastructure.UnitTests`.

- [x] **`./scripts/reset` crea schemas, tablas, restricciones y semillas sin errores**: `scripts/reset.ps1` ⇒ `db` y `api` *healthy*, las 6 migraciones aplicadas en el orden HallSchedule, Quotes, EventLifecycle. 3 schemas, `inv..` en 5 + 4 + 1 `CHECK`, 1 `EXCLUDE`.
- [x] **`INSERT`/`UPDATE` manual inválido rechazado por su `CHECK inv..`**: aforo 90 (`ck_hall_events_inv04_attendance`), depósito 60 % (`..._inv02_...`), saldo −1 (`ck_customer_credits_inv21_...`), crédito −5 (`..._inv05_...`), vigencia ≠ 96 h (`ck_quotes_inv14_...`), descuento 11 % (`ck_quotes_inv17_...`) y 9 h (`..._inv01_duration`).
- [x] **Se ven E1..E6, Q1..Q8, crédito de TechSol = 600 y bloqueos coherentes** (consultas sobre las tablas; 6 bloqueos, 4 activos).
- [x] **Un bloqueo activo solapado falla por la exclusión**: franja 15–17 contra E1 (14–18) y 19–21 solo por el buffer ⇒ `ex_schedule_blocks_inv18_overlap`; 20–22 (toca el buffer justo en el límite) y un bloqueo liberado sí se aceptan; segundo bloqueo activo del mismo evento ⇒ `ux_..._inv19_...`.
- [x] **Reloj +5 días y jobs**: Q1 ⇒ `Expired`, E1 ⇒ `Expired`, su bloqueo ⇒ `Released` (de forma eventual vía outbox; también E2 por depósito vencido, con 324 Bs que pasan a crédito: TechSol 924). Q3 (aceptada) y E3 (confirmada) no cambian.
- [x] **Los outbox quedan con `processed_on_utc` poblado** a los pocos segundos (quotes 1/1, event_lifecycle 3/3, hall_schedule 2/2), con 0 reintentos.

Verificado además contra la BD real, con un sondeo temporal ya eliminado: las 14 consultas de lectura; aprobar ⇒ cobrar con excedente (crédito 160) ⇒ idempotencia del mismo `TransactionId`; cancelar un confirmado (crédito 617); reprogramar con saldo (bloqueo definitivo nuevo, anterior liberado, crédito consumido); emitir y **aceptar** una cotización de punta a punta (`QuoteAccepted` ⇒ outbox ⇒ bus ⇒ `QuoteAcceptedHandler` ⇒ evento con sus extras + inbox ⇒ bloqueo provisional en HallSchedule); pago parcial y pago que cubre; **conflicto de `xmin` ⇒ `ConflictException`**; **violación de la exclusión a través de EF ⇒ HTTP 409 con `"code": "SCHEDULE.SLOT_OVERLAP"`**.

Comprobación por mutación de la capa de infraestructura (6 mutantes, todos detectados): quitar el `CHECK` de INV-04, cambiar la longitud de una columna sin migración, debilitar INV-02 a 10–90 %, que el decorador deje de deduplicar, que el bus deje de decorar con el inbox y que el mapa de restricciones pierda INV-18. Código restaurado tras cada una.

`dotnet build Nodo.slnx --no-incremental`: 0 advertencias, 0 errores. `dotnet test Nodo.slnx`: **720 pruebas, 0 fallos** (SharedKernel 15, Quotes 121, HallSchedule 81, EventLifecycle 294, Arquitectura 105, Infraestructura 104). Sin `DateTime.Now/UtcNow` fuera de `DemoClock`.

## Verificación de criterios (fase 03)
- [x] Ningún handler contiene reglas de negocio (declaración arriba).
- [x] Todos los eventos de integración tienen un test que verifica que se agregan al outbox (tabla arriba + guardas por módulo).
- [x] Tests de arquitectura en verde (105): Application no referencia Infrastructure ni módulos ajenos salvo Contracts.
- `dotnet build Nodo.slnx --no-incremental`: 0 advertencias, 0 errores. `dotnet test Nodo.slnx`: 616 pruebas, 0 fallos (SharedKernel 15, Quotes 121, HallSchedule 81, EventLifecycle 294, Arquitectura 105).
- `docker compose up --build`: `db` y `api` *healthy*, `/scalar` responde 200.
- Mutación manual de 8 decisiones de orquestación (aceptar sin disponibilidad, perder el excedente, bloqueo del tipo equivocado, saltar el inbox, no otorgar crédito al cancelar, saltar la comprobación al reprogramar, perder el crédito parcial al expirar, no consolidar al confirmar): los tests fallan en los 8 casos. Código restaurado después de cada una.

## Verificación de criterios (fase 02)
- [x] `INV-01..INV-22` implementadas con su `code` exacto y cubiertas con válido / límite / inválido.
- [x] Tabla INV → clase dueña → tests (arriba).
- [x] BR-01..BR-23 tienen al menos un test que las referencia (BR-10: parcialmente, ver tabla).
- [x] Ninguna propiedad pública de agregado, entidad o VO tiene setter público (`DomainModelProtectionTests`).
- [x] Tests de arquitectura en verde (105, ahora con tipos reales en cada Domain).
- [x] Tabla regla → tests (arriba).
- `dotnet build Nodo.slnx --no-incremental`: 0 advertencias, 0 errores. `dotnet test Nodo.slnx`: 451 pruebas, 0 fallos.
- Comprobación de la calidad de los tests por mutación manual: cambiar `>=` por `>` en el plazo de Dirección (INV-07), en la vigencia de 96 h (INV-14) y en el buffer de HallSchedule (INV-18), o anular el crédito al cancelar (INV-05), hace fallar los tests. La mutación de INV-18 sobrevivió al principio (faltaba el caso del evento nuevo *antes* del existente); se añadió el test y ahora se detecta.

## Deuda conocida
**Fase 04**
- **Adaptadores externos (fase 05)** siguen pendientes: `ISpacesCatalog` (×3), `IMembershipDirectory`, `ICustomerDirectory` (×2) y el ACL de la pasarela. Sin ellos los casos de uso que los necesitan (emitir/previsualizar cotización, aceptar solicitudes, reprogramar, calendario) no se pueden invocar desde la API; los endpoints REST son de la fase 05. Se ejercitaron con adaptadores falsos en un sondeo temporal.
- **Sin tests automáticos** para `OutboxProcessor`, `EfUnitOfWork`, repositorios EF ni consultas (requieren PostgreSQL; el spec prohíbe tests de integración). Se verificaron a mano en Docker (ver arriba) y los protegen los tests de modelo y de migraciones.
- **Dos requests que crean a la vez la agenda de un día nuevo** chocan en la clave primaria `hall_schedules` ⇒ 409 *sin* `code` (no es una invariante del catálogo).
- **Orden del historial** al hidratar un `HallEvent` (ver desviación 15) y **snapshot de migraciones** desactualizado en forma pero no en esquema (desviación 16).
- **`ValidateOnBuild`** desactivado hasta la fase 05.
- **Reintentos del outbox**: tras 5 intentos fallidos el mensaje queda "aparcado" (`attempts = 5`, `error` informado) y no hay herramienta para reencolarlo.
- `IHallEventRepository.ExistsActiveForSlotAsync` está implementado pero ningún caso de uso lo llama.

**Fase 03**
- **Implementaciones de los puertos (fase 04)**: repositorios, `IUnitOfWork` (debe despachar los eventos de dominio en bucle hasta que no queden, dentro de la transacción, con `MediatRDomainEventDispatcher`), `IOutbox`/`IInbox`, `I*Queries` con `AsNoTracking` y los jobs que llaman a `ExpireDueQuotes` / `ExpireOverdueHallEvents`.
- **Adaptadores externos (fase 05)**: `ISpacesCatalog`, `IMembershipDirectory`, `ICustomerDirectory` (uno por módulo), el ACL de la pasarela que invoca `AccreditDepositCommand`, y el cableado en `Nodo.Api` (MediatR sobre los 3 ensamblados de Application, `AddXxxApplication()`, bus en memoria que resuelve los `IIntegrationEventHandler<T>`). _Resuelto en la fase 04 salvo los adaptadores y los controllers._
- **Bloqueo huérfano**: `RescheduleHallEvent` y `QuoteAcceptedHandler` confirman el bloqueo en HallSchedule (otra transacción) antes de guardar en EventLifecycle; si el guardado fallara justo después quedaría un bloqueo sin evento. Es la consecuencia aceptada de "un agregado por transacción"; el job de expiración y el `EXCLUDE` de BD acotan el daño. No hay compensación.
- `IHallEventRepository.ExistsActiveForSlotAsync` (spec 02) no se usa en ningún caso de uso; el solapamiento lo garantiza HallSchedule.
- `HallScheduleModuleApi` traduce `DomainException` a `ScheduleResult.Fail`; cualquier otro error sube como excepción (500), que es lo correcto para un fallo de infraestructura.
- `ExtraServicesAssigned` ya dispara el evento de integración, pero la **orden al proveedor de catering** (parte de BR-20) es del handler externo de la fase 05.

**Fase 02**
- ~~BR-10 ... son de la fase 03.~~ Resuelto en la fase 03.
- Los `CHECK`/`EXCLUDE` de BD de cada invariante (defensa en profundidad), el mapeo de VOs y los índices (`xmin`, `(event_id) WHERE status='Active'`) son de la fase 04.
- `IHallScheduleRepository` no tiene búsqueda por `EventId` e `IQuoteRepository` no lista las vencidas; se añaden cuando la fase 03/04 los necesite (los handlers de liberar/consolidar reciben fecha y salón en el evento).
- Laboratorio de invariantes y respuestas 422 en Scalar: fases 05–06.
- `ExtraServicesAssigned` no agrega una entrada al historial (no es un cambio de estado).
- Los 3 proyectos `*.Application` siguen vacíos; `dotnet test` ya no avisa de "sin pruebas" en los de módulo.
- `npgsql`, `EFCore.NamingConventions` y `EF Design` siguen declarados pero sin referenciar (fase 04).
- El healthcheck de la API depende de `curl` en la imagen (fase 07 puede evaluar otra opción).
