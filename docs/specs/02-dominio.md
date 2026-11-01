# Spec 02 — Modelo de dominio de los tres Bounded Contexts

## Objetivo
Implementar los agregados, entidades, value objects, domain services, factorías, domain events y
contratos de repositorio de `Quotes`, `EventLifecycle` y `HallSchedule`, protegiendo **todas** las reglas
BR-01..BR-23 de `business-rules.md`, con unit tests puros (sin mocks de infraestructura).

## Prioridad de esta fase: invariantes
Esta es la fase que más pesa en la evaluación. Implementa **todo** el catálogo `INV-01..INV-22`
(`business-rules.md §9`) con el `code` exacto. Modelo *always-valid*:
- Agregados creados solo por factoría/método estático; constructor sin parámetros `private` solo para EF.
- VOs con constructor privado + `Create(...)` que valida; no existe forma de obtener un VO inválido.
- Colecciones internas privadas; los extras solo cambian vía métodos de la raíz.
- Ningún método deja el agregado a medias: validar todo **antes** de mutar.
- Operaciones que el negocio prohíbe (reembolsar dinero, cambiar precio de cotización aceptada) **no existen** como método (INV-15, INV-22).

## Reglas generales
- Solo proyectos `*.Domain` y sus tests. Cero dependencias externas.
- Cada método de dominio que cambia estado recibe `DateTime nowUtc` (y, si aplica, `DateOnly todayLocal`) como parámetro.
- Cada transición de `HallEvent` agrega entrada a `History` y levanta su domain event.
- Mensajes de `DomainException` en español; `code` con prefijo del módulo (`QUOTE.*`, `EVENT.*`, `SCHEDULE.*`).

## Quotes (`Nodo.Quotes.Domain`)
- VOs: `QuoteId`, `CustomerId`, `HallId`, `TimeSlot` (BR-01, BR-02; calcula `DurationHours`), `Attendance` (BR-04),
  `DegressiveRate` (BR-06; `FactorFor(hours)`), `ExtraServiceLine` (código, nombre, tipo, cantidad, costo unitario),
  `PriceBreakdown` (BaseHourlyRate, Hours, RateFactor, HallRental, MembershipDiscountPercentage, MembershipDiscount, ExtrasTotal, Total).
- Domain service `EventPricingCalculator`: recibe horas, porcentaje de membresía (0–10), líneas de extra ⇒ `PriceBreakdown` (BR-06..08).
- Agregado `Quote`:
  - `Issue(...)` factoría: valida BR-01, 02, 04, 05, 08 (cantidad equipos ≤ stock, que llega como dato de entrada), calcula precio, `ExpiresAtUtc = issued + 96 h` (BR-09). Levanta `QuoteIssued`.
  - `Accept(nowUtc)`: falla si vencida o no `Issued`. Levanta `QuoteAccepted` con snapshot completo.
  - `Expire(nowUtc)`: idempotente; solo si `Issued` y vencida. Levanta `QuoteExpired`.
  - Estados: `Issued`, `Accepted`, `Expired`.
- `IQuoteRepository`.

## HallSchedule (`Nodo.HallSchedule.Domain`)
- Agregado `HallSchedule` identificado por (`HallId`, `Date`) **[Decisión: un agregado por día]**.
- Entidad `ScheduleBlock`: `EventId`, `TimeSlot`, rango con buffers (BR-03), `Kind` (`Provisional`/`Definitive`),
  `Status` (`Active`/`Released`), `EquipmentAllocations` (código + cantidad).
- Domain service `AvailabilityChecker` (`VerificadorDisponibilidadAgendaService`): dado un `HallSchedule` y una franja
  + equipos solicitados + inventario, responde disponible/no con motivo (solapamiento con buffers; equipos por encima del stock).
- Métodos de `HallSchedule`:
  - `PlaceProvisionalBlock(eventId, slot, equipment, inventory)` → valida con `AvailabilityChecker`; `SlotProvisionallyBlocked`.
  - `PlaceDefinitiveBlock(...)` (usado en reprogramación cubierta).
  - `Consolidate(eventId)` → `Provisional` ⇒ `Definitive`; idempotente; `SlotBlockConsolidated`.
  - `Release(eventId)` → idempotente; `SlotReleased`.
  - Un mismo `EventId` no puede tener dos bloqueos activos.
- `IHallScheduleRepository` (`GetOrCreate(hallId, date)`, `Save`), `IEquipmentInventory` **no** va aquí: el inventario llega como parámetro.

## EventLifecycle (`Nodo.EventLifecycle.Domain`)
- VOs: `HallEventId`, `TimeSlot`, `Attendance`, `GuaranteeDeposit` (Percentage 20–50, Required, Accredited,
  `IsCovered`, set de `TransactionId` acreditados; `Accredit(money, txId)` idempotente; excedente calculable) — BR-14, 15.
- Entidad `ExtraServiceItem` (Id, Type, Code, Description, Quantity, UnitCost). Gestionada solo vía la raíz.
- Agregado `HallEvent` con: `QuoteId`, `CustomerId`, `HallId`, `TimeSlot`, `Attendance`, `Total`, `ExtraServices`,
  `Status`, `Deposit` (null hasta aprobar), `RequestedAtUtc`, `DecisionDeadlineUtc` (fin del día local, BR-12),
  `DepositDeadlineUtc` (BR-17), `ManagementDecision` (por, cuándo, motivo de rechazo), `CreditGenerated`,
  `RescheduleCount`, `ExtraServicesAssigned` (bool), `History`.
  - `Approve(by, percentage, nowUtc)` — solo `Requested` y antes del deadline.
  - `Reject(by, reason, nowUtc)` — BR-13.
  - `AccreditDeposit(money, txId, nowUtc)` — BR-15; si queda cubierto llama internamente a `ConfirmFirmly` (BR-16). Devuelve excedente (Money) si lo hubo.
  - `ConfirmFirmly(nowUtc)` — invariante: aprobado + cubierto.
  - `AssignExtraServices(nowUtc)` — solo `Confirmed`, idempotente (BR-20).
  - `Start(nowUtc)` (BR-18), `Complete(nowUtc)` (BR-19; expone `RemainingToSettle`).
  - `Cancel(by, reason, nowUtc)` — BR-21; usa `CancellationPolicy` para calcular el saldo a favor.
  - `Expire(nowUtc)` — BR-12 / BR-17 según estado; idempotente.
  - `Reschedule(newSlot, creditApplied, by, nowUtc)` — BR-23; deja `Confirmed` o `ApprovedByManagement`.
- Domain service `CancellationPolicy` (`PoliticaCancelacionEventoService`): monto de saldo a favor = depósito acreditado.
- `HallEventFactory.FromAcceptedQuote(snapshot, nowUtc, todayLocal)` — crea `HallEvent` en `Requested`, `CreditGenerated = 0`; levanta `EventRequestCreated`.
- Agregado `CustomerCredit` (`CustomerId`, `Balance`, `Movements`): `Grant(amount, eventId, reason)`, `Apply(maxAmount, eventId) → Money aplicado`; nunca negativo.
- Repositorios: `IHallEventRepository` (`GetById`, `Add`, `ExistsActiveForSlot` según el documento), `ICustomerCreditRepository`.

## Tests mínimos (por módulo, nombres `Method_Scenario_ExpectedResult`)
- **Quotes**: tabla degresiva (los 4 tramos), descuento solo sobre salón, extras, vencimiento a las 96 h exactas (95:59 vigente / 96:00 vencida), aceptar vencida falla, aforo 81 falla, 9 horas falla, inicio 07:00 falla (BR-02), fecha D+2 falla (BR-05).
- **HallSchedule**: solapamiento directo, solapamiento solo por buffer (A 10–12, B 13–15 ⇒ falla; B 14–16 ⇒ ok), liberado no bloquea, consolidar idempotente, equipos por encima de stock fallan.
- **EventLifecycle**: aprobar fuera de plazo falla, porcentaje 19/51 falla, rechazo sin motivo falla, pago parcial no confirma, pagos acumulados confirman, transacción repetida no duplica, excedente devuelto, confirmar sin aprobación falla, iniciar antes de inicio − 1 h falla, cancelar confirmado genera saldo = acreditado, cancelar `InProgress` falla, reprogramar con saldo suficiente ⇒ `Confirmed`, insuficiente ⇒ `ApprovedByManagement`, reprogramar evento sin saldo falla, expiración por BR-12 y BR-17 (con saldo por pago parcial), historial registra cada transición.

### Tests de invariantes (obligatorios)
- Por cada `INV-xx`: test del caso válido, del **límite** (ej. aforo 80 ok / 81 falla; 8 h ok / 9 h falla; 20 % y 50 % ok / 19 % y 51 % fallan;
  95:59 h vigente / 96:00 vencida) y del inválido, verificando el `code` exacto con Shouldly.
- Usa `[Theory]` con `InlineData` para los límites. Agrupa los tests en clases `{Agregado}InvariantTests`.
- Test de *estado inalterado*: tras una `DomainException`, el agregado conserva su estado y no levantó domain events.
- Test de máquina de estados: matriz `[Theory]` estado × comando que verifica que toda transición no permitida lanza `EVENT.INVALID_STATE_TRANSITION` (INV-06).
- Test por reflexión: ninguna propiedad pública de agregados/VOs tiene setter público y ningún agregado tiene constructor público.

## Criterios de aceptación
- [ ] `INV-01..INV-22` implementadas con su `code` exacto y cubiertas por tests de válido/límite/inválido.
- [ ] Tabla en `PROGRESS.md`: `INV-xx` → clase dueña → tests.
- [ ] Cada BR-01..BR-23 tiene al menos un test que la referencia en su nombre o comentario.
- [ ] Ninguna propiedad pública de agregado tiene setter público.
- [ ] Tests de arquitectura siguen en verde.
- [ ] Tabla en `PROGRESS.md` regla → test(s).
