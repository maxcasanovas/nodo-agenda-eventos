# Matriz de trazabilidad de invariantes

Cada invariante del catálogo (`docs/domain/business-rules.md` §9) vive en un agregado o Value Object, lanza una
`DomainException` con un `code` estable y mensaje en español, está cubierta por tests y se puede **intentar violar
desde la UI** (Laboratorio de invariantes) y desde Scalar. Las que tienen defensa en la base de datos la tienen con
una restricción cuyo nombre lleva el id (`..._inv04_...`).

Un test (`InvariantDocumentationTests`) comprueba que esta tabla coincide con el catálogo que publica la API
(`GET /api/demo/invariants`): mismos 22 ids, mismos `code` y misma marca de refuerzo en BD.

- **Regla**: regla de negocio del documento (BR-xx, ver `business-rules.md` §3).
- **Dueño**: clase del dominio que la hace cumplir (ruta bajo `src/`; todas son *always-valid*: sin constructores ni
  setters públicos, verificado por `DomainModelProtectionTests`).
- **Tests**: archivos de `tests/` que cubren el caso válido, el límite y el inválido (nombran el `INV-xx`).
- **BD**: restricción de PostgreSQL (defensa en profundidad; la violación detectada solo por la BD responde 409).
- **Sonda**: lo que intenta `POST /api/demo/invariants/{id}/probe` (el caso de uso real, dentro de una transacción
  que siempre se revierte).

## HallEvent (EventLifecycle)

| ID | Regla | Dueño | Code | Tests | BD | Sonda |
|---|---|---|---|---|---|---|
| INV-01 | BR-01 duración de 1 a 8 h enteras, mismo día | VO `TimeSlot` (`Modules/*/…Domain/TimeSlot.cs`, uno por módulo) | `EVENT.DURATION_OUT_OF_RANGE` | `HallEventInvariantTests`, `QuoteInvariantTests`, `HallScheduleInvariantTests` | `ck_hall_events_inv01_duration`, `ck_quotes_inv01_duration`, `ck_schedule_blocks_inv01_duration` | Cotizar 9 horas (09:00–18:00) |
| INV-02 | BR-14 depósito entre 20 % y 50 % | VO `GuaranteeDeposit` (`EventLifecycle.Domain/GuaranteeDeposit.cs`) | `EVENT.DEPOSIT_PERCENTAGE_OUT_OF_RANGE` | `GuaranteeDepositTests`, `HallEventInvariantTests` | `ck_hall_events_inv02_deposit_percentage` | Aprobar con un depósito fuera de rango |
| INV-03 | BR-16 confirmar exige aprobación y depósito cubierto | `HallEvent.ConfirmFirmly` (`EventLifecycle.Domain/HallEvent.cs`) | `EVENT.CONFIRMATION_PRECONDITIONS_NOT_MET` | `HallEventInvariantTests`, `HallEventStateMachineTests` | — | Pago parcial: ningún endpoint confirma, solo el pago que cubre el depósito (protegida por diseño; el dominio lanza el código si se invoca sin las condiciones) |
| INV-04 | BR-04 aforo entre 1 y 80 | VO `Attendance` (`EventLifecycle.Domain/Attendance.cs`) | `EVENT.ATTENDANCE_OUT_OF_RANGE` | `HallEventInvariantTests`, `QuoteInvariantTests` | `ck_hall_events_inv04_attendance`, `ck_quotes_inv04_attendance` | Cotizar para 81 personas |
| INV-05 | BR-21 cancelar no devuelve dinero: todo pasa a saldo a favor | `HallEvent.Cancel` + `CancellationPolicy` | `EVENT.CANCELLATION_NOT_ALLOWED` | `HallEventInvariantTests`, `HallEventStateMachineTests` | `ck_hall_events_inv05_credit_generated` | Cancelar un evento ya rechazado |
| INV-06 | Solo transiciones válidas de la máquina de estados | `HallEvent` (transición interna) | `EVENT.INVALID_STATE_TRANSITION` | `HallEventStateMachineTests` | — | Aprobar dos veces la misma solicitud |
| INV-07 | BR-12 Dirección decide dentro del día | `HallEvent.Approve` / `Reject` | `EVENT.DECISION_DEADLINE_EXCEEDED` | `HallEventInvariantTests` | — | Aprobar al día siguiente de la solicitud (reloj desplazado en la sonda) |
| INV-08 | BR-13 rechazo con motivo de al menos 10 caracteres | `HallEvent.Reject` | `EVENT.REJECTION_REASON_REQUIRED` | `HallEventInvariantTests` | — | Rechazar con el motivo «corto» |
| INV-09 | BR-15 solo se acredita con el evento aprobado; un `TransactionId` una sola vez | `HallEvent.AccreditDeposit` / `GuaranteeDeposit` | `EVENT.DEPOSIT_NOT_ACCEPTED` | `HallEventInvariantTests`, `GuaranteeDepositTests`, `HallEventStateMachineTests`, `PaymentGatewayAclTests` (idempotencia) | — | Acreditar un pago a una solicitud que Dirección aún no aprobó |
| INV-10 | El monto acreditado nunca es negativo ni de otra moneda | VO `Money` (`SharedKernel/Money.cs`) | `MONEY.CURRENCY_MISMATCH` / `MONEY.NEGATIVE_AMOUNT` | `MoneyTests`, `GuaranteeDepositTests`, `HallEventInvariantTests` | — | Acreditar un pago en dólares sobre un depósito en bolivianos |
| INV-11 | BR-18 inicio desde 1 h antes y antes del fin | `HallEvent.Start` | `EVENT.START_OUTSIDE_WINDOW` | `HallEventInvariantTests` | — | Iniciar un evento más de 1 h antes de su hora |
| INV-12 | BR-23 reprogramar solo un cancelado con saldo, conservando duración, aforo, extras y total | `HallEvent.Reschedule` / `SlotMovedTo` | `EVENT.RESCHEDULE_NOT_ALLOWED` | `HallEventInvariantTests`, `HallEventStateMachineTests`, `RescheduleTests` | — | Reprogramar un evento que no está cancelado |
| INV-13 | BR-08 extras solo vía la raíz; equipos ≤ inventario | `HallEvent` / `Quote` (líneas de extras) | `EVENT.EXTRA_SERVICE_INVALID` | `HallEventInvariantTests`, `QuoteInvariantTests` | — | Cotizar 2 proyectores con inventario de 1 |

## Quote (Quotes)

| ID | Regla | Dueño | Code | Tests | BD | Sonda |
|---|---|---|---|---|---|---|
| INV-14 | BR-09 vigencia de 96 h; vencida no se acepta | `Quote.Accept` (`Quotes.Domain/Quote.cs`) | `QUOTE.EXPIRED` | `QuoteInvariantTests` | `ck_quotes_inv14_validity_96h` | Aceptar una cotización 120 h después de emitida |
| INV-15 | Precio y servicios congelados al aceptar | `Quote` (no existe método que los cambie) | (por diseño) | `QuoteInvariantTests`, `DomainModelProtectionTests` (reflexión: sin setters ni métodos de cambio) | — | Protegida por diseño: la sonda lo explica |
| INV-16 | BR-02 / BR-05 horario operativo con buffers y anticipación de 3 a 180 días | `Quote.Issue` + `BookingPolicy` | `QUOTE.SLOT_OUTSIDE_OPERATING_HOURS` / `QUOTE.DATE_OUT_OF_RANGE` | `QuoteInvariantTests` | — | Cotizar un evento que empieza a las 07:00 (sin hora para el montaje) |
| INV-17 | BR-07 descuento de membresía entre 0 % y 10 %, solo sobre el salón | `PriceBreakdown` (`Quotes.Domain/PriceBreakdown.cs`) | `QUOTE.INVALID_DISCOUNT` | `QuoteInvariantTests`, `ExternalAdapterTests` (el adaptador no corrige el 15 %) | `ck_quotes_inv17_membership_discount` | Cotizar para un socio cuya membresía publica un 15 % (inyectado solo durante la sonda) |

## HallSchedule

| ID | Regla | Dueño | Code | Tests | BD | Sonda |
|---|---|---|---|---|---|---|
| INV-18 | BR-03 ningún bloqueo activo solapa a otro con 1 h de montaje y 1 h de limpieza | `HallSchedule` (`HallSchedule.Domain/HallSchedule.cs`) | `SCHEDULE.SLOT_OVERLAP` | `HallScheduleInvariantTests`, `ModuleApiTests`, `ConstraintTranslationTests` | `ex_schedule_blocks_inv18_overlap` (restricción EXCLUDE con gist) | Aceptar una franja que choca con otra reserva (buffers incluidos) |
| INV-19 | Un evento tiene como máximo un bloqueo activo | `HallSchedule` | `SCHEDULE.DUPLICATE_BLOCK` | `HallScheduleInvariantTests` | `ux_schedule_blocks_inv19_one_active_block_per_event` (índice único parcial) | Colocar un segundo bloqueo activo para el mismo evento |
| INV-20 | Equipos asignados ≤ inventario | `AvailabilityChecker` | `SCHEDULE.EQUIPMENT_UNAVAILABLE` | `HallScheduleInvariantTests` | — | Reservar 2 proyectores en un bloqueo con inventario de 1 |

## CustomerCredit (EventLifecycle)

| ID | Regla | Dueño | Code | Tests | BD | Sonda |
|---|---|---|---|---|---|---|
| INV-21 | El saldo nunca es negativo; no se aplica más de lo disponible | `CustomerCredit` (`EventLifecycle.Domain/CustomerCredit.cs`) | `CREDIT.INSUFFICIENT` | `CustomerCreditInvariantTests` | `ck_customer_credits_inv21_balance_non_negative` | Reprogramar un segundo evento cuando el saldo ya se consumió |
| INV-22 | El saldo a favor no se convierte en dinero (sin retiro ni reembolso) | `CustomerCredit` (no existe la operación) | (por diseño) | `CustomerCreditInvariantTests`, `DomainModelProtectionTests` | — | Protegida por diseño: la sonda lo explica |

## Cómo verlo funcionar

- **UI**: `http://localhost:3000` → «Laboratorio de invariantes» → **Probar todas** (`22/22 protegidas`: 19 rechazadas
  con su `code` y 3 protegidas por diseño — INV-03, INV-15 e INV-22).
- **Scalar**: `http://localhost:5080/scalar`; cada operación lista las `INV-xx` que puede disparar y trae ejemplos del 422.
- **Base de datos** (defensa en profundidad), con `docker compose exec db psql -U nodo -d nodo`:

  ```sql
  -- INV-02: el depósito de la semilla E2 no puede pasar de 50 %
  UPDATE event_lifecycle.hall_events SET deposit_percentage = 60
   WHERE id = '44444444-0000-0000-0000-000000000002';
  -- ERROR: ... violates check constraint "ck_hall_events_inv02_deposit_percentage"

  -- INV-04: aforo de 90 en una cotización
  UPDATE quotes.quotes SET attendance = 90 WHERE id = '33333333-0000-0000-0000-000000000001';
  -- ERROR: ... violates check constraint "ck_quotes_inv04_attendance"
  ```

  INV-18 (`EXCLUDE`) con una reserva de 15:00 a 18:00 el mismo día que E3 (10:00–14:00): no solapa con el evento, pero sí con su
  hora de limpieza, y la BD la rechaza con `ex_schedule_blocks_inv18_overlap`. Está en [`docs/demo-script.md`](demo-script.md).
