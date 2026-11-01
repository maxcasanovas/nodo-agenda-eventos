# Spec 07 — Cierre, verificación end-to-end y guion de demo

## Objetivo
Dejar el prototipo listo para la evaluación y el video: documentación, guion reproducible y verificación final.

## Entregables
1. **README.md** completo (en español):
   - Qué es, mapa de módulos y context map (diagrama Mermaid con patrones U/D, ACL, Conformist, OHS/PL, Published Language).
   - Requisitos (SDK .NET 10, Node LTS, Docker), cómo levantar todo en 4 comandos, URLs (API, Scalar, front, pgAdmin).
   - Decisiones y desviaciones respecto al documento (copiar de `business-rules.md` las marcadas **[Decisión]** con su justificación).
   - Trazabilidad: tabla documento → código (agregado/VO/servicio del documento ↔ clase en el repo).
2. **`docs/demo-script.md`**: guion paso a paso para el video (≈ 8–10 min) con lo que se hace en pantalla y qué concepto DDD se ilustra en cada paso:
   1. Context map y estructura del repo (30 s).
   2. Calendario con datos semilla.
   3. Fase I: cotización con tarifa degresiva y descuento de miembro; vencimiento adelantando el reloj.
   4. Fase II: bandeja de Dirección, aprobación con % de depósito.
   5. Fase III: pago parcial + total por la pasarela (ACL), confirmación en firme, credencial y catering, log de integration events (Outbox).
   6. Fase IV: iniciar/finalizar, liquidación en Facturación.
   7. Fase V: rechazo, cancelación con saldo a favor, reprogramación usando E5.
   8. **Invariantes (bloque más largo, ~2–3 min)**: Laboratorio de invariantes con "Probar todas"; luego 3–4 violaciones desde los
      formularios reales (aforo 81, 9 horas, solapamiento solo por buffer, depósito 60 %), una en Scalar mostrando el 422 con `code`,
      y un `INSERT` en pgAdmin rechazado por el `CHECK` de BD (defensa en profundidad).
   9. Código: mostrar un agregado (`HallEvent.ConfirmFirmly`) y un VO (`Attendance`) explicando el modelo *always-valid*.
   10. Tests: `dotnet test` con los tests de invariantes (válido / límite / inválido) y de arquitectura.
3. **`docs/architecture.md`**: diagrama de capas por módulo, flujo Outbox/Inbox, secuencia del caso de uso central
   "Confirmar evento en firme" (Mermaid `sequenceDiagram`: Pasarela → ACL → AccreditDeposit → HallEvent → Outbox → HallSchedule/Acceso/Facturación/Notificaciones).

4. **`docs/invariants.md`**: matriz de trazabilidad `INV-xx` → regla del documento → clase dueña → `code` → tests → restricción BD → sonda.

## Verificación final
- [ ] Clonado limpio + **solo** `docker compose up --build` deja la app completa en `http://localhost:3000` (con seeds) sin pasos manuales.
- [ ] `docker compose down -v && docker compose up --build` reproduce el estado inicial de la demo.
- [ ] `dotnet build` sin warnings, `dotnet test` en verde, `npm run lint && npm run build` en verde.
- [ ] Recorrer `demo-script.md` completo sin errores en consola del navegador ni excepciones no controladas en la API.
- [ ] Revisión de reglas: ningún `DateTime.Now`, ningún `double` en montos, ningún setter público en agregados, ningún módulo referenciando internals de otro.
- [ ] `PROGRESS.md` con estado final y deuda conocida.

## Fuera de alcance
Despliegue en la nube, autenticación, CI/CD.
