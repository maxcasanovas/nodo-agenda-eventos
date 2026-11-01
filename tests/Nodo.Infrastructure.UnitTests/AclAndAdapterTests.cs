using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Nodo.EventLifecycle.Application;
using Nodo.EventLifecycle.Infrastructure;
using Nodo.External.Simulated.Memberships;
using Nodo.External.Simulated.Payments;
using Nodo.External.Simulated.Spaces;

namespace Nodo.Infrastructure.UnitTests;

/// <summary>The payment gateway's anti-corruption layer: the provider's payload in, our command out.</summary>
public class PaymentGatewayAclTests
{
    private static readonly Guid EventId = Guid.Parse("44444444-0000-0000-0000-000000000003");

    private static (PaymentGatewayAclAdapter Acl, IMediator Mediator) Build()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<AccreditDepositCommand>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var command = call.Arg<AccreditDepositCommand>();
            return new AccreditDepositResultDto(command.EventId, "ApprovedByManagement", command.Amount, 100m, false, 0m);
        });
        return (new PaymentGatewayAclAdapter(mediator, NullLogger<PaymentGatewayAclAdapter>.Instance), mediator);
    }

    private static GatewayWebhookPayload Payload(
        string status = "APPROVED", long cents = 61700, string? merchantRef = null, string txn = "TXN-1", string currency = "BOB") => new()
    {
        TxnId = txn,
        AmountCents = cents,
        CurrencyCode = currency,
        Status = status,
        Metadata = new GatewayMetadata { MerchantRef = merchantRef ?? EventId.ToString() }
    };

    [Fact]
    public async Task Receive_ApprovedPayment_SendsAccreditDepositWithCentsConvertedToBolivianos()
    {
        var (acl, mediator) = Build();

        await acl.ReceiveAsync(Payload(cents: 61700));

        await mediator.Received(1).Send(
            Arg.Is<AccreditDepositCommand>(c =>
                c.EventId == EventId && c.Amount == 617.00m && c.Currency == "BOB" && c.TransactionId == "TXN-1"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(1, "0.01")]
    [InlineData(100, "1.00")]
    [InlineData(25050, "250.50")]
    [InlineData(99999999, "999999.99")]
    public async Task Receive_AmountInCents_IsExactInBolivianos(long cents, string expected)
    {
        var (acl, mediator) = Build();

        await acl.ReceiveAsync(Payload(cents: cents));

        await mediator.Received(1).Send(
            Arg.Is<AccreditDepositCommand>(c => c.Amount == decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receive_DeclinedPayment_IsAcknowledgedButNeverReachesTheDomain()
    {
        var (acl, mediator) = Build();

        var ack = await acl.ReceiveAsync(Payload(status: "DECLINED"));

        ack.Accredited.ShouldBeFalse();
        ack.Outcome.ShouldBe("DECLINED_NOT_ACCREDITED");
        ack.EventId.ShouldBe(EventId);
        await mediator.DidNotReceiveWithAnyArgs().Send(default(AccreditDepositCommand)!, default);
    }

    [Theory]
    [InlineData("approved")]
    [InlineData("Approved")]
    public async Task Receive_StatusIsCaseInsensitive(string status)
    {
        var (acl, mediator) = Build();

        await acl.ReceiveAsync(Payload(status: status));

        await mediator.Received(1).Send(Arg.Any<AccreditDepositCommand>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Receive_InvalidMerchantRef_IsAShapeErrorAndSendsNothing(string merchantRef)
    {
        var (acl, mediator) = Build();

        var ex = await Should.ThrowAsync<ValidationException>(() => acl.ReceiveAsync(Payload(merchantRef: merchantRef)));

        ex.Errors.ShouldContain(e => e.PropertyName == "metadata.merchant_ref");
        await mediator.DidNotReceiveWithAnyArgs().Send(default(AccreditDepositCommand)!, default);
    }

    [Fact]
    public async Task Receive_MissingMetadata_IsAShapeError()
    {
        var (acl, _) = Build();
        var payload = Payload();
        payload.Metadata = null;

        var ex = await Should.ThrowAsync<ValidationException>(() => acl.ReceiveAsync(payload));

        ex.Errors.ShouldContain(e => e.PropertyName == "metadata.merchant_ref");
    }

    [Fact]
    public async Task Receive_UnknownStatus_IsAShapeError()
    {
        var (acl, mediator) = Build();

        var ex = await Should.ThrowAsync<ValidationException>(() => acl.ReceiveAsync(Payload(status: "PENDING")));

        ex.Errors.ShouldContain(e => e.PropertyName == "status");
        await mediator.DidNotReceiveWithAnyArgs().Send(default(AccreditDepositCommand)!, default);
    }

    [Fact]
    public async Task Receive_SeveralProblems_ReportsAllOfThemTogether()
    {
        var (acl, _) = Build();
        var payload = Payload(status: "WAT", merchantRef: "x", txn: "");

        var ex = await Should.ThrowAsync<ValidationException>(() => acl.ReceiveAsync(payload));

        ex.Errors.Select(e => e.PropertyName).ShouldBe(["txn_id", "status", "metadata.merchant_ref"], ignoreOrder: true);
    }

    [Fact]
    public async Task Receive_SameTxnIdTwice_BothReachTheDomainWhichAccreditsItOnce()
    {
        // The ACL does not keep state: idempotency is the aggregate's job (a transaction id is accredited once, INV-09).
        var (acl, mediator) = Build();

        await acl.ReceiveAsync(Payload(txn: "TXN-REPEATED"));
        await acl.ReceiveAsync(Payload(txn: "TXN-REPEATED"));

        await mediator.Received(2).Send(
            Arg.Is<AccreditDepositCommand>(c => c.TransactionId == "TXN-REPEATED"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receive_OtherCurrency_IsPassedAsIsSoTheDomainRefusesIt()
    {
        var (acl, mediator) = Build();

        await acl.ReceiveAsync(Payload(currency: "USD"));

        await mediator.Received(1).Send(Arg.Is<AccreditDepositCommand>(c => c.Currency == "USD"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receive_PaymentThatCoversTheDeposit_ReportsItInTheAck()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<AccreditDepositCommand>(), Arg.Any<CancellationToken>())
            .Returns(new AccreditDepositResultDto(EventId, "Confirmed", 617m, 0m, true, 50m));
        var acl = new PaymentGatewayAclAdapter(mediator, NullLogger<PaymentGatewayAclAdapter>.Instance);

        var ack = await acl.ReceiveAsync(Payload());

        ack.Accredited.ShouldBeTrue();
        ack.Outcome.ShouldBe("ACCREDITED_AND_COVERED");
        ack.EventStatus.ShouldBe("Confirmed");
        ack.ExcessGrantedAsCredit.ShouldBe(50m);
    }
}

/// <summary>The adapters that translate the external contexts' own models into what each module asks for.</summary>
public class ExternalAdapterTests
{
    private const string Ana = "22222222-0000-0000-0000-000000000001";
    private const string Carlos = "22222222-0000-0000-0000-000000000003";
    private const string Fundacion = "22222222-0000-0000-0000-000000000004";

    private static Nodo.Quotes.Infrastructure.MembershipDirectoryAdapter Memberships(MembershipsContext? context = null) =>
        new(context ?? new MembershipsContext());

    [Fact]
    public async Task Membership_ActiveMember_AdoptsTheStatusAndTheDiscountAsPublished()
    {
        var membership = await Memberships().GetMembershipAsync(Guid.Parse(Ana));

        membership.IsActiveMember.ShouldBeTrue();
        membership.DiscountPercentage.ShouldBe(5);
    }

    [Fact]
    public async Task Membership_ExpiredPlan_IsNotAMemberAndHasNoDiscount()
    {
        var membership = await Memberships().GetMembershipAsync(Guid.Parse(Carlos));

        membership.IsActiveMember.ShouldBeFalse();
        membership.DiscountPercentage.ShouldBe(0);
    }

    [Fact]
    public async Task Membership_ExternalCustomerWithoutPlan_IsNotAMember()
    {
        var membership = await Memberships().GetMembershipAsync(Guid.Parse(Fundacion));

        membership.IsActiveMember.ShouldBeFalse();
        membership.DiscountPercentage.ShouldBe(0);
    }

    [Fact]
    public async Task Membership_UnknownCustomer_IsNotAMember()
    {
        var membership = await Memberships().GetMembershipAsync(Guid.NewGuid());

        membership.IsActiveMember.ShouldBeFalse();
        membership.DiscountPercentage.ShouldBe(0);
    }

    [Theory]
    [InlineData("ACTIVE", true)]
    [InlineData("EXPIRED", false)]
    [InlineData("NONE", false)]
    [InlineData("active", false)] // Conformist: it reads exactly "ACTIVE", it does not second-guess the provider
    public async Task Membership_OnlyTheExactActiveStatusCountsAsMember(string status, bool expected)
    {
        var context = new MembershipsContext();
        var id = Guid.NewGuid();
        using var _ = context.AddTemporarily(new MemberRecord(id.ToString(), "Prueba", "Freelancer", "X", status, 7));

        var membership = await Memberships(context).GetMembershipAsync(id);

        membership.IsActiveMember.ShouldBe(expected);
        membership.DiscountPercentage.ShouldBe(7); // passed on untouched, whatever the status
    }

    [Fact]
    public async Task Membership_ADiscountAboveTheLimit_IsNotCorrectedByTheAdapter()
    {
        // The adapter conforms; the domain is the one that refuses 15 % (INV-17).
        var context = new MembershipsContext();
        var id = Guid.NewGuid();
        using var _ = context.AddTemporarily(new MemberRecord(id.ToString(), "Prueba", "Freelancer", "X", "ACTIVE", 15));

        var membership = await Memberships(context).GetMembershipAsync(id);

        membership.DiscountPercentage.ShouldBe(15);
    }

    [Fact]
    public void Membership_TemporaryRecord_DisappearsWhenDisposed()
    {
        var context = new MembershipsContext();
        var before = context.Members.Count;

        var handle = context.AddTemporarily(new MemberRecord(Guid.NewGuid().ToString(), "Prueba", "Freelancer", "X", "ACTIVE", 15));
        context.Members.Count.ShouldBe(before + 1);
        handle.Dispose();

        context.Members.Count.ShouldBe(before);
    }

    [Fact]
    public async Task CustomerDirectory_ReturnsTheNameOrNull()
    {
        var directory = new Nodo.Quotes.Infrastructure.CustomerDirectoryAdapter(new MembershipsContext());

        (await directory.GetNameAsync(Guid.Parse(Ana))).ShouldBe("Ana Gutiérrez");
        (await directory.GetNameAsync(Guid.NewGuid())).ShouldBeNull();
    }

    // ---- Spaces ----

    [Fact]
    public async Task QuotesSpaces_TranslatesVenueAssetsAndMenuToTheModulesOwnTerms()
    {
        var adapter = new Nodo.Quotes.Infrastructure.SpacesCatalogAdapter(new SpacesContext());
        var hallId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var hall = await adapter.GetHallAsync(hallId);
        var equipment = await adapter.GetEquipmentAsync();
        var menu = await adapter.GetCateringMenuAsync();

        hall!.Name.ShouldBe("Salón Polivalente");
        hall.MaxAttendance.ShouldBe(80);
        equipment.Select(e => (e.Code, e.Stock, e.CostPerEvent)).ShouldBe(
            [("PROJECTOR", 1, 150m), ("TV", 2, 100m), ("SOUND", 1, 250m), ("LIGHTS", 1, 200m)], ignoreOrder: true);
        menu.Select(m => (m.Code, m.CostPerPerson)).ShouldBe(
            [("COFFEE_BREAK", 35m), ("SNACK", 25m), ("LUNCH", 90m), ("BUFFET", 120m)], ignoreOrder: true);
    }

    [Fact]
    public async Task QuotesSpaces_UnknownHall_IsNull()
    {
        var adapter = new Nodo.Quotes.Infrastructure.SpacesCatalogAdapter(new SpacesContext());

        (await adapter.GetHallAsync(Guid.NewGuid())).ShouldBeNull();
    }

    [Fact]
    public async Task EventLifecycleAndHallSchedule_SeeTheSameEquipmentStock()
    {
        var spaces = new SpacesContext();
        var expected = new Dictionary<string, int> { ["PROJECTOR"] = 1, ["TV"] = 2, ["SOUND"] = 1, ["LIGHTS"] = 1 };

        var fromEvents = await new Nodo.EventLifecycle.Infrastructure.SpacesCatalogAdapter(spaces).GetEquipmentStockAsync();
        var fromSchedule = await new Nodo.HallSchedule.Infrastructure.SpacesCatalogAdapter(spaces).GetEquipmentStockAsync();

        fromEvents.ShouldBe(expected, ignoreOrder: true);
        fromSchedule.ShouldBe(expected, ignoreOrder: true);
        fromSchedule["projector"].ShouldBe(1); // codes are matched regardless of case
    }

    [Fact]
    public void SpacesContext_PublishesTheVenueInItsOwnVocabulary()
    {
        var venue = new SpacesContext().Venues.Single();

        venue.VenueCode.ShouldBe("SALON-POLIVALENTE");
        venue.MaxCapacity.ShouldBe(80);
        venue.OpensAt.ShouldBe("07:00");
        venue.ClosesAt.ShouldBe("23:00");
    }
}
