using FluentValidation;
using NSubstitute;
using Nodo.BuildingBlocks.Application;
using Nodo.HallSchedule.Contracts;
using Nodo.Quotes.Application;
using Nodo.Quotes.Contracts;
using Nodo.Quotes.Domain;
using Nodo.SharedKernel;
using static Nodo.Quotes.UnitTests.UseCases.FakeMembershipDirectory;

namespace Nodo.Quotes.UnitTests.UseCases;

public class IssueAndPreviewTests
{
    // ---- IssueQuote ----

    [Fact]
    public async Task IssueQuote_MemberWithActivePlan_AppliesThePlanDiscountToTheHallRentalOnly()
    {
        using var host = new QtTestHost();

        // 4 h = 1.800; Ana (Flexible, 5 %) = -90; sound system 250 untouched.
        var result = await host.IssueAsync(host.IssueCommand(
            customerId: Ana, extras: [new ExtraServiceRequest("SOUND")]));

        result.Price.HallRental.ShouldBe(1800m);
        result.Price.MembershipDiscount.ShouldBe(90m);
        result.Price.ExtrasTotal.ShouldBe(250m);
        result.Price.Total.ShouldBe(1960m);
    }

    [Fact]
    public async Task IssueQuote_ExpiredMembership_GetsNoDiscount()
    {
        using var host = new QtTestHost();

        var result = await host.IssueAsync(host.IssueCommand(customerId: Carlos)); // plan says 8 %, but it is not active

        result.Price.MembershipDiscountPercentage.ShouldBe(0);
        result.Price.Total.ShouldBe(1800m);
    }

    [Fact]
    public async Task IssueQuote_ExternalCustomer_GetsNoDiscount()
    {
        using var host = new QtTestHost();

        var result = await host.IssueAsync(host.IssueCommand(customerId: Fundacion));

        result.Price.MembershipDiscount.ShouldBe(0m);
    }

    [Fact]
    public async Task IssueQuote_CateringWithoutQuantity_DefaultsToTheAttendance()
    {
        using var host = new QtTestHost();

        var result = await host.IssueAsync(host.IssueCommand(
            attendance: 35, extras: [new ExtraServiceRequest("COFFEE_BREAK")]));

        var line = result.ExtraServices.Single();
        line.Type.ShouldBe("ExternalCatering");
        line.Quantity.ShouldBe(35);
        line.UnitCost.ShouldBe(35m);
        line.LineTotal.ShouldBe(1225m);
    }

    [Fact]
    public async Task IssueQuote_EquipmentWithoutQuantity_DefaultsToOneUnitAtTheCatalogCost()
    {
        using var host = new QtTestHost();

        var result = await host.IssueAsync(host.IssueCommand(extras: [new ExtraServiceRequest("projector")]));

        var line = result.ExtraServices.Single();
        line.Type.ShouldBe("OwnEquipment");
        line.Code.ShouldBe("PROJECTOR");
        line.Quantity.ShouldBe(1);
        line.UnitCost.ShouldBe(150m);
    }

    [Fact]
    public async Task IssueQuote_ValidRequest_PersistsInOneCommitWithTheFrozenPrice()
    {
        using var host = new QtTestHost();

        var result = await host.IssueAsync();

        host.UnitOfWork.SaveCount.ShouldBe(1);
        var saved = host.Quotes.All.Single();
        saved.Id.Value.ShouldBe(result.QuoteId);
        saved.Status.ShouldBe(QuoteStatus.Issued);
        saved.Price.Total.Amount.ShouldBe(result.Price.Total);
        result.ExpiresAtUtc.ShouldBe(host.Clock.UtcNow.AddHours(96));
    }

    [Fact]
    public async Task IssueQuote_SlotAlreadyTaken_StillIssuesAndOnlyInformsAvailability_Br10()
    {
        using var host = new QtTestHost();
        host.HallScheduleIsBusy();

        var result = await host.IssueAsync();

        result.Availability.IsAvailable.ShouldBeFalse();
        result.Availability.Code.ShouldBe("SCHEDULE.SLOT_OVERLAP");
        host.Quotes.All.Count.ShouldBe(1); // issued anyway
        await host.HallSchedule.DidNotReceiveWithAnyArgs().PlaceProvisionalBlockAsync(default, default, default, default, default, default!, default);
        await host.HallSchedule.DidNotReceiveWithAnyArgs().PlaceDefinitiveBlockAsync(default, default, default, default, default, default!, default);
    }

    [Fact]
    public async Task IssueQuote_FreeSlot_ReportsItAsAvailable_Br10()
    {
        using var host = new QtTestHost();

        var result = await host.IssueAsync();

        result.Availability.IsAvailable.ShouldBeTrue();
    }

    [Fact]
    public async Task IssueQuote_OwnEquipmentIsPartOfTheAvailabilityCheck()
    {
        using var host = new QtTestHost();

        await host.IssueAsync(host.IssueCommand(extras: [new ExtraServiceRequest("TV", 2)]));

        await host.HallSchedule.Received(1).CheckAvailabilityAsync(
            QtTestHost.Hall, QtTestHost.EventDay, new TimeOnly(9, 0), new TimeOnly(13, 0),
            Arg.Is<IReadOnlyCollection<EquipmentRequest>>(e => e.Single().Code == "TV" && e.Single().Quantity == 2),
            null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IssueQuote_CateringIsNotSentAsEquipmentToTheSchedule()
    {
        using var host = new QtTestHost();

        await host.IssueAsync(host.IssueCommand(extras: [new ExtraServiceRequest("LUNCH")]));

        await host.HallSchedule.Received(1).CheckAvailabilityAsync(
            Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<TimeOnly>(), Arg.Any<TimeOnly>(),
            Arg.Is<IReadOnlyCollection<EquipmentRequest>>(e => e.Count == 0), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IssueQuote_DomainRuleViolated_ThrowsDomainExceptionAndSavesNothing()
    {
        using var host = new QtTestHost();

        var ex = await Should.ThrowAsync<DomainException>(
            () => host.IssueAsync(host.IssueCommand(startHour: 7, endHour: 11))); // BR-02

        ex.Code.ShouldBe("QUOTE.SLOT_OUTSIDE_OPERATING_HOURS");
        host.UnitOfWork.SaveCount.ShouldBe(0);
        host.Quotes.All.ShouldBeEmpty();
        host.Outbox.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task IssueQuote_EquipmentAboveStock_ThrowsExtraServiceInvalid()
    {
        using var host = new QtTestHost();

        var ex = await Should.ThrowAsync<DomainException>(
            () => host.IssueAsync(host.IssueCommand(extras: [new ExtraServiceRequest("PROJECTOR", 2)])));

        ex.Code.ShouldBe("EVENT.EXTRA_SERVICE_INVALID");
    }

    [Fact]
    public async Task IssueQuote_UnknownExtraServiceCode_ThrowsNotFound()
    {
        using var host = new QtTestHost();

        await Should.ThrowAsync<NotFoundException>(
            () => host.IssueAsync(host.IssueCommand(extras: [new ExtraServiceRequest("FIREWORKS")])));

        host.UnitOfWork.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task IssueQuote_UnknownHall_ThrowsNotFound()
    {
        using var host = new QtTestHost();
        var command = host.IssueCommand() with { HallId = Guid.NewGuid() };

        await Should.ThrowAsync<NotFoundException>(() => host.IssueAsync(command));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task IssueQuote_MissingPerformedBy_FailsShapeValidation(string performedBy)
    {
        using var host = new QtTestHost();
        var command = host.IssueCommand() with { PerformedBy = performedBy };

        var ex = await Should.ThrowAsync<ValidationException>(() => host.IssueAsync(command));

        ex.Errors.ShouldContain(e => e.PropertyName == nameof(IssueQuoteCommand.PerformedBy));
        host.UnitOfWork.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task IssueQuote_ExtraWithoutCode_FailsShapeValidation()
    {
        using var host = new QtTestHost();

        await Should.ThrowAsync<ValidationException>(
            () => host.IssueAsync(host.IssueCommand(extras: [new ExtraServiceRequest("")])));
    }

    [Fact]
    public async Task IssueQuote_Outbox_AddsQuoteIssuedIntegrationEventInTheSameCommit()
    {
        using var host = new QtTestHost();

        var result = await host.IssueAsync();

        var published = host.Outbox.Single<QuoteIssuedIntegrationEvent>();
        published.QuoteId.ShouldBe(result.QuoteId);
        published.CustomerId.ShouldBe(Ana);
        published.Date.ShouldBe(QtTestHost.EventDay);
        published.Start.ShouldBe(new TimeOnly(9, 0));
        published.End.ShouldBe(new TimeOnly(13, 0));
        published.Total.ShouldBe(result.Price.Total);
        published.ExpiresAtUtc.ShouldBe(result.ExpiresAtUtc);
        published.Id.ShouldNotBe(Guid.Empty);
        published.OccurredOnUtc.ShouldBe(host.Clock.UtcNow);
    }

    // ---- PreviewQuote ----

    [Fact]
    public async Task PreviewQuote_ReturnsPriceAndAvailabilityWithoutPersistingAnything()
    {
        using var host = new QtTestHost();

        var preview = await host.Mediator.Send(new PreviewQuoteQuery(
            Ana, QtTestHost.Hall, QtTestHost.EventDay, new TimeOnly(9, 0), new TimeOnly(13, 0), 40,
            [new ExtraServiceRequest("PROJECTOR")]));

        preview.Price.Total.ShouldBe(1710m + 150m);
        preview.Availability.IsAvailable.ShouldBeTrue();
        preview.ExtraServices.Single().Code.ShouldBe("PROJECTOR");
        host.UnitOfWork.SaveCount.ShouldBe(0);
        host.Quotes.All.ShouldBeEmpty();
        host.Outbox.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task PreviewQuote_TakenSlot_ReportsUnavailable()
    {
        using var host = new QtTestHost();
        host.HallScheduleIsBusy();

        var preview = await host.Mediator.Send(new PreviewQuoteQuery(
            Ana, QtTestHost.Hall, QtTestHost.EventDay, new TimeOnly(9, 0), new TimeOnly(13, 0), 40, []));

        preview.Availability.IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task PreviewQuote_InvalidRequest_ThrowsTheSameDomainExceptionAsIssuing()
    {
        using var host = new QtTestHost();

        var ex = await Should.ThrowAsync<DomainException>(() => host.Mediator.Send(new PreviewQuoteQuery(
            Ana, QtTestHost.Hall, QtTestHost.EventDay, new TimeOnly(9, 0), new TimeOnly(18, 0), 40, []))); // 9 h

        ex.Code.ShouldBe("EVENT.DURATION_OUT_OF_RANGE");
    }

    [Fact]
    public async Task PreviewQuote_AttendanceAboveTheLimit_ThrowsAttendanceOutOfRange()
    {
        using var host = new QtTestHost();

        var ex = await Should.ThrowAsync<DomainException>(() => host.Mediator.Send(new PreviewQuoteQuery(
            Ana, QtTestHost.Hall, QtTestHost.EventDay, new TimeOnly(9, 0), new TimeOnly(13, 0), 81, [])));

        ex.Code.ShouldBe("EVENT.ATTENDANCE_OUT_OF_RANGE");
    }
}
