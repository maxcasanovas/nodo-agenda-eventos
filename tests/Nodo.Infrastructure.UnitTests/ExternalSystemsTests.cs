using Microsoft.Extensions.DependencyInjection;
using Nodo.BuildingBlocks.Application;
using Nodo.EventLifecycle.Contracts;
using Nodo.External.Simulated;
using Nodo.External.Simulated.Downstream;
using Nodo.External.Simulated.Handlers;
using Nodo.External.Simulated.Memberships;
using Nodo.External.Simulated.Payments;
using Nodo.HallSchedule.Contracts;
using Nodo.Quotes.Contracts;

namespace Nodo.Infrastructure.UnitTests;

/// <summary>The simulated downstream systems: what each one does with the integration events it receives.</summary>
public class ExternalSystemsTests
{
    private static readonly Guid EventId = Guid.Parse("44444444-0000-0000-0000-000000000003");
    private static readonly Guid Ana = Guid.Parse("22222222-0000-0000-0000-000000000001");
    private static readonly Guid Fundacion = Guid.Parse("22222222-0000-0000-0000-000000000004");
    private static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private sealed class World
    {
        private readonly ServiceProvider _provider;

        public World()
        {
            var services = new ServiceCollection();
            services.AddExternalSimulated(
                typeof(QuoteIssuedIntegrationEvent).Assembly,
                typeof(EventRequestCreatedIntegrationEvent).Assembly,
                typeof(SlotReleasedIntegrationEvent).Assembly);
            _provider = services.BuildServiceProvider();
        }

        public IServiceProvider Provider => _provider;

        public T Get<T>() where T : notnull => _provider.GetRequiredService<T>();

        public Task Deliver<T>(T message) where T : IIntegrationEvent
        {
            // Like the bus: a new scope, every handler registered for the event.
            using var scope = _provider.CreateScope();
            return Task.WhenAll(scope.ServiceProvider
                .GetServices<IIntegrationEventHandler<T>>()
                .Select(h => h.HandleAsync(message)));
        }
    }

    private static HallEventFirmlyConfirmedIntegrationEvent Confirmed(
        Guid? id = null, DateOnly? date = null, int startHour = 10, int endHour = 14, int attendance = 35,
        params EventExtraServiceDto[] extras) => new(
        id ?? Guid.NewGuid(), Now, EventId, Guid.Parse("11111111-1111-1111-1111-111111111111"),
        date ?? new DateOnly(2026, 11, 20), new TimeOnly(startHour, 0), new TimeOnly(endHour, 0), attendance, Ana,
        3085m, 617m, extras);

    // ---- Control de Acceso (ACL) ----

    [Fact]
    public void AccessCredential_WindowStartsOneHourBeforeTheEventAndEndsWithIt_InUtc()
    {
        // La Paz is UTC-4 with no daylight saving: 10:00-14:00 local = 14:00-18:00 UTC, valid from 13:00 UTC (setup hour).
        var credential = AccessCredentialTranslator.FromConfirmed(Confirmed(), "123456");

        credential.ValidFromUtc.ShouldBe(new DateTime(2026, 11, 20, 13, 0, 0, DateTimeKind.Utc));
        credential.ValidUntilUtc.ShouldBe(new DateTime(2026, 11, 20, 18, 0, 0, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(35)]
    [InlineData(80)]
    public void AccessCredential_CapacityEqualsTheEventAttendance(int attendance) =>
        AccessCredentialTranslator.FromConfirmed(Confirmed(attendance: attendance), "123456").Capacity.ShouldBe(attendance);

    [Fact]
    public void AccessCredential_CarriesThePinAndAQrTextThatIdentifiesTheEvent()
    {
        var credential = AccessCredentialTranslator.FromConfirmed(Confirmed(), "482916");

        credential.Pin.ShouldBe("482916");
        credential.QrText.ShouldContain("482916");
        credential.QrText.ShouldContain(EventId.ToString("N"));
        credential.Status.ShouldBe("ACTIVE");
        credential.RevokedAtUtc.ShouldBeNull();
    }

    [Fact]
    public async Task Confirming_CreatesACredentialWithASixDigitPin()
    {
        var world = new World();

        await world.Deliver(Confirmed());

        var credential = world.Get<AccessControlContext>().Credentials.Items.Single();
        credential.Pin.Length.ShouldBe(6);
        credential.Pin.ShouldAllBe(c => char.IsDigit(c));
        credential.EventRef.ShouldBe(EventId.ToString());
    }

    [Fact]
    public async Task Confirming_TheSameMessageTwice_CreatesOneCredential()
    {
        var world = new World();
        var message = Confirmed();

        await world.Deliver(message);
        await world.Deliver(message);

        world.Get<AccessControlContext>().Credentials.Items.Count.ShouldBe(1);
        world.Get<BillingContext>().Ledger.Items.Count.ShouldBe(1);
        world.Get<NotificationsContext>().Feed.Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Reconfirming_ARescheduledEvent_RevokesThePreviousCredentialAndIssuesANewOne()
    {
        var world = new World();
        await world.Deliver(Confirmed(date: new DateOnly(2026, 11, 20)));

        await world.Deliver(Confirmed(date: new DateOnly(2026, 12, 5)));

        var credentials = world.Get<AccessControlContext>().Credentials.Items;
        credentials.Count.ShouldBe(2);
        credentials.Count(c => c.Status == "ACTIVE").ShouldBe(1);
        credentials.Single(c => c.Status == "REVOKED").RevokedAtUtc.ShouldNotBeNull();
        credentials.Single(c => c.Status == "ACTIVE").ValidFromUtc.Date.ShouldBe(new DateTime(2026, 12, 5));
    }

    [Fact]
    public async Task Cancelling_RevokesTheActiveCredential()
    {
        var world = new World();
        await world.Deliver(Confirmed());

        await world.Deliver(new HallEventCancelledWithCreditIntegrationEvent(
            Guid.NewGuid(), Now.AddHours(1), EventId, Guid.NewGuid(), new DateOnly(2026, 11, 20), Ana, 617m));

        var credential = world.Get<AccessControlContext>().Credentials.Items.Single();
        credential.Status.ShouldBe("REVOKED");
        credential.RevokedAtUtc.ShouldBe(Now.AddHours(1));
    }

    // ---- Facturación y Cobros ----

    [Fact]
    public async Task Confirming_RegistersTheDepositInTheLedger()
    {
        var world = new World();

        await world.Deliver(Confirmed());

        var entry = world.Get<BillingContext>().Ledger.Items.Single();
        entry.Kind.ShouldBe("DepositoRegistrado");
        entry.Amount.ShouldBe(617m);
        entry.Account.ShouldBe(Ana.ToString());
        entry.Reference.ShouldBe(EventId.ToString());
    }

    [Fact]
    public async Task Cancelling_RegistersTheCreditAsABalanceNotARefund()
    {
        var world = new World();

        await world.Deliver(new HallEventCancelledWithCreditIntegrationEvent(
            Guid.NewGuid(), Now, EventId, Guid.NewGuid(), new DateOnly(2026, 11, 20), Ana, 617m));

        var entry = world.Get<BillingContext>().Ledger.Items.Single();
        entry.Kind.ShouldBe("SaldoAFavorRegistrado");
        entry.Amount.ShouldBe(617m);
        entry.Note.ShouldContain("no hay devolución de dinero");
    }

    [Fact]
    public async Task Cancelling_WithNothingPaid_RegistersNothingInTheLedger()
    {
        var world = new World();

        await world.Deliver(new HallEventCancelledWithCreditIntegrationEvent(
            Guid.NewGuid(), Now, EventId, Guid.NewGuid(), new DateOnly(2026, 11, 20), Ana, 0m));

        world.Get<BillingContext>().Ledger.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Completing_RegistersTheFinalSettlementForTheRemainingBalance()
    {
        var world = new World();

        await world.Deliver(new HallEventCompletedIntegrationEvent(Guid.NewGuid(), Now, EventId, Ana, 2520m, 1260m, 1260m));

        var entry = world.Get<BillingContext>().Ledger.Items.Single();
        entry.Kind.ShouldBe("LiquidacionFinalCompletada");
        entry.Amount.ShouldBe(1260m);
    }

    // ---- Proveedor de Catering ----

    [Fact]
    public async Task ExtraServices_WithCatering_PlaceAnOrderWithOnlyTheCateringLines()
    {
        var world = new World();
        var message = new ExtraServicesAssignedIntegrationEvent(Guid.NewGuid(), Now, EventId,
        [
            new EventExtraServiceDto("OwnEquipment", "PROJECTOR", "Proyector", 1, 150m),
            new EventExtraServiceDto("ExternalCatering", "COFFEE_BREAK", "Coffee break", 35, 35m)
        ]);

        await world.Deliver(message);

        var order = world.Get<CateringContext>().Orders.Items.Single();
        order.EventRef.ShouldBe(EventId.ToString());
        var line = order.Lines.Single();
        (line.Sku, line.Headcount, line.PricePerHead).ShouldBe(("COFFEE_BREAK", 35, 35m));
    }

    [Fact]
    public async Task ExtraServices_WithoutCatering_PlaceNoOrder()
    {
        var world = new World();

        await world.Deliver(new ExtraServicesAssignedIntegrationEvent(Guid.NewGuid(), Now, EventId,
            [new EventExtraServiceDto("OwnEquipment", "PROJECTOR", "Proyector", 1, 150m)]));

        world.Get<CateringContext>().Orders.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExtraServices_SameMessageTwice_PlacesOneOrder()
    {
        var world = new World();
        var message = new ExtraServicesAssignedIntegrationEvent(Guid.NewGuid(), Now, EventId,
            [new EventExtraServiceDto("ExternalCatering", "LUNCH", "Almuerzo ejecutivo", 70, 90m)]);

        await world.Deliver(message);
        await world.Deliver(message);

        world.Get<CateringContext>().Orders.Items.Count.ShouldBe(1);
    }

    // ---- Pasarela de Pagos ----

    [Fact]
    public async Task Approval_CreatesAPaymentLinkForTheRequiredDepositReferencingTheEvent()
    {
        var world = new World();

        await world.Deliver(new HallEventApprovedByManagementIntegrationEvent(
            Guid.NewGuid(), Now, EventId, "Lucía Paz", 20, 617m, Now.AddHours(48)));

        var link = world.Get<PaymentGatewayContext>().Links.Single();
        link.MerchantRef.ShouldBe(EventId.ToString()); // what comes back as merchant_ref in the webhook
        link.AmountCents.ShouldBe(61700);
        link.CurrencyCode.ShouldBe("BOB");
        link.Status.ShouldBe("PENDING");
    }

    [Fact]
    public async Task Reschedule_NotCoveredByCredit_CreatesALinkForTheDifferenceOnly()
    {
        var world = new World();

        await world.Deliver(new HallEventRescheduledWithCreditIntegrationEvent(
            Guid.NewGuid(), Now, EventId, Guid.NewGuid(), Ana, new DateOnly(2026, 12, 1), new TimeOnly(10, 0),
            new TimeOnly(14, 0), 400m, "ApprovedByManagement", 200m, Now.AddHours(48)));

        world.Get<PaymentGatewayContext>().Links.Single().AmountCents.ShouldBe(20000);
    }

    [Fact]
    public async Task Reschedule_FullyCoveredByCredit_CreatesNoLink()
    {
        var world = new World();

        await world.Deliver(new HallEventRescheduledWithCreditIntegrationEvent(
            Guid.NewGuid(), Now, EventId, Guid.NewGuid(), Ana, new DateOnly(2026, 12, 1), new TimeOnly(10, 0),
            new TimeOnly(14, 0), 600m, "Confirmed", 0m, null));

        world.Get<PaymentGatewayContext>().Links.ShouldBeEmpty();
    }

    [Fact]
    public void PaymentLink_PartialThenFullPaymentThenDecline_TracksItsStatus()
    {
        var gateway = new PaymentGatewayContext();
        var link = gateway.CreateLink(EventId.ToString(), 61700, "Depósito", Now);

        gateway.RecordAttempt(link, "T1", 20000, approved: true, Now);
        link.Status.ShouldBe("PARTIALLY_PAID");
        gateway.RecordAttempt(link, "T2", 41700, approved: true, Now);
        link.Status.ShouldBe("PAID");
        link.PaidCents.ShouldBe(61700);
        gateway.RecordAttempt(link, "T3", 100, approved: false, Now);
        link.Status.ShouldBe("PAID"); // a declined attempt never undoes money already received
        link.Attempts.Select(a => a.Status).ShouldBe(["APPROVED", "APPROVED", "DECLINED"]);
    }

    [Fact]
    public void PaymentLink_DeclinedFirstAttempt_IsRecordedAndMarksTheLinkDeclined()
    {
        var gateway = new PaymentGatewayContext();
        var link = gateway.CreateLink(EventId.ToString(), 61700, "Depósito", Now);

        gateway.RecordAttempt(link, "T1", 61700, approved: false, Now);

        link.Status.ShouldBe("DECLINED");
        link.PaidCents.ShouldBe(0);
        link.Attempts.Single().Status.ShouldBe("DECLINED");
    }

    // ---- Notificaciones ----

    [Fact]
    public async Task NewRequest_NotifiesManagementNamingTheCustomerAndTheDay()
    {
        var world = new World();

        await world.Deliver(new EventRequestCreatedIntegrationEvent(
            Guid.NewGuid(), Now, EventId, Guid.NewGuid(), new DateOnly(2026, 11, 12), new TimeOnly(14, 0),
            new TimeOnly(18, 0), 60, Fundacion));

        var notification = world.Get<NotificationsContext>().Feed.Items.Single();
        notification.To.ShouldBe("Dirección");
        notification.Body.ShouldContain("Dirección: nueva solicitud de Fundación Crecer para el 12/11");
    }

    [Fact]
    public async Task LaterNotifications_NameTheCustomerLearnedFromTheRequest()
    {
        var world = new World();
        await world.Deliver(new EventRequestCreatedIntegrationEvent(
            Guid.NewGuid(), Now, EventId, Guid.NewGuid(), new DateOnly(2026, 11, 12), new TimeOnly(14, 0),
            new TimeOnly(18, 0), 60, Fundacion));

        await world.Deliver(new HallEventApprovedByManagementIntegrationEvent(
            Guid.NewGuid(), Now, EventId, "Lucía Paz", 30, 540m, Now.AddHours(48)));

        world.Get<NotificationsContext>().Feed.Items.Last().To.ShouldBe("Cliente: Fundación Crecer");
    }

    [Fact]
    public void NotificationHandler_IsRegisteredForEveryIntegrationEventOfTheThreeModules()
    {
        var world = new World();
        var contracts = new[]
            {
                typeof(QuoteIssuedIntegrationEvent).Assembly,
                typeof(EventRequestCreatedIntegrationEvent).Assembly,
                typeof(SlotReleasedIntegrationEvent).Assembly
            }
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsAssignableTo(typeof(IIntegrationEvent)))
            .ToList();

        contracts.Count.ShouldBe(19);
        foreach (var contract in contracts)
        {
            var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(contract);
            using var scope = world.Provider.CreateScope();
            scope.ServiceProvider.GetServices(handlerType).ShouldNotBeEmpty(contract.Name);
        }
    }

    [Fact]
    public void NotificationFormatter_WritesAReadableSpanishMessageForEveryContract()
    {
        var formatter = new NotificationFormatter(new MembershipsContext(), new EventDirectory());
        var contracts = new[]
            {
                typeof(QuoteIssuedIntegrationEvent).Assembly,
                typeof(EventRequestCreatedIntegrationEvent).Assembly,
                typeof(SlotReleasedIntegrationEvent).Assembly
            }
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsAssignableTo(typeof(IIntegrationEvent)));

        foreach (var contract in contracts)
        {
            var message = formatter.Format((IIntegrationEvent)SampleFactory.Create(contract));

            message.Subject.ShouldNotBeNullOrWhiteSpace(contract.Name);
            message.Body.ShouldNotBeNullOrWhiteSpace(contract.Name);
            message.Subject.ShouldNotBe(contract.Name, "the generic fallback means the event has no dedicated message");
            message.To.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task CancelledEvent_NotificationExplainsThereIsNoRefund()
    {
        var world = new World();

        await world.Deliver(new HallEventCancelledWithCreditIntegrationEvent(
            Guid.NewGuid(), Now, EventId, Guid.NewGuid(), new DateOnly(2026, 11, 20), Ana, 617m));

        var body = world.Get<NotificationsContext>().Feed.Items.Single(n => n.Subject == "Evento cancelado").Body;
        body.ShouldContain("saldo a favor");
        body.ShouldContain("no hay devolución en dinero");
        body.ShouldContain("Bs 617.00");
    }
}
