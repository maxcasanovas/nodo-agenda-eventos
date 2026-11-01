# Arquitectura

Monolito modular con DDD táctico y Clean Architecture. Tres Bounded Contexts internos (`Quotes`, `EventLifecycle`,
`HallSchedule`), cada uno con su propio modelo, `DbContext` y *schema* de PostgreSQL, que solo se conocen a través de
su Published Language (`*.Contracts`). Los siete contextos externos viven simulados en `Nodo.External.Simulated`.

## 1. Capas de un módulo

Las flechas son referencias de proyecto (el que apunta depende del apuntado). Las reglas las verifica
`Nodo.ArchitectureTests` (108 tests): el dominio no depende de nada salvo el SharedKernel, y ningún módulo toca los
internos de otro.

```mermaid
flowchart TB
  subgraph Host["Nodo.Api (composition root)"]
    API["Controllers · ProblemDetails · OpenAPI/Scalar · Laboratorio de invariantes"]
  end

  subgraph Module["Módulo (p. ej. EventLifecycle)"]
    direction TB
    INF["<b>Infrastructure</b><br/>EF Core (DbContext + schema) · repositorios · queries<br/>adaptadores ACL / Conformist hacia External"]
    APP["<b>Application</b><br/>comandos y queries (MediatR) · validadores · políticas<br/>mappers dominio→integración · handlers de integración"]
    DOM["<b>Domain</b><br/>agregados · Value Objects · servicios de dominio<br/>domain events · puertos de repositorio"]
    CON["<b>Contracts</b> (Published Language)<br/>integration events · API síncrona del módulo"]
  end

  SK["Nodo.SharedKernel<br/>Entity · AggregateRoot · ValueObject · Money · DomainException"]
  BBA["BuildingBlocks.Application<br/>IClock · behaviors · IUnitOfWork / IOutbox / IInbox · IEventBus"]
  BBI["BuildingBlocks.Infrastructure<br/>EfUnitOfWork · Outbox/Inbox · OutboxProcessor · InMemoryEventBus · DemoClock"]
  EXT["Nodo.External.Simulated<br/>Espacios · Membresías · Pasarela · Facturación · Acceso · Catering · Notificaciones"]

  API --> INF
  API --> APP
  INF --> APP
  APP --> DOM
  APP --> CON
  DOM --> SK
  CON --> BBA
  APP --> BBA
  INF --> BBI
  INF -. "solo vía adaptadores" .-> EXT
  EXT --> CON
```

| Capa | Qué contiene | Qué NO puede hacer |
|---|---|---|
| **Domain** | Agregados *always-valid* (constructores privados, fábricas, sin setters), VOs, servicios de dominio y todas las invariantes. Recibe el tiempo como parámetro (`nowUtc`). | Referenciar EF, MediatR, ASP.NET, FluentValidation ni otro módulo. |
| **Application** | Un handler por caso de uso: carga, invoca al dominio, guarda. FluentValidation valida solo la *forma*. | Contener reglas de negocio ni conocer EF. |
| **Infrastructure** | `DbContext` por módulo, configuraciones Fluent, repositorios, restricciones de BD, adaptadores a los contextos externos. | Ser referenciada por Application. |
| **Contracts** | Eventos de integración (solo primitivos) y la interfaz síncrona del módulo. | Exponer tipos del dominio. |

## 2. Context map implementado

```mermaid
flowchart LR
  subgraph Externos["Contextos externos simulados"]
    ESP["Espacios y Recursos"]
    MEM["Membresías y Planes"]
    PAS["Pasarela de Pagos"]
    FAC["Facturación y Cobros"]
    ACC["Control de Acceso"]
    NOT["Notificaciones"]
    CAT["Proveedor de Catering"]
  end

  subgraph Nodo["Agenda y Eventos (monolito modular)"]
    QT["Quotes"]
    EL["EventLifecycle"]
    HS["HallSchedule"]
  end

  ESP -- "[U] OHS/PL · Customer/Supplier [D]" --> QT
  ESP -- "OHS/PL" --> EL
  ESP -- "OHS/PL" --> HS
  MEM -- "[U] Conformist [D]" --> QT
  PAS -- "[U] ACL [D]" --> EL
  QT -- "Published Language (Outbox)" --> EL
  QT -- "API de módulo (síncrona)" --> HS
  EL -- "API de módulo (síncrona)" --> HS
  EL -- "Published Language (Outbox)" --> HS
  EL -- "[U] Customer/Supplier [D]" --> FAC
  EL -- "[U] ACL [D]" --> ACC
  EL -- "Published Language" --> NOT
  EL -- "Customer/Supplier" --> CAT
```

Detalle y justificación en `docs/domain/business-rules.md` §6.

## 3. Outbox / Inbox

Un agregado por transacción: cada comando cambia un solo agregado y deja los eventos de integración en la tabla
`outbox_messages` **de su propio schema, dentro de la misma transacción**. Un proceso aparte los entrega.

```mermaid
flowchart LR
  subgraph TX["Una transacción (un DbContext)"]
    CMD["Handler del comando"] --> AGG["Agregado<br/>levanta domain events"]
    AGG --> UOW["EfUnitOfWork<br/>despacha domain events en bucle"]
    UOW --> MAP["Mappers: domain event → integration event"]
    MAP --> OB[("outbox_messages")]
    AGG --> DB[("tablas del agregado")]
  end
  OB -- "cada 1 s · 20 mensajes<br/>FOR UPDATE SKIP LOCKED · 5 intentos" --> PROC["OutboxProcessor&lt;TContext&gt;"]
  PROC --> BUS["InMemoryEventBus<br/>un scope por handler"]
  BUS --> INBOXH["Decorador Inbox<br/>(idempotencia por mensaje y handler)"]
  INBOXH --> H1["Handler en otro módulo<br/>(HallSchedule, EventLifecycle…)"]
  INBOXH --> H2["Handler en External<br/>(Acceso, Facturación, Catering, Notificaciones)"]
  INBOXH --> IB[("inbox_messages")]
```

- **Atomicidad**: agregado + outbox se confirman con un único `SaveChanges`. Si el comando falla, no sale ningún evento.
- **Entrega al menos una vez**: el procesador reintenta hasta 5 veces y deja el error en el mensaje. El **Inbox** hace que
  repetir un mensaje no repita su efecto: cada consumidor registra `(id del mensaje, consumidor)` en su `inbox_messages`,
en la misma transacción que su trabajo; si el handler falla, no queda registrado y el mensaje se reintenta.
- **Síncrono solo donde hace falta**: colocar un bloqueo exige certeza inmediata (si el salón está ocupado, la solicitud
  no nace), así que EventLifecycle llama a `IHallScheduleModuleApi`. Consolidar o liberar es asíncrono (consistencia
  eventual, visible en la UI por el *polling*).
- **Defensa extra**: si dos comandos concurrentes chocan, la restricción `EXCLUDE`/índice único de PostgreSQL rechaza uno y
  `InvariantConstraintMap` lo traduce a 409 con el `code` de la invariante.

## 4. Secuencia central: confirmar un evento en firme

Del pago que cubre el depósito hasta sus consecuencias en los demás contextos.

```mermaid
sequenceDiagram
  autonumber
  participant PAS as Pasarela (externa)
  participant API as Webhook<br/>/api/integrations/payment-gateway/webhook
  participant ACL as PaymentGatewayAclAdapter (ACL)
  participant APP as AccreditDepositHandler
  participant HE as HallEvent (agregado)
  participant OB as Outbox (event_lifecycle)
  participant PRC as OutboxProcessor + Bus
  participant HS as HallSchedule
  participant ACC as Control de Acceso
  participant FAC as Facturación
  participant NOT as Notificaciones
  participant CAT as Catering

  PAS->>API: payload del proveedor (txn_id, amount_cents, status, metadata.merchant_ref)
  API->>ACL: ReceiveAsync(payload)
  Note over ACL: traduce centavos→Bs, valida forma,<br/>DECLINED no llega al dominio
  ACL->>APP: AccreditDepositCommand(eventId, Bs, txnId)
  APP->>HE: AccreditDeposit(monto, txnId, nowUtc)
  Note over HE: INV-09 (estado y txnId único)<br/>INV-10 (moneda y monto)
  alt depósito cubierto
    HE->>HE: ConfirmFirmly() · INV-03
    HE-->>APP: domain events (GuaranteeDepositAccredited,<br/>HallEventFirmlyConfirmed)
    Note over APP,HE: política AssignExtraServicesOnConfirmed (BR-20):<br/>misma transacción → HallEvent.AssignExtraServices()<br/>→ domain event ExtraServicesAssigned
  end
  APP->>OB: mappers → integration events (misma transacción)
  APP-->>ACL: resultado (estado, total acreditado, excedente)
  ACL-->>API: WebhookAck
  API-->>PAS: 200 {accredited, outcome, eventStatus}

  loop cada 1 s
    PRC->>OB: lee mensajes pendientes
  end
  par consecuencias asíncronas
    PRC->>HS: HallEventFirmlyConfirmed → consolidar bloqueo definitivo + equipos
    PRC->>ACC: ACL traduce → credencial (PIN + QR, ventana −1 h → fin, aforo)
    PRC->>FAC: ledger: DepositoRegistrado
    PRC->>NOT: notificación al cliente
    PRC->>CAT: ExtraServicesAssigned → orden de catering (solo líneas de catering)
  end
```

## 5. Otras decisiones de diseño relevantes

- **Tiempo**: el dominio nunca lee el reloj; recibe `nowUtc`. `DemoClock` (único lugar con `DateTime.UtcNow`) permite
  adelantar el tiempo de la demo; `ScopedClock` desplaza el reloj solo dentro de una sonda de invariantes.
- **Persistencia**: un `DbContext` y un schema por módulo (`quotes`, `event_lifecycle`, `hall_schedule`), nombres en
  snake_case, concurrencia optimista con `xmin`, VOs como *complex types*, restricciones nombradas con el id de la invariante.
- **Errores**: `ProblemMapping` es la única fuente del mapeo error → HTTP: 400 forma, 404 no existe, 409 concurrencia o
  solape detectado por la BD, 422 regla de dominio con su `code`.
- **Laboratorio de invariantes**: las sondas ejecutan el caso de uso real con tres `DbContext` que comparten una conexión y
  una transacción que siempre se revierte; la demo queda intacta.
