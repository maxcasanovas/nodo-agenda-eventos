# Reglas de negocio — Agenda y Eventos (fuente única de verdad)

Derivado del documento "Proyecto Final: Diseño DDD - Caso Nodo" (Grupo 3). Donde el documento era ambiguo
se tomó una decisión explícita, marcada como **[Decisión]**. Valores marcados **[Inventado]** no existen en el
documento y fueron definidos para el prototipo.

---

## 1. Alcance

Tres Bounded Contexts internos (módulos):

| Módulo (código) | BC del documento | Responsabilidad |
|---|---|---|
| `Quotes` | Cotizaciones | Presupuestos con tarifa degresiva, vigencia de 4 días, aceptación que congela el precio. No bloquea calendario. |
| `EventLifecycle` | Ciclo del Evento | Solicitud, decisión de Dirección, depósito de garantía, confirmación en firme, ejecución, cancelación con saldo a favor y reprogramación. |
| `HallSchedule` | Agenda del Salón | Ocupación temporal del salón, bloqueos provisionales/definitivos, buffers de montaje/limpieza, asignación de equipamiento. |

Contextos externos (hardcodeados en `Nodo.External.Simulated`): Espacios y Recursos, Membresías y Planes,
Pasarela de Pagos, Facturación y Cobros, Control de Acceso, Notificaciones, Proveedor de Catering.

---

## 2. Glosario (Lenguaje Ubicuo ES → código EN)

| Término de negocio (ES) | Código (EN) | Notas |
|---|---|---|
| Evento (agregado raíz) | `HallEvent` | Se evita `Event` para no colisionar con "domain event". |
| Solicitud de Evento | `HallEvent` en estado `Requested` | No es un agregado aparte. |
| Evento en Firme | `HallEvent` en estado `Confirmed` | |
| Cotización | `Quote` | |
| Depósito de Garantía | `GuaranteeDeposit` | VO |
| Saldo a Favor | `CustomerCredit` (agregado) / `credit` | Sin devolución en dinero. |
| Servicio Extra / Ítem | `ExtraServiceItem` | Entidad interna de `HallEvent` |
| Tipo de servicio | `ExtraServiceType`: `OwnEquipment`, `ExternalCatering` | |
| Franja Horaria | `TimeSlot` | VO: `Date`, `Start`, `End` |
| Dinero | `Money` | VO: `Amount`, `Currency` |
| Aforo | `Attendance` | VO |
| Agenda del Salón | `HallSchedule` | Agregado por (`HallId`, `Date`) **[Decisión]** |
| Bloqueo | `ScheduleBlock` | Entidad interna de `HallSchedule` |
| Dirección | `Management` | Actor |
| Recepción | `Reception` | Actor |
| Operaciones / Host | `Operations` | Actor |
| Cliente (organizador) | `Customer` | Miembro o externo |
| Tarifa degresiva | `DegressiveRate` | |
| Calculador de tarifa | `EventPricingCalculator` | Domain service (Quotes) |
| Verificador de disponibilidad | `AvailabilityChecker` | Domain service (HallSchedule) |
| Política de cancelación | `CancellationPolicy` | Domain service (EventLifecycle) |
| Factoría de evento | `HallEventFactory` | |

---

## 3. Reglas de negocio

### Franja, aforo y calendario
- **BR-01 Duración**: horas enteras, mínimo 1 y máximo 8, continuas, dentro del mismo día. Inicio y fin en hora exacta (`HH:00`).
- **BR-02 Horario operativo [Inventado]**: el salón opera 07:00–23:00. El rango *con buffers* debe caber ahí ⇒ el evento empieza ≥ 08:00 y termina ≤ 22:00.
- **BR-03 Buffers [Decisión]**: 1 h de montaje antes y 1 h de limpieza después. Dos bloqueos activos no pueden solapar sus rangos con buffers. (Los 15 min del documento aplican a salas de reunión, fuera de alcance.)
- **BR-04 Aforo**: entero entre 1 y 80.
- **BR-05 Anticipación [Inventado]**: la fecha del evento debe estar entre *hoy + 3 días* y *hoy + 180 días* (fecha local).

### Cotización
- **BR-06 Tarifa degresiva [Inventado]**: tarifa base **500 Bs/h**, factor aplicado a *todas* las horas según la duración total:

  | Horas | Factor | Ejemplo |
  |---|---|---|
  | 1–2 | 1.00 | 2 h = 1.000 Bs |
  | 3–4 | 0.90 | 4 h = 1.800 Bs |
  | 5–6 | 0.80 | 6 h = 2.400 Bs |
  | 7–8 | 0.70 | 8 h = 2.800 Bs |

- **BR-07 Descuento por membresía**: entre 5 % y 10 % según plan (ver §5), solo si la membresía está activa y **solo sobre el alquiler del salón** (no sobre servicios extra) **[Decisión]**.
- **BR-08 Servicios extra**: equipos propios (`OwnEquipment`) con cantidad ≤ inventario; catering subcontratado (`ExternalCatering`) cobrado por persona, cantidad por defecto = aforo. Costo unitario se congela en la cotización.
- **Total** = alquiler con factor − descuento membresía + Σ(cantidad × costo unitario de extras). Redondeo a 2 decimales.
- **BR-09 Vigencia**: la cotización vence a las **96 h (4 días calendario)** de emitida. Vencida no se puede aceptar. Un job la marca `Expired`.
- **BR-10 Disponibilidad**: emitir una cotización **no bloquea** el calendario; al emitir se informa la disponibilidad (no bloqueante) y al **aceptar** se exige disponibilidad (bloqueante).
- **BR-11 Aceptación**: congela precio y servicios. La política reacciona creando automáticamente la solicitud de evento (`HallEvent` en `Requested`) y el bloqueo provisional.

### Dirección y depósito
- **BR-12 Respuesta en el día**: Dirección debe aprobar o rechazar antes de las 23:59:59 (hora local) del día de la solicitud. Si no, el evento pasa a `Expired` y se libera el bloqueo **[Decisión: mecanismo de expiración]**.
- **BR-13 Rechazo**: requiere motivo (mínimo 10 caracteres). Libera el bloqueo provisional.
- **BR-14 Porcentaje de depósito**: Dirección lo fija al aprobar, entero entre 20 y 50. `Requerido = Total × %`.
- **BR-15 Acreditación**: solo en estado `ApprovedByManagement`. Los pagos **se acumulan**; idempotentes por `TransactionId` (un mismo id no se acredita dos veces); moneda BOB. Si lo acreditado supera lo requerido, el excedente se registra como saldo a favor del cliente **[Decisión]**.
- **BR-16 Confirmación en firme**: requiere aprobación de Dirección **y** depósito cubierto (acreditado ≥ requerido). Se dispara automáticamente al quedar cubierto, dentro de la misma transacción del comando de acreditación.
- **BR-17 Plazo del depósito [Inventado]**: 48 h desde la aprobación. Vencido sin cubrir ⇒ `Expired`; lo acreditado parcialmente pasa a saldo a favor; se libera el bloqueo.

### Ejecución
- **BR-18 Inicio**: solo `Confirmed`; permitido desde *inicio − 1 h* (montaje) hasta antes del fin.
- **BR-19 Cierre**: solo `InProgress`. Al completar, Facturación liquida el saldo restante = `Total − DepósitoAcreditado`.
- **BR-20 Servicios extra asignados**: al confirmarse en firme, una política asigna los extras: los equipos quedan reservados en el bloqueo del salón y se emite orden al proveedor de catering. Emite `ExtraServicesAssigned`.

### Cancelación y reprogramación
- **BR-21 Cancelación**: permitida en `Requested`, `ApprovedByManagement` y `Confirmed`, siempre antes del inicio. **Nunca hay devolución en dinero**: todo lo acreditado se convierte íntegramente en saldo a favor. Se libera el bloqueo y se revocan pases de acceso.
- **BR-22 Saldo a favor**: por cliente, vigencia indefinida, sólo utilizable para reprogramar.
- **BR-23 Reprogramación**: aplica a un `HallEvent` `Cancelled` que generó saldo a favor (> 0). Nueva fecha/hora de inicio que cumpla BR-01..05; **misma duración, aforo, servicios y total congelados**; sin nueva aprobación de Dirección y sin penalización. Se aplica saldo a favor hasta cubrir el depósito requerido:
  - Si cubre ⇒ `Confirmed` con bloqueo definitivo; el excedente sigue como saldo a favor.
  - Si no cubre ⇒ `ApprovedByManagement` con bloqueo provisional y 48 h para pagar la diferencia (BR-17).
  - Se registra en el historial y se incrementa `RescheduleCount`.

### Actores
- **BR-24 Permisos (solo UI) [Decisión]**: Cliente y Recepción cotizan; Cliente acepta; Dirección aprueba/rechaza; Operaciones inicia/cierra; Cliente o Recepción cancelan y reprograman. La API no valida rol (no hay autenticación); los comandos reciben `performedBy` para auditoría.

---

## 4. Máquina de estados de `HallEvent`

```
Requested ──approve──▶ ApprovedByManagement ──deposit covered──▶ Confirmed ──start──▶ InProgress ──complete──▶ Completed
    │ reject ▶ Rejected        │ 48h ▶ Expired                      │
    │ end of day ▶ Expired     │ cancel ▶ Cancelled                 │ cancel ▶ Cancelled
    │ cancel ▶ Cancelled                                            
Cancelled (con saldo) ──reschedule──▶ Confirmed | ApprovedByManagement
```

Estados: `Requested`, `ApprovedByManagement`, `Confirmed`, `InProgress`, `Completed`, `Rejected`, `Cancelled`, `Expired`.

**[Decisión] Desviación respecto al documento**: el enum del documento tiene `REPROGRAMADO` y no tiene
`REJECTED`, `EXPIRED` ni `IN_PROGRESS`. Aquí la reprogramación es una *transición* (registrada en historial)
y no un estado, para no duplicar la lógica de confirmación. Cada transición agrega una entrada a
`HallEvent.History` (estado, fecha UTC, actor, nota) que la UI muestra como línea de tiempo.

---

## 5. Datos externos hardcodeados

### Espacios y Recursos (upstream, OHS/PL)
- Salón: `HallId = 11111111-1111-1111-1111-111111111111`, "Salón Polivalente", aforo máximo 80, horario 07:00–23:00.
- Inventario de equipos propios:

  | Código | Nombre | Stock | Costo por evento |
  |---|---|---|---|
  | `PROJECTOR` | Proyector | 1 | 150 Bs |
  | `TV` | Pantalla TV 55" | 2 | 100 Bs c/u |
  | `SOUND` | Sistema de sonido | 1 | 250 Bs |
  | `LIGHTS` | Kit de luces | 1 | 200 Bs |

### Proveedor de Catering (subcontratado)

| Código | Nombre | Costo por persona |
|---|---|---|
| `COFFEE_BREAK` | Coffee break | 35 Bs |
| `SNACK` | Refrigerio | 25 Bs |
| `LUNCH` | Almuerzo ejecutivo | 90 Bs |
| `BUFFET` | Cena buffet | 120 Bs |

### Membresías y Planes (upstream, Conformist)

| CustomerId | Nombre | Tipo | Plan | Activa | Descuento |
|---|---|---|---|---|---|
| `22222222-0000-0000-0000-000000000001` | Ana Gutiérrez | Freelancer | Flexible | Sí | 5 % |
| `22222222-0000-0000-0000-000000000002` | TechSol S.R.L. | Empresa | Equipo | Sí | 10 % |
| `22222222-0000-0000-0000-000000000003` | Carlos Méndez | Freelancer | Dedicado | **No** (vencida) | 0 % |
| `22222222-0000-0000-0000-000000000004` | Fundación Crecer | Externo | — | — | 0 % |

Planes: Pase diario 0 %, Flexible 5 %, Dedicado 8 %, Equipo 10 %.

### Personal (para `performedBy`)
- Dirección: "Lucía Paz" · Recepción: "Recepción Nodo" · Operaciones: "Host de turno".

---

## 6. Context map implementado

| Relación | Patrón | Mecanismo en el prototipo |
|---|---|---|
| Espacios y Recursos [U] → Agenda y Eventos [D] | Customer/Supplier + OHS/PL | Llamada in-process síncrona a `ISpacesCatalog` (External) vía adaptador en Infrastructure. |
| Membresías [U] → Agenda y Eventos [D] | Conformist | Adaptador que adopta `IsActiveMember` + `DiscountPercentage` tal cual. |
| Pasarela de Pagos [U] → Agenda y Eventos [D] | **ACL** | Webhook `POST /api/integrations/payment-gateway/webhook` con payload "feo" del proveedor; el ACL lo traduce a `AccreditDepositCommand`. |
| Agenda y Eventos [U] → Facturación [D] | Customer/Supplier | Integration events (Outbox) → handler en External que registra ledger. |
| Agenda y Eventos [U] → Control de Acceso [D] | **ACL** | Handler traduce `HallEventFirmlyConfirmed` a una credencial temporal (PIN + QR) con capacidad = aforo y ventana [inicio − 1 h, fin]. |
| Agenda y Eventos → Notificaciones | Published Language (bus async) | Handler genérico que registra todos los integration events como notificaciones legibles en español. |
| Quotes → EventLifecycle | Published Language | `QuoteAcceptedIntegrationEvent` (async, Outbox). |
| Quotes / EventLifecycle → HallSchedule | API de módulo (síncrona) | `IHallScheduleModuleApi` en `HallSchedule.Contracts`: `CheckAvailability`, `PlaceProvisionalBlock`, `PlaceDefinitiveBlock`. |
| EventLifecycle → HallSchedule | Published Language | Async: confirmar ⇒ consolidar bloqueo; rechazar/expirar/cancelar ⇒ liberar. |

**[Decisión] Desviación respecto al pseudocódigo 5.2 del documento**: allí `HallEvent` y `AgendaSalon` se
guardan en una misma transacción. Como son agregados de BCs distintos (cada uno con su DbContext/schema),
se aplica *un agregado por transacción*: la colocación del bloqueo es **síncrona** (garantiza no solapamiento
al crear la solicitud) y la consolidación/liberación es **asíncrona** vía Outbox (consistencia eventual). La
restricción de exclusión en Postgres actúa como defensa adicional contra solapamientos.

**[Decisión]** El saldo a favor se modela como agregado `CustomerCredit` dentro de `EventLifecycle` (el
término pertenece al lenguaje ubicuo de esta área) y se publica a Facturación vía integration events.

---

## 7. Eventos

### Domain events (dentro de cada módulo)
- Quotes: `QuoteIssued`, `QuoteAccepted`, `QuoteExpired`.
- EventLifecycle: `EventRequestCreated`, `HallEventApprovedByManagement`, `EventRequestRejected`,
  `GuaranteeDepositAccredited`, `HallEventFirmlyConfirmed`, `ExtraServicesAssigned`, `HallEventStarted`,
  `HallEventCompleted`, `HallEventCancelledWithCredit`, `HallEventRescheduledWithCredit`, `HallEventExpired`,
  `CustomerCreditGranted`, `CustomerCreditApplied`.
- HallSchedule: `SlotProvisionallyBlocked`, `SlotBlockConsolidated`, `SlotReleased`.

### Integration events (Published Language, en `*.Contracts`)
Mismo nombre con sufijo `IntegrationEvent`, payload con primitivos (Guid, DateOnly, TimeOnly, decimal, string).
Siempre incluyen `Id` (Guid) y `OccurredOnUtc`. Payloads mínimos según el documento:
- `EventRequestCreated`: EventId, HallId, Date, Start, End, Attendance, CustomerId.
- `HallEventApprovedByManagement`: EventId, ApprovedBy, DepositPercentage, DepositRequired, DepositDeadlineUtc.
- `GuaranteeDepositAccredited`: EventId, Amount, TransactionId, AccreditedTotal, IsCovered.
- `HallEventFirmlyConfirmed`: EventId, HallId, Date, Start, End, Attendance, CustomerId, Total, DepositAccredited, ExtraServices[].
- `HallEventCancelledWithCredit`: EventId, CustomerId, CreditAmount.
- `HallEventCompleted`: EventId, CustomerId, Total, DepositAccredited, RemainingToSettle.

---

## 8. Datos semilla (migración `SeedDemoData` por módulo)

Fechas relativas a `CURRENT_DATE` (D = día de aplicación de la migración). IDs fijos:

| Id | Escenario | Cliente | Fecha / hora | Estado |
|---|---|---|---|---|
| Q1 `33333333-…-0001` | Cotización vigente | Ana | D+12, 09–13 h, 40 pers. | `Issued` |
| Q2 `33333333-…-0002` | Cotización vencida | Carlos | emitida D−6 | `Expired` |
| Q3..Q7 | Cotizaciones aceptadas que originaron E1..E5 | — | — | `Accepted` |
| E1 `44444444-…-0001` | Pendiente de Dirección | Fundación Crecer | D+10, 14–18 h, 60 pers. | `Requested` (bloqueo provisional) |
| E2 `44444444-…-0002` | Aprobado con depósito parcial (30 %, pagado la mitad) | TechSol | D+14, 09–15 h, 70 pers. | `ApprovedByManagement` |
| E3 `44444444-…-0003` | Confirmado con proyector + coffee break | Ana | D+7, 10–14 h, 35 pers. | `Confirmed` (bloqueo definitivo) |
| E4 `44444444-…-0004` | Realizado | TechSol | D−5, 08–16 h, 80 pers. | `Completed` |
| E5 `44444444-…-0005` | Cancelado con saldo a favor 600 Bs (para demo de reprogramación) | TechSol | D+20 | `Cancelled` |
| E6 `44444444-…-0006` | Rechazado | Carlos | D+9 | `Rejected` |

`CustomerCredit` de TechSol = 600 Bs. Los bloqueos de `HallSchedule` deben ser coherentes con la tabla
(E1/E2 provisional, E3/E4 definitivo, E5/E6 liberado). Los totales de cada evento se calculan con BR-06..08
y deben cuadrar con las cotizaciones semilla.

---

## 9. Catálogo de invariantes (criterio principal de evaluación)

Cada invariante tiene un `code` estable (el que devuelve la API en el 422), un dueño en el dominio y, cuando
aplica, una restricción en BD como defensa en profundidad. Los IDs `INV-xx` deben aparecer en el nombre o
comentario de sus tests y en el Laboratorio de invariantes del front.

### Agregado raíz `HallEvent` (las 5 invariantes del documento, §4.1)

| ID | Invariante | Dueño | Code | BD |
|---|---|---|---|---|
| INV-01 | Duración entera, continua, entre 1 y 8 h en el mismo día (BR-01) | VO `TimeSlot` | `EVENT.DURATION_OUT_OF_RANGE` | `CHECK (end_time > start_time AND end_time - start_time BETWEEN '1 hour' AND '8 hours')` |
| INV-02 | Depósito exigido entre 20 % y 50 % del total (BR-14) | VO `GuaranteeDeposit` | `EVENT.DEPOSIT_PERCENTAGE_OUT_OF_RANGE` | `CHECK (deposit_percentage BETWEEN 20 AND 50)` |
| INV-03 | Confirmación en firme solo con aprobación de Dirección **y** depósito cubierto (BR-16) | `HallEvent.ConfirmFirmly` | `EVENT.CONFIRMATION_PRECONDITIONS_NOT_MET` | — |
| INV-04 | Aforo entre 1 y 80 (BR-04) | VO `Attendance` | `EVENT.ATTENDANCE_OUT_OF_RANGE` | `CHECK (attendance BETWEEN 1 AND 80)` |
| INV-05 | Cancelar un evento con depósito no devuelve dinero: todo lo acreditado pasa íntegro a saldo a favor (BR-21) | `HallEvent.Cancel` + `CancellationPolicy` | `EVENT.CANCELLATION_NOT_ALLOWED` (estado inválido) | `CHECK (credit_generated >= 0)` |

### `HallEvent` — invariantes de ciclo de vida

| ID | Invariante | Code |
|---|---|---|
| INV-06 | Solo transiciones válidas de la máquina de estados (§4); cualquier otra falla | `EVENT.INVALID_STATE_TRANSITION` |
| INV-07 | Dirección decide dentro del día de la solicitud (BR-12) | `EVENT.DECISION_DEADLINE_EXCEEDED` |
| INV-08 | Rechazo requiere motivo ≥ 10 caracteres (BR-13) | `EVENT.REJECTION_REASON_REQUIRED` |
| INV-09 | Solo se acredita depósito en `ApprovedByManagement`; un `TransactionId` se acredita una sola vez (BR-15) | `EVENT.DEPOSIT_NOT_ACCEPTED` (la repetición es idempotente, no error) |
| INV-10 | Depósito acreditado nunca negativo ni en otra moneda | `MONEY.CURRENCY_MISMATCH` / `MONEY.NEGATIVE_AMOUNT` |
| INV-11 | Inicio solo desde inicio − 1 h y antes del fin (BR-18) | `EVENT.START_OUTSIDE_WINDOW` |
| INV-12 | Reprogramar solo eventos cancelados con saldo > 0, conservando duración, aforo, extras y total (BR-23) | `EVENT.RESCHEDULE_NOT_ALLOWED` |
| INV-13 | Los servicios extra solo se modifican a través de la raíz; equipos ≤ stock (BR-08) | `EVENT.EXTRA_SERVICE_INVALID` |

### `Quote`

| ID | Invariante | Code | BD |
|---|---|---|---|
| INV-14 | Vigencia de 96 h; vencida no se acepta (BR-09) | `QUOTE.EXPIRED` | `CHECK (expires_at_utc = issued_at_utc + interval '96 hours')` |
| INV-15 | Precio y servicios congelados al aceptar (no hay método que los cambie después) | (por diseño, sin método) | — |
| INV-16 | Horario operativo y anticipación (BR-02, BR-05) | `QUOTE.SLOT_OUTSIDE_OPERATING_HOURS` / `QUOTE.DATE_OUT_OF_RANGE` | — |
| INV-17 | Descuento de membresía entre 0 y 10 % y solo sobre el salón (BR-07) | `QUOTE.INVALID_DISCOUNT` | `CHECK (membership_discount_percentage BETWEEN 0 AND 10)` |

### `HallSchedule`

| ID | Invariante | Code | BD |
|---|---|---|---|
| INV-18 | Ningún bloqueo activo solapa a otro considerando 1 h de montaje y 1 h de limpieza (BR-03) | `SCHEDULE.SLOT_OVERLAP` | `EXCLUDE USING gist (...)` |
| INV-19 | Un evento tiene como máximo un bloqueo activo | `SCHEDULE.DUPLICATE_BLOCK` | índice único parcial `(event_id) WHERE status = 'Active'` |
| INV-20 | Equipos asignados ≤ stock del inventario | `SCHEDULE.EQUIPMENT_UNAVAILABLE` | — |

### `CustomerCredit`

| ID | Invariante | Code | BD |
|---|---|---|---|
| INV-21 | El saldo nunca es negativo; aplicar más de lo disponible aplica solo lo disponible | `CREDIT.INSUFFICIENT` | `CHECK (balance >= 0)` |
| INV-22 | El saldo a favor no se convierte en dinero: no existe operación de retiro/reembolso | (por diseño, sin método) | — |

**Regla de oro:** la API nunca devuelve 500 por una invariante; siempre 422 con `code`. Una violación detectada
solo por la BD (carrera concurrente) se traduce a 409.
