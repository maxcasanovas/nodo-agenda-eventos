# Spec 05 — Contextos externos simulados, ACLs y API REST documentada con Scalar

## Objetivo
Implementar los contextos externos hardcodeados, los adaptadores (ACL / Conformist / OHS) que materializan el
context map, y exponer toda la funcionalidad por API REST documentada en Scalar.

## `Nodo.External.Simulated`
Todo en memoria (singletons thread-safe), con los datos de `business-rules.md §5`. Cada contexto expone **su
propio modelo**, distinto al de nuestros módulos, para que los adaptadores tengan algo que traducir.

| Contexto | Qué hace | Modelo propio (ejemplo) |
|---|---|---|
| Espacios y Recursos | Catálogo de salón, equipos y catering | `VenueDto { venue_code, max_capacity, assets: [...] }` |
| Membresías y Planes | Directorio de clientes y membresías | `MemberRecord { memberId, plan, status: "ACTIVE"|"EXPIRED", discountPct }` |
| Pasarela de Pagos | Links de pago y envío de webhook | Payload: `{ "txn_id", "amount_cents", "currency_code", "status": "APPROVED"|"DECLINED", "metadata": { "merchant_ref" } }` |
| Facturación y Cobros | Ledger: depósitos, saldos a favor, liquidación final | `LedgerEntry` |
| Control de Acceso | Credenciales temporales (PIN 6 dígitos + texto QR), revocación | `AccessCredential` |
| Notificaciones | Feed de mensajes en español derivados de integration events | `Notification { to, channel, subject, body, sentAt }` |
| Proveedor de Catering | Órdenes recibidas | `CateringOrder` |

## Adaptadores (en `*.Infrastructure` de cada módulo)
- `SpacesCatalogAdapter : ISpacesCatalog` (OHS/PL, Customer/Supplier).
- `MembershipDirectoryAdapter : IMembershipDirectory` (Conformist: adopta `status == ACTIVE` y `discountPct` sin transformarlos).
- **ACL Pasarela** (`PaymentGatewayAclAdapter`): recibe el payload del proveedor, valida `status == APPROVED`, convierte
  `amount_cents` → `Money` BOB, `merchant_ref` → `HallEventId`, y envía `AccreditDepositCommand`. Pagos `DECLINED` se registran y no acreditan.
- Handlers de integración hacia downstream (viven en EventLifecycle.Infrastructure o en External, justifica):
  - `HallEventApprovedByManagement` ⇒ pasarela crea link de pago por el depósito requerido (y por la diferencia en reprogramación).
  - `HallEventFirmlyConfirmed` ⇒ **ACL Control de Acceso** crea credencial (capacidad = aforo, ventana inicio − 1 h a fin);
    Facturación registra depósito.
  - `ExtraServicesAssigned` ⇒ orden al proveedor de catering.
  - `HallEventCancelledWithCredit` ⇒ Control de Acceso revoca; Facturación registra saldo a favor.
  - `HallEventCompleted` ⇒ Facturación registra `LiquidacionFinalCompletada` por el saldo restante.
  - Todos ⇒ Notificaciones genera un mensaje legible (ej. "Dirección: nueva solicitud de Fundación Crecer para el 12/11").

## API REST (`Nodo.Api`)
Agrupar con tags de OpenAPI para que Scalar muestre secciones claras. Documentar cada endpoint (summary,
description, ejemplos de request y respuestas 200/400/404/409/422).

**Cotizaciones** (`/api/quotes`)
- `POST /preview`, `POST /` (emitir), `GET /`, `GET /{id}`, `POST /{id}/accept`.

**Ciclo del Evento** (`/api/hall-events`)
- `GET /`, `GET /{id}`, `GET /pending-approvals`, `GET /today`
- `POST /{id}/approve` `{ depositPercentage, approvedBy }`
- `POST /{id}/reject` `{ reason, rejectedBy }`
- `POST /{id}/start`, `POST /{id}/complete`, `POST /{id}/cancel` `{ reason, cancelledBy }`, `POST /{id}/reschedule` `{ newDate, newStartTime, performedBy }`
- `GET /api/customers/{id}/credit`

**Agenda del Salón** (`/api/hall-schedule`)
- `GET /{hallId}?from=&to=` (calendario), `GET /{hallId}/availability?date=&start=&end=&equipment=`

**Integraciones (ACL)**
- `POST /api/integrations/payment-gateway/webhook` (payload del proveedor).

**Sistemas externos (simulados)** (`/api/external`)
- `GET /catalog`, `GET /customers`, `GET /payment-links`, `POST /payment-links/{id}/pay` `{ amount?, decline? }`
  (construye el payload del proveedor y llama al webhook por HTTP vía `HttpClient` contra la propia API, para que el ACL se ejercite de verdad),
  `GET /access-credentials`, `GET /notifications`, `GET /billing/ledger`, `GET /catering-orders`.

**Demo**
- `GET /api/demo/integration-events` (log de eventos publicados, más recientes primero), clock y jobs (specs 01 y 04).
- `GET /api/demo/invariants`: devuelve el catálogo `INV-xx` (id, descripción en español, agregado dueño, code, si tiene refuerzo en BD).
- `POST /api/demo/invariants/{id}/probe`: ejecuta un intento de violación predefinido **a través del caso de uso real** (no atajos) y devuelve
  `{ invariantId, attempted, rejected, code, message, statusCode }`. Debe existir una sonda por cada invariante con comportamiento observable
  (las "por diseño" devuelven explicación). Las sondas trabajan sobre datos de prueba y no alteran el estado de la demo (operan en transacción
  revertida o sobre entidades efímeras).

**Documentación de invariantes en Scalar**
- Cada endpoint lista en su `description` las `INV-xx` que puede violar y el ejemplo de respuesta 422 con su `code`.

## Tests
- ACL de pasarela: payload APPROVED ⇒ comando correcto (montos en centavos → Bs); DECLINED ⇒ no envía comando; `merchant_ref` inválido ⇒ 400.
- Adaptador de membresías: EXPIRED ⇒ no miembro, 0 %.
- ACL de acceso: ventana y capacidad correctas.

## Criterios de aceptación
- [ ] En Scalar se recorre el ciclo completo: preview → emitir → aceptar → (esperar outbox) evento `Requested` → aprobar 30 % →
      pagar parcial → pagar resto → `Confirmed` → credencial y orden de catering creadas → adelantar reloj → iniciar → completar → liquidación en ledger.
- [ ] Rama A (rechazo), B (cancelación con saldo) y C (reprogramación con saldo) funcionan desde Scalar.
- [ ] Reenviar el mismo `txn_id` no duplica la acreditación.
- [ ] Cada error de regla responde 422 con `code` y mensaje en español.
- [ ] `POST /api/demo/invariants/{id}/probe` devuelve `rejected: true` con el `code` correcto para todas las invariantes observables.
- [ ] Todo verificado con `docker compose up --build` (la llamada HTTP al webhook usa `Api__SelfBaseUrl=http://localhost:8080` dentro del contenedor).
- [ ] Documento OpenAPI generado sin warnings y con ejemplos.
