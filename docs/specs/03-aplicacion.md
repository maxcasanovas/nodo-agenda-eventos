# Spec 03 — Capa de aplicación, contratos e integración entre módulos

## Objetivo
Casos de uso (comandos y queries con MediatR), puertos hacia contextos externos, Published Language
(`*.Contracts`) y políticas reactivas. Todo probado con unit tests usando dobles (NSubstitute / fakes en memoria).

## Contracts (Published Language)
- `Nodo.Quotes.Contracts`: integration events `QuoteIssued`, `QuoteAccepted` (snapshot completo para crear el evento), `QuoteExpired`.
- `Nodo.EventLifecycle.Contracts`: los integration events de `business-rules.md §7`.
- `Nodo.HallSchedule.Contracts`:
  - `IHallScheduleModuleApi`: `CheckAvailabilityAsync(hallId, date, start, end, equipment[], excludeEventId?)`,
    `PlaceProvisionalBlockAsync(...)`, `PlaceDefinitiveBlockAsync(...)` → resultado con `Success` + `Reason`.
  - Integration events `SlotProvisionallyBlocked`, `SlotBlockConsolidated`, `SlotReleased`.

## Puertos hacia externos (interfaces en Application, implementación en spec 05)
- `ISpacesCatalog`: datos del salón, inventario de equipos con costo, catálogo de catering (Customer/Supplier OHS/PL).
- `IMembershipDirectory`: `GetMembership(customerId) → (IsActiveMember, DiscountPercentage)` (Conformist).
- `ICustomerDirectory`: nombre del cliente para read models.

## Casos de uso

### Quotes
| Tipo | Nombre | Notas |
|---|---|---|
| Query | `PreviewQuote` | Calcula precio y disponibilidad (informativa) sin persistir. |
| Command | `IssueQuote` | `performedBy`; persiste; devuelve Id + desglose + disponibilidad informativa (BR-10). |
| Command | `AcceptQuote` | Exige disponibilidad vía `IHallScheduleModuleApi.CheckAvailability` (BR-10) y luego `quote.Accept`. |
| Command | `ExpireDueQuotes` | Para el job (spec 04). |
| Query | `GetQuote`, `ListQuotes(customerId?, status?)` | Incluye `ExpiresAtUtc` y horas restantes. |

### EventLifecycle
| Tipo | Nombre | Notas |
|---|---|---|
| Policy | `QuoteAcceptedHandler` (integration) | Crea `HallEvent` con `HallEventFactory` y coloca bloqueo provisional vía API síncrona. Si el bloqueo falla ⇒ crea el evento y lo rechaza con motivo de sistema "Sin disponibilidad al registrar la solicitud". Idempotente (Inbox). |
| Command | `ApproveEventRequest` | `percentage`, `approvedBy`. |
| Command | `RejectEventRequest` | `reason`, `rejectedBy`. |
| Command | `AccreditDeposit` | Lo invoca el ACL de la pasarela: `eventId`, `amount`, `currency`, `transactionId`. Confirma automáticamente si queda cubierto (BR-16) y otorga excedente como `CustomerCredit` (BR-15). Equivale al `ConfirmarEventoApplicationService` del documento. |
| Policy | `AssignExtraServicesOnConfirmed` (domain event handler) | BR-20. |
| Command | `StartHallEvent`, `CompleteHallEvent` | BR-18, BR-19. |
| Command | `CancelHallEvent` | BR-21; otorga `CustomerCredit`. |
| Command | `RescheduleHallEvent` | BR-23; verifica y coloca bloqueo (definitivo o provisional) vía API síncrona, aplica `CustomerCredit`. |
| Command | `ExpireOverdueHallEvents` | Para el job (BR-12, BR-17). |
| Query | `GetHallEvent` (con historial, depósito, extras, nombre del cliente), `ListHallEvents(status?, customerId?, from?, to?)`, `GetPendingApprovals` (bandeja de Dirección, ordenada por deadline), `GetCustomerCredit(customerId)`, `GetTodayOperations` (eventos confirmados / en curso del día local). |

### HallSchedule
| Tipo | Nombre | Notas |
|---|---|---|
| Module API | Implementación de `IHallScheduleModuleApi` | Usa el agregado del día + `AvailabilityChecker` + inventario de `ISpacesCatalog`. |
| Integration handlers | `HallEventFirmlyConfirmed` ⇒ `Consolidate`; `EventRequestRejected`, `HallEventExpired`, `HallEventCancelledWithCredit` ⇒ `Release`. Idempotentes. |
| Query | `GetHallCalendar(hallId, from, to)` | Bloques activos con buffers, tipo, evento, cliente (para FullCalendar). |
| Query | `CheckAvailability` | Para la UI. |

## Flujo de domain events → outbox
- Al guardar, la Unit of Work del módulo despacha los domain events (MediatR `INotification` vía un adaptador, el dominio no conoce MediatR).
- Handlers de domain events en Application mapean a integration events y los agregan al Outbox **en la misma transacción**.
- Mapear explícitamente (no serializar el domain event tal cual): el integration event es el contrato público.

## Validación
FluentValidation por comando para validaciones de forma (requeridos, rangos de entrada). Las reglas de negocio siguen en el dominio.

## Tests
- Handlers con repositorios en memoria (fakes simples en el proyecto de tests) y `IClock` falso.
- Casos clave: aceptar cotización sin disponibilidad falla; `QuoteAcceptedHandler` procesado dos veces crea un solo evento;
  `AccreditDeposit` que cubre confirma y registra integration event `HallEventFirmlyConfirmed` en el outbox;
  pago con excedente crea saldo a favor; cancelar confirmado crea/incrementa `CustomerCredit`;
  reprogramar aplica saldo y deja excedente; bloqueo síncrono fallido en reprogramación no modifica el evento.

## Criterios de aceptación
- [ ] Ningún handler contiene reglas de negocio (solo orquestación). Revísalo y decláralo en `PROGRESS.md`.
- [ ] Todos los integration events tienen un test que verifica que se agregan al outbox.
- [ ] Tests de arquitectura en verde (Application no referencia Infrastructure ni módulos ajenos salvo Contracts).
