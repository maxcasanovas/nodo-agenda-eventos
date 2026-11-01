using Nodo.EventLifecycle.Application;
using Nodo.External.Simulated.Memberships;
using Nodo.HallSchedule.Contracts;
using Nodo.Quotes.Application;
using static Nodo.Api.Invariants.ProbeContext;

namespace Nodo.Api.Invariants;

/// <summary>
/// One predefined attempt to violate each invariant, made through the real use case. Each probe builds its own
/// ephemeral data (so it does not depend on the demo's seed data) and runs inside a transaction that is rolled back.
/// </summary>
internal static class InvariantProbes
{
    private static readonly Dictionary<string, Func<ProbeContext, Task<ProbeOutcome>>> Probes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["INV-01"] = c => c.AttemptAsync(
            "Emitir una cotización de 9 horas (09:00-18:00)",
            () => c.IssueAsync(Customer, c.FarDay(), 9, 18)),

        ["INV-02"] = async c =>
        {
            var id = await c.RequestedEventAsync(Customer, c.FarDay());
            return await c.AttemptAsync("Aprobar la solicitud con un depósito del 19 %", () => c.ApproveAsync(id, 19));
        },

        ["INV-03"] = async c =>
        {
            var id = await c.RequestedEventAsync(Customer, c.FarDay());
            await c.ApproveAsync(id, 30);
            var result = await c.PayAsync(id, 100m, "TX-SONDA-INV03");
            return ByDesign(
                "Intentar confirmar con el depósito incompleto: pago parcial de Bs 100 sobre Bs 540",
                $"Ningún endpoint confirma un evento: solo el pago que cubre el depósito lo hace. Con el pago parcial el evento sigue en " +
                $"{result.Status} (acreditado Bs {result.AccreditedTotal:0.00} de Bs 540.00). Si el dominio se invocara sin las dos condiciones lanzaría " +
                "EVENT.CONFIRMATION_PRECONDITIONS_NOT_MET (lo cubren los tests unitarios de HallEvent).");
        },

        ["INV-04"] = c => c.AttemptAsync(
            "Emitir una cotización para 81 personas",
            () => c.IssueAsync(Customer, c.FarDay(), attendance: 81)),

        ["INV-05"] = async c =>
        {
            var id = await c.RequestedEventAsync(Customer, c.FarDay());
            await c.Mediator.Send(new RejectEventRequestCommand(id, "Rechazada por la sonda de invariantes.", "Lucía Paz"));
            return await c.AttemptAsync("Cancelar un evento ya rechazado", () => c.CancelAsync(id));
        },

        ["INV-06"] = async c =>
        {
            var id = await c.RequestedEventAsync(Customer, c.FarDay());
            await c.ApproveAsync(id);
            return await c.AttemptAsync("Aprobar dos veces la misma solicitud", () => c.ApproveAsync(id));
        },

        ["INV-07"] = async c =>
        {
            var id = await c.RequestedEventAsync(Customer, c.FarDay());
            c.ClockOffset.Extra = TimeSpan.FromDays(1); // the next day, for this probe only
            return await c.AttemptAsync("Aprobar al día siguiente de la solicitud", () => c.ApproveAsync(id));
        },

        ["INV-08"] = async c =>
        {
            var id = await c.RequestedEventAsync(Customer, c.FarDay());
            return await c.AttemptAsync(
                "Rechazar con el motivo \"corto\" (menos de 10 caracteres)",
                () => c.Mediator.Send(new RejectEventRequestCommand(id, "corto", "Lucía Paz")));
        },

        ["INV-09"] = async c =>
        {
            var id = await c.RequestedEventAsync(Customer, c.FarDay());
            return await c.AttemptAsync(
                "Acreditar un pago en una solicitud que Dirección aún no aprobó",
                () => c.PayAsync(id, 100m, "TX-SONDA-INV09"));
        },

        ["INV-10"] = async c =>
        {
            var id = await c.RequestedEventAsync(Customer, c.FarDay());
            await c.ApproveAsync(id);
            return await c.AttemptAsync(
                "Acreditar un pago en dólares (USD) sobre un depósito en bolivianos",
                () => c.PayAsync(id, 100m, "TX-SONDA-INV10", "USD"));
        },

        ["INV-11"] = async c =>
        {
            var id = await c.RequestedEventAsync(Customer, c.FarDay());
            await c.ApproveAsync(id);
            await c.PayAsync(id, 540m, "TX-SONDA-INV11"); // covers the 30 % deposit: Confirmed
            return await c.AttemptAsync(
                "Iniciar el evento más de 1 hora antes de su hora de inicio",
                () => c.Mediator.Send(new StartHallEventCommand(id, "Host de turno")));
        },

        ["INV-12"] = async c =>
        {
            var id = await c.RequestedEventAsync(Customer, c.FarDay());
            return await c.AttemptAsync(
                "Reprogramar un evento que no está cancelado ni tiene saldo a favor",
                () => c.Mediator.Send(new RescheduleHallEventCommand(id, c.FarDay(20), new TimeOnly(10, 0), Actor)));
        },

        ["INV-13"] = c => c.AttemptAsync(
            "Cotizar 2 proyectores cuando el inventario es de 1",
            () => c.IssueAsync(Customer, c.FarDay(), extras: [new ExtraServiceRequest("PROJECTOR", 2)])),

        ["INV-14"] = async c =>
        {
            var quote = await c.IssueAsync(Customer, c.FarDay());
            c.ClockOffset.Extra = TimeSpan.FromDays(5); // 120 h later: past the 96 h of validity
            return await c.AttemptAsync("Aceptar una cotización 120 horas después de emitida", () => c.AcceptAsync(quote.QuoteId));
        },

        ["INV-15"] = _ => Task.FromResult(ByDesign(
            "Cambiar el precio de una cotización ya aceptada",
            "No existe ninguna operación para hacerlo: Quote no tiene métodos que modifiquen el precio ni los servicios, " +
            "y la API no ofrece un endpoint para ello. La invariante se mantiene por diseño.")),

        ["INV-16"] = c => c.AttemptAsync(
            "Cotizar un evento que empieza a las 07:00 (sin hora para el montaje)",
            () => c.IssueAsync(Customer, c.FarDay(), 7, 11)),

        ["INV-17"] = async c =>
        {
            // The upstream context is trusted as published (Conformist). Simulate it reporting 15 %: the domain must still say no.
            var corrupt = Guid.Parse("99999999-0000-0000-0000-0000000000f1");
            using var _ = c.Service<MembershipsContext>().AddTemporarily(new MemberRecord(
                corrupt.ToString(), "Miembro de prueba (sonda INV-17)", "Freelancer", "Plan inválido", "ACTIVE", 15));
            return await c.AttemptAsync(
                "Cotizar para un miembro cuya membresía publica un descuento del 15 %",
                () => c.IssueAsync(corrupt, c.FarDay()));
        },

        ["INV-18"] = async c =>
        {
            await c.RequestedEventAsync(Customer, c.FarDay(), 10, 14); // this request holds the 10-14 slot
            var second = await c.IssueAsync(Customer, c.FarDay(), 13, 17); // free to quote, but not to accept: 13-17 touches its buffer
            return await c.AttemptAsync("Aceptar una cotización cuya franja choca con otra reserva (buffers incluidos)", () => c.AcceptAsync(second.QuoteId));
        },

        ["INV-19"] = async c =>
        {
            var api = c.Service<IHallScheduleModuleApi>();
            var eventId = Guid.NewGuid();
            var day = c.FarDay(2);
            (await api.PlaceProvisionalBlockAsync(eventId, c.HallId, day, new TimeOnly(9, 0), new TimeOnly(11, 0), [])).Success.ShouldHold();
            return await c.AttemptScheduleAsync(
                "Colocar un segundo bloqueo activo para el mismo evento",
                () => api.PlaceProvisionalBlockAsync(eventId, c.HallId, day, new TimeOnly(15, 0), new TimeOnly(17, 0), []));
        },

        ["INV-20"] = c =>
        {
            var api = c.Service<IHallScheduleModuleApi>();
            return c.AttemptScheduleAsync(
                "Reservar 2 proyectores en un bloqueo cuando el inventario es de 1",
                () => api.PlaceProvisionalBlockAsync(
                    Guid.NewGuid(), c.HallId, c.FarDay(3), new TimeOnly(10, 0), new TimeOnly(12, 0), [new EquipmentRequest("PROJECTOR", 2)]));
        },

        ["INV-21"] = async c =>
        {
            // Two cancelled events of one customer, each with 250 Bs of credit. Rescheduling the first uses up both credits
            // (it asks for the whole 540 Bs deposit); the second one then finds nothing left to apply.
            var first = await CancelledWithPartialPaymentAsync(c, c.FarDay(), "A");
            var second = await CancelledWithPartialPaymentAsync(c, c.FarDay(3), "B");
            await c.Mediator.Send(new RescheduleHallEventCommand(first, c.FarDay(10), new TimeOnly(10, 0), Actor));
            return await c.AttemptAsync(
                "Reprogramar un segundo evento cuando el saldo a favor del cliente ya se consumió",
                () => c.Mediator.Send(new RescheduleHallEventCommand(second, c.FarDay(13), new TimeOnly(10, 0), Actor)));
        },

        ["INV-22"] = _ => Task.FromResult(ByDesign(
            "Retirar o reembolsar en dinero el saldo a favor",
            "No existe ninguna operación para hacerlo: CustomerCredit solo se abre, se acredita y se aplica a una reprogramación. " +
            "Cancelar convierte lo pagado íntegro en saldo a favor (nunca en devolución). La invariante se mantiene por diseño."))
    };

    public static bool Has(string id) => Probes.ContainsKey(id);

    public static Task<ProbeOutcome> RunAsync(string id, ProbeContext context) => Probes[id](context);

    private static async Task<Guid> CancelledWithPartialPaymentAsync(ProbeContext c, DateOnly day, string tag)
    {
        var id = await c.RequestedEventAsync(Customer, day);
        await c.ApproveAsync(id);
        await c.PayAsync(id, 250m, $"TX-SONDA-INV21-{tag}");
        await c.CancelAsync(id); // 250 Bs of credit, never refunded
        return id;
    }

    /// <summary>A fixture step that must succeed; if it does not, the probe is broken, not the invariant.</summary>
    private static void ShouldHold(this bool succeeded)
    {
        if (!succeeded)
            throw new InvalidOperationException("A fixture step of an invariant probe failed unexpectedly.");
    }
}
