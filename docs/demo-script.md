# Guion de la demo (≈ 9–10 min)

Recorrido para el video. Cada paso indica **qué se hace en pantalla**, **qué decir** y **qué concepto DDD se ilustra**.
Todos los pasos se probaron de punta a punta (con un navegador automatizado) contra `docker compose up --build`.

**D** es el día en que se levantó la base de datos (las semillas son relativas a D). Las fechas del guion son «D + n días».

## Antes de grabar

1. `./scripts/reset.sh` (o `scripts/reset.ps1`) → BD con las semillas. **Hazlo antes de cada toma**: el paso 3 vence las
   cotizaciones y solicitudes sembradas de forma permanente.
2. Pestañas abiertas: la app `http://localhost:3000`, Scalar `http://localhost:5080/scalar`, pgAdmin
   `http://localhost:5050` (`docker compose --profile tools up -d pgadmin`; servidor `db`, puerto 5432, usuario y clave `nodo`),
   el editor con `HallEvent.cs`, `Attendance.cs` y `docs/architecture.md`, y una terminal en la raíz del repo.
3. Navegador a 1920×1080 con la consola de desarrollo cerrada. Rol inicial: **Cliente → Ana Gutiérrez**.
4. Hora del evento: el reloj de demo conserva la hora actual al adelantar días. En el paso 3 elige una **hora de inicio igual
   o hasta ~1 h anterior a la hora local actual** (por ejemplo, si son las 10:30, inicio 09:00 y 4 h), para que al llegar al
   día del evento (paso 6) «ahora» caiga dentro de la ventana de inicio. El evento debe empezar entre 08:00 y 18:00.
5. Es normal ver en el log de la API, al crear la BD vacía, dos líneas `fail: …Database.Command` sobre la tabla de historial
   de migraciones (EF la consulta antes de crearla) y los `warn` de reglas de dominio rechazadas a propósito.

## Datos semilla que se usan

| Id | Cliente | Cuándo | Estado | Para qué |
|---|---|---|---|---|
| Q1 | Ana (5 %) | D+12, 09–13 h, 40 pers. | cotización vigente | paso 3 |
| E1 | Fundación Crecer | D+10, 14–18 h | `Requested` (bloqueo provisional) | calendario |
| E2 | TechSol (10 %) | D+14, 09–15 h | `ApprovedByManagement`, 30 % con la mitad pagada | calendario |
| E3 | Ana | D+7, 10–14 h, proyector + coffee break | `Confirmed` (bloqueo definitivo) | calendario, paso 8 |
| E4 | TechSol | D−5 | `Completed` | historial |
| E5 | TechSol | D+20 | `Cancelled` con **Bs 600 de saldo a favor** | reprogramación (paso 7) |

## Línea de tiempo

| # | Paso | Min |
|---|---|---|
| 1 | Context map y estructura del repo | 0:30 |
| 2 | Calendario con datos semilla | 0:30 |
| 3 | Fase I: cotizar (tarifa degresiva, miembro) y vencimiento | 1:30 |
| 4 | Fase II: aprobación de Dirección | 0:45 |
| 5 | Fase III: pagos por la pasarela, confirmación, credencial, catering, Outbox | 1:30 |
| 6 | Fase IV: iniciar, finalizar y liquidar | 0:45 |
| 7 | Fase V: rechazo, cancelación con saldo a favor y reprogramación | 1:15 |
| 8 | **Invariantes** | **2:30** |
| 9 | Código: agregado y Value Object | 0:45 |
| 10 | Tests | 0:30 |

---

## 1. Context map y estructura (0:30)

**En pantalla**: `README.md` en el diagrama «Context map»; luego el árbol `src/` (3 módulos × 4 proyectos, `External`, `Nodo.Api`).

**Decir**: «Tres bounded contexts internos —Cotizaciones, Ciclo del Evento y Agenda del Salón— y siete contextos externos
simulados. Con Espacios usamos OHS/Published Language; con Membresías somos *conformistas*; la Pasarela de Pagos y Control de
Acceso pasan por una *capa anticorrupción*; entre módulos solo hay Published Language.»

**Concepto**: *Context Map*, relaciones U/D, Published Language, módulos como límites de modelo.

## 2. Calendario con datos semilla (0:30)

**En pantalla**: menú **Calendario del salón** → semana siguiente. Se ven E3 (verde, definitivo, D+7), E1 (ámbar, D+10) y
E2 (ámbar, D+14), cada uno con su hora de montaje y de limpieza en gris rayado.

**Decir**: «Provisional es una solicitud en espera; definitivo es un evento confirmado. El gris son los buffers de una hora
antes y después: el salón no se puede reservar dentro de ellos.»

**Concepto**: agregado `HallSchedule` (uno por salón y día), `ScheduleBlock`, buffers (BR-03).

## 3. Fase I — Cotizar y vencimiento (1:30)

**En pantalla** (rol Cliente, Ana):

1. **Cotizar**: fecha D+20, inicio 09:00 (ver «Hora del evento» arriba), duración 4 h, aforo 40, **Proyector ×1**,
   **Coffee break**. El desglose en vivo muestra: base 4 h × 500 = 2.000 · **factor 0,9** → alquiler Bs 1.800 ·
   **descuento de membresía (5 %) = − Bs 90** (solo sobre el salón) · Proyector Bs 150 · Coffee break 40 × 35 = Bs 1.400 →
   **total Bs 3.260**. Aviso verde: «El salón está disponible».
2. Cambiar el aforo a **90**: el panel de precio muestra el 422 del dominio con su código (se verá con más calma en el paso 8);
   volver a 40.
3. **Emitir cotización** → **Mis cotizaciones** (estado «Vigente», cuenta regresiva de 96 h).
4. **Vencimiento**: en el reloj de demo, **+4 días**. Las cotizaciones pasan a **«Vencida»** y el botón *Aceptar* queda
   deshabilitado. Pulsar **Restablecer**.

> Esto vence también la cotización sembrada Q1 y deja caducar las solicitudes sembradas E1 y E2; lo que E2 tenía pagado
> (Bs 324) pasa a saldo a favor de TechSol, que queda en **Bs 924** (600 de E5 + 324 de E2). Es esperado: el job de
> expiración corre al mover el reloj.

**Decir**: «El precio lo calcula el dominio —servicio `EventPricingCalculator` y `PriceBreakdown`—; el front solo lo muestra.
El descuento del socio solo aplica al alquiler, no a los extras.»

**Concepto**: agregado `Quote`, Value Objects (`PriceBreakdown`, `Money`), servicio de dominio, vigencia (BR-09), reloj
inyectable (`IClock`: el dominio nunca llama a `DateTime.Now`).

**Después de Restablecer**: crear de nuevo la cotización de Ana (mismos datos, D+20, 09:00–13:00, Proyector, Coffee break) y
**Aceptar**.

## 4. Fase II — Bandeja de Dirección (0:45)

**En pantalla**: rol **Dirección** → **Bandeja de aprobaciones**. Aparece «Ana Gutiérrez · D+20 · 09:00–13:00» con «Responder en
… h». Mover el slider a **30 %**: «Depósito de garantía: 30 % ≈ Bs 978,00». **Aprobar con 30 %**.

**Decir**: «Aceptar la cotización creó automáticamente la solicitud y bloqueó el calendario de forma provisional. Dirección tiene
hasta el fin del día para decidir; ahora fija el porcentaje del depósito.»

**Concepto**: *policy* «cotización aceptada → solicitud» (`QuoteAcceptedHandler`), máquina de estados de `HallEvent`
(`Requested → ApprovedByManagement`), VO `GuaranteeDeposit`.

## 5. Fase III — Pasarela, confirmación y consecuencias (1:30)

**En pantalla**: **Sistemas externos → Pasarela**: link «Depósito de garantía (30 %)» por Bs 978.

1. Monto **400** → **Pagar**: aviso «Webhook 200: ACCREDITED · evento ApprovedByManagement»; el link queda `PARTIALLY_PAID` y el
   evento sigue sin confirmarse.
2. Dejar el monto vacío → **Pagar** (el resto): «Webhook 200: ACCREDITED_AND_COVERED · evento Confirmed».
3. Pestañas, en este orden: **Control de acceso** (PIN de 6 dígitos, aforo 40, vigencia desde 1 h antes), **Catering** (Coffee
   break × 40), **Facturación** (`DepositoRegistrado` Bs 978), **Eventos de integración** (log con módulo, hora, entregado y
   JSON expandible).
4. Calendario: el bloque de Ana pasó a **verde** con el proyector reservado.

**Decir**: «El pago llega con el formato del proveedor —centavos, `merchant_ref`—. La capa anticorrupción lo traduce a
nuestro comando `AccreditDeposit`. Al cubrirse el depósito el agregado se confirma él mismo y, **dentro de la misma
transacción**, deja sus eventos en el Outbox. Un procesador los entrega, y de ahí salen la credencial, el catering, el
asiento contable y el bloqueo definitivo.» Mostrar [`docs/architecture.md`](architecture.md) §4 (secuencia) un instante.

**Concepto**: **ACL**, idempotencia por `TransactionId`, `HallEvent.ConfirmFirmly`, **Transactional Outbox / Inbox**,
consistencia eventual entre agregados.

## 6. Fase IV — Ejecución y liquidación (0:45)

**En pantalla**: reloj de demo **+4 días** cinco veces (llegar al día del evento, D+20). Rol **Operaciones → Eventos de hoy**:
checklist con equipos, catering y credencial (PIN). **Iniciar uso del salón** → «En curso». **Finalizar evento** (el evento
sale de la lista: ya no está en operación). **Sistemas externos → Facturación**: `LiquidacionFinalCompletada` por
**Bs 2.282** (3.260 − 978). Pulsar **Restablecer**.

> Si **Iniciar** responde 422 `EVENT.START_OUTSIDE_WINDOW`, la hora local simulada quedó fuera de la ventana del evento:
> usa **+1 h** hasta caer entre una hora antes del inicio y el fin. (Es también un buen ejemplo de INV-11.)

**Decir**: «El inicio solo es posible desde una hora antes del evento; al finalizar, Facturación liquida el saldo restante.»

**Concepto**: ciclo de vida del agregado, regla de ventana de inicio (INV-11), integration event `HallEventCompleted`.

## 7. Fase V — Rechazo, cancelación y reprogramación (1:15)

**En pantalla**:

- **Rechazo (rama A)**: como Fundación Crecer, cotizar D+30 y aceptar → Dirección **Rechazar** con motivo «no» →
  422 `EVENT.REJECTION_REASON_REQUIRED`; con un motivo de más de 10 caracteres → «Rechazado». El bloqueo desaparece del calendario.
- **Cancelación (rama B)**: crear y confirmar otro evento (aprobar y pagar el link completo) → como Cliente, **Cancelar**: el
  diálogo avisa «**No hay reembolso en dinero**: lo acreditado pasa íntegro a saldo a favor». Confirmar → el detalle muestra el
  saldo a favor y la credencial se revoca.
- **Reprogramación (rama C)**: cliente **TechSol** → **Mis eventos** → el evento cancelado (E5: generó **Bs 600**; el
  saldo del cliente es **Bs 924** por los 324 de E2) → **Reprogramar** a D+45. Se mantiene la duración (5 h) y el aviso
  confirma que el salón está libre. Como el saldo cubre el depósito de Bs 600, queda **Confirmado** y a TechSol le quedan **Bs 324**.
- **Para el paso 8**: deja una solicitud pendiente de Dirección (por ejemplo, cotizar y aceptar como Carlos Méndez, D+35) sin aprobarla.

**Decir**: «Cancelar nunca devuelve dinero: se convierte en saldo a favor (`CustomerCredit`), que solo sirve para reprogramar.
Reprogramar conserva duración, aforo, extras y total.»

**Concepto**: política de cancelación, agregado `CustomerCredit`, regla de negocio frente a la UI (la UI solo la muestra).

## 8. Invariantes (2:30) — el bloque central

**En pantalla**:

1. **Laboratorio de invariantes** → **Probar todas**. Se llenan 22 filas y aparece **`22/22 protegidas`**. Señalar una fila
   rechazada (código, mensaje en español, HTTP 422) y las tres «Protegidas por diseño» (INV-03, INV-15 e INV-22: el sistema
   simplemente no ofrece esa operación). El ícono **BD** marca las que además tienen restricción en PostgreSQL.
   *«Cada prueba usa el caso de uso real dentro de una transacción que siempre se revierte: la demo queda intacta.»*
2. **Violaciones desde los formularios reales** (rol Cliente/Recepción/Dirección). Ningún formulario las bloquea en el front:
   - **Aforo 81** (Cotizar): `EVENT.ATTENDANCE_OUT_OF_RANGE` — «El aforo debe estar entre 1 y 80 personas (se indicó 81).»
   - **Duración 9 horas** (Cotizar): `EVENT.DURATION_OUT_OF_RANGE`.
   - **Solapamiento solo por buffer** (Cotizar, Ana, el día de E3 —D+7—, inicio **15:00**, 3 h): E3 termina a las 14:00 y las
     franjas no se cruzan, pero su hora de limpieza llega hasta las 15:00. El panel avisa «El salón no está disponible: la franja
     15:00-18:00 se solapa con otra reserva (10:00-14:00) considerando 1 h de montaje y 1 h de limpieza»; **Emitir** (emitir
     no bloquea) → **Aceptar** → 422 `SCHEDULE.SLOT_OVERLAP`.
   - **Depósito 60 %** (Dirección, bandeja, con la solicitud pendiente del paso 7): escribir **60** en el campo numérico junto
     al slider → **Aprobar con 60 %** → `EVENT.DEPOSIT_PERCENTAGE_OUT_OF_RANGE` («…debe ser entre 20 % y 50 % del total (se indicó 60 %)»).
3. **Scalar** (`/scalar`): `POST /api/quotes` con `attendance: 81` → **422** con `code` y el mensaje; abrir la descripción de la
   operación para ver la lista de `INV-xx` que puede disparar.
4. **pgAdmin → Query Tool** (defensa en profundidad: la base rechaza lo que el dominio no vio):

   ```sql
   -- INV-02: el depósito de E2 no puede pasar de 50 %
   UPDATE event_lifecycle.hall_events SET deposit_percentage = 60
    WHERE id = '44444444-0000-0000-0000-000000000002';
   -- ERROR: new row for relation "hall_events" violates check constraint "ck_hall_events_inv02_deposit_percentage"

   -- INV-18: una reserva 15:00–18:00 el día de E3 (10–14 h) no choca con el evento, pero sí con su buffer de limpieza
   INSERT INTO hall_schedule.schedule_blocks
     (id, event_id, hall_id, date, schedule_date, start_time, end_time, kind, status, created_at_utc)
   VALUES (gen_random_uuid(), gen_random_uuid(), '11111111-1111-1111-1111-111111111111',
           '<D+7 en formato AAAA-MM-DD>', '<D+7>', '15:00', '18:00', 'Provisional', 'Active', now());
   -- ERROR: conflicting key value violates exclusion constraint "ex_schedule_blocks_inv18_overlap"
   ```

**Decir**: «Las invariantes viven en el agregado o en el Value Object, jamás en el controller ni en el front. La validación de
FluentValidation solo revisa la forma. Y como defensa extra, la base de datos las repite con `CHECK` y `EXCLUDE`: si una
carrera concurrente se saltara el dominio, PostgreSQL lo rechaza y la API lo traduce a un 409 con el mismo código.»

**Concepto**: *always-valid domain model*, invariantes en agregados y VOs, `DomainException` con código estable, defensa en
profundidad. Matriz completa: [`docs/invariants.md`](invariants.md).

## 9. Código: un agregado y un Value Object (0:45)

**En pantalla**: `EventLifecycle.Domain/Attendance.cs`: constructor **privado**, fábrica `Create`, y la regla que lanza
`DomainException(AttendanceOutOfRange, …)`; no existe forma de construir un aforo inválido. Luego `HallEvent.ConfirmFirmly`
(`HallEvent.cs`): comprueba aprobación **y** depósito cubierto, transiciona el estado y levanta `HallEventFirmlyConfirmed`.
Mostrar que `AccreditDeposit` llama a `ConfirmFirmly` al cubrirse el depósito y que no hay setters públicos.

**Decir**: «El handler solo carga, invoca y guarda; las reglas están aquí. Ningún método deja al agregado en un estado inválido:
si lanza, el estado y el historial quedan intactos.»

**Concepto**: agregado raíz, VO inmutable con validación en la creación, eventos de dominio, encapsulamiento.

## 10. Tests (0:30)

**En pantalla**: en la terminal, `dotnet test Nodo.slnx` → **856 tests en verde**. Abrir `HallEventInvariantTests` (caso
válido / límite / inválido por invariante) y `DependencyRulesTests` / `DomainModelProtectionTests` (arquitectura:
el dominio no depende de EF, ningún módulo referencia los internos de otro, ningún agregado tiene setters públicos).

**Decir**: «Cada invariante tiene su test válido, límite e inválido, y la arquitectura se verifica automáticamente.»

**Concepto**: tests de dominio sin infraestructura, tests de arquitectura (`NetArchTest`).

---

## Cierre

«Tres módulos con límites claros, un modelo siempre válido, integración por Published Language con Outbox, anticorrupción hacia
los sistemas externos y las 22 invariantes demostrables desde la aplicación.»
