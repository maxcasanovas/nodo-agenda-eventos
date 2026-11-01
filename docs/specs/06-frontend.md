# Spec 06 — Frontend React (prototipo funcional)

## Objetivo
Interfaz en español que permita demostrar en video el ciclo completo del salón desde la perspectiva de cada
actor, con un calendario visual y un panel que haga visibles los contextos externos y los eventos de integración.

## Stack y estructura (`web/`)
Vite + React 19 + TypeScript strict, TanStack Query v5, React Router, Tailwind v4 + shadcn/ui, FullCalendar
(`@fullcalendar/react`, `daygrid`, `timegrid`, `interaction`).

```
web/src/
  api/            # cliente fetch tipado por recurso (quotes, hallEvents, hallSchedule, external, demo) + tipos
  features/
    quotes/  events/  approvals/  operations/  calendar/  external-panel/
  components/     # layout, RoleSelector, DemoClock, StatusBadge, Money, EventTimeline, ProblemAlert
  context/        # RoleContext (rol + cliente seleccionado, persistido en localStorage)
  lib/            # formato de fechas (es-BO, America/La_Paz) y Bs
```
- Llamadas a la API con rutas relativas `/api/...` (ver Empaquetado Docker).
- Errores `ProblemDetails` se muestran con su `message` en español (toast + inline en formularios).
- Polling: `refetchInterval` 2–3 s en vistas de estado de evento, panel externo y calendario (consistencia eventual visible).

## Layout global
- Header: logo "Nodo · Agenda y Eventos", **selector de rol** (Cliente / Recepción / Dirección / Operaciones);
  si el rol es Cliente, combo para elegir cuál de los 4 clientes; **reloj de demo** con fecha/hora simulada y
  botones "+1 h", "+1 día", "+4 días", "Restablecer" y "Ejecutar jobs".
- Navegación lateral que cambia según rol. "Calendario del salón" y "Sistemas externos" visibles para todos.

## Pantallas por rol

**Cliente**
- *Cotizar*: formulario (fecha, hora inicio, duración 1–8 h, aforo, equipos con cantidad, catering) con **desglose
  en vivo** vía `/api/quotes/preview` (base, factor degresivo, descuento de membresía, extras, total) y aviso de disponibilidad.
- *Mis cotizaciones*: estado, total, cuenta regresiva de vigencia (según reloj de demo), botón "Aceptar".
- *Mis eventos*: lista con estado; detalle con línea de tiempo (`History`), depósito (requerido / acreditado / barra de progreso),
  extras, saldo a favor del cliente, acciones "Cancelar" (confirmación indicando que no hay reembolso en dinero, solo saldo a favor)
  y "Reprogramar" (selector de nueva fecha/hora con validación de disponibilidad).

**Recepción**
- Cotizar en nombre de un cliente (mismo formulario + selector de cliente) y listado de todas las cotizaciones.
- Listado de eventos con filtros; cancelar/reprogramar.

**Dirección**
- *Bandeja de aprobaciones*: solicitudes `Requested` ordenadas por plazo, con tiempo restante del día.
  Aprobar con slider 20–50 % que muestra el monto del depósito; Rechazar con motivo obligatorio.
- Vista de todos los eventos.

**Operaciones**
- *Eventos de hoy*: confirmados y en curso con checklist (equipos asignados, catering, credencial de acceso),
  botones "Iniciar uso del salón" y "Finalizar evento" (habilitados según BR-18/19).

## Calendario del salón
- FullCalendar `timeGridWeek` (y `timeGridDay`), 07:00–23:00, locale `es`.
- Bloques: provisional (ámbar), definitivo (verde), buffers de montaje/limpieza (gris rayado, como eventos de fondo).
- Clic en un bloque abre el detalle del evento. Clic en hueco libre (rol Cliente/Recepción) abre *Cotizar* con fecha/hora precargadas.

## Panel "Sistemas externos"
Pestañas: **Pasarela** (links pendientes con botón "Pagar", campo de monto editable para pago parcial y botón "Rechazar pago"),
**Notificaciones**, **Control de acceso** (credenciales con PIN y QR renderizado en texto/código), **Facturación** (ledger),
**Catering** (órdenes) y **Eventos de integración** (log con tipo, módulo origen, hora y JSON expandible).

## Laboratorio de invariantes (pieza central del video)
Página visible para todos los roles:
- Tabla del catálogo (`GET /api/demo/invariants`) agrupada por agregado: ID, descripción, dueño, `code`, ícono si tiene refuerzo en BD.
- Botón **"Intentar violar"** por fila ⇒ `POST /probe` ⇒ muestra en verde "Rechazada por el dominio" con `code`, mensaje en español y HTTP status.
- Botón **"Probar todas"** que ejecuta las sondas en secuencia y muestra el resultado `n/n protegidas`.
- Además, en los formularios normales (cotizar, aprobar, reprogramar) **no** se bloquean valores inválidos en el front más allá de lo
  mínimo de formato: por ejemplo, el campo de aforo permite escribir 90 para que se vea el 422 del dominio en el video.

## Empaquetado Docker
- `web/Dockerfile` multi-stage: `node:22-alpine` (`npm ci`, `npm run build`) → `nginx:alpine` sirviendo `dist/`.
- `web/nginx.conf`: SPA fallback a `index.html` y `location /api/` con `proxy_pass http://api:8080`.
- El front usa rutas relativas `/api` (en dev, `server.proxy` de Vite apunta a `http://localhost:5080`).
- Agregar el servicio `web` a `docker-compose.yml` en el puerto 3000, `depends_on: api` healthy.

## Calidad
- Componentes accesibles (labels, foco). Responsive básico (el video será en escritorio).
- Sin lógica de negocio duplicada en el front: el precio y las validaciones vienen de la API (el front solo valida forma).
- `npm run lint` y `npm run build` sin errores.

## Criterios de aceptación
- [ ] Con la API y los seeds arriba, el calendario muestra E1 (provisional), E2 (provisional), E3 (definitivo) con buffers.
- [ ] Guion completo desde la UI: Cliente Ana cotiza y acepta → Dirección aprueba 30 % → Panel pasarela paga en dos partes →
      el evento pasa a Confirmado y aparece credencial + orden de catering → adelantar reloj → Operaciones inicia y finaliza → ledger muestra liquidación.
- [ ] Rama A, B y C demostrables desde la UI (TechSol tiene E5 con 600 Bs de saldo para reprogramar).
- [ ] Cotización vencida al adelantar el reloj 4 días muestra estado "Vencida" y no permite aceptar.
- [ ] Errores de regla (p. ej. aforo 90) se muestran con el mensaje en español de la API.
- [ ] El Laboratorio de invariantes muestra todas las observables como protegidas con "Probar todas".
- [ ] Todo lo anterior funciona en `http://localhost:3000` tras `docker compose up --build`, sin correr nada fuera de Docker.
