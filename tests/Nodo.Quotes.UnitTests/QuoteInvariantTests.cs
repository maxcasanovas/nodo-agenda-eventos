using Nodo.Quotes.Domain;
using Nodo.SharedKernel;
using static Nodo.Quotes.UnitTests.TestData;

namespace Nodo.Quotes.UnitTests;

public class QuoteInvariantTests
{
    // ---- INV-01 / BR-01: duration 1..8 whole hours ----

    [Theory]
    [InlineData(9, 10)] // 1 h
    [InlineData(9, 17)] // 8 h
    public void TimeSlotCreate_Inv01_DurationWithinRange_Succeeds(int start, int end) =>
        Slot(start, end).DurationHours.ShouldBe(end - start);

    [Theory]
    [InlineData(9, 18)] // 9 h
    [InlineData(9, 9)]
    [InlineData(12, 10)]
    public void TimeSlotCreate_Inv01_DurationOutOfRange_ThrowsDurationOutOfRange(int start, int end) =>
        Should.Throw<DomainException>(() => Slot(start, end)).Code.ShouldBe("EVENT.DURATION_OUT_OF_RANGE");

    [Fact]
    public void TimeSlotCreate_Inv01_NotExactHour_ThrowsDurationOutOfRange() =>
        Should.Throw<DomainException>(() => TimeSlot.Create(EventDay, new TimeOnly(9, 30), new TimeOnly(12, 0)))
            .Code.ShouldBe("EVENT.DURATION_OUT_OF_RANGE");

    // ---- INV-04 / BR-04: attendance 1..80 ----

    [Theory]
    [InlineData(1)]
    [InlineData(40)]
    [InlineData(80)]
    public void AttendanceCreate_Inv04_WithinRange_Succeeds(int value) =>
        Attendance.Create(value).Value.ShouldBe(value);

    [Theory]
    [InlineData(0)]
    [InlineData(81)]
    [InlineData(-5)]
    public void AttendanceCreate_Inv04_OutOfRange_ThrowsAttendanceOutOfRange(int value) =>
        Should.Throw<DomainException>(() => Attendance.Create(value)).Code.ShouldBe("EVENT.ATTENDANCE_OUT_OF_RANGE");

    // ---- INV-14 / BR-09: validity of 96 h ----

    [Fact]
    public void Issue_Inv14_SetsExpiryExactly96HoursAfterIssue()
    {
        var quote = Issue();

        quote.ExpiresAtUtc.ShouldBe(NowUtc.AddHours(96));
    }

    [Fact]
    public void Accept_Inv14_At95h59m_Succeeds()
    {
        var quote = Issue();

        quote.Accept(NowUtc.AddHours(95).AddMinutes(59), Today);

        quote.Status.ShouldBe(QuoteStatus.Accepted);
    }

    [Fact]
    public void Accept_Inv14_At96h00m_ThrowsExpired()
    {
        var quote = Issue();

        Should.Throw<DomainException>(() => quote.Accept(NowUtc.AddHours(96), Today)).Code.ShouldBe("QUOTE.EXPIRED");
    }

    [Fact]
    public void Accept_Inv14_AfterBeingMarkedExpired_ThrowsExpired()
    {
        var quote = Issue();
        quote.Expire(NowUtc.AddHours(100));

        Should.Throw<DomainException>(() => quote.Accept(NowUtc.AddHours(100), Today)).Code.ShouldBe("QUOTE.EXPIRED");
    }

    [Fact]
    public void Accept_Inv14_RejectedAcceptance_LeavesQuoteIssuedAndRaisesNoEvent()
    {
        var quote = Issue();
        quote.DequeueDomainEvents();

        Should.Throw<DomainException>(() => quote.Accept(NowUtc.AddHours(97), Today));

        quote.Status.ShouldBe(QuoteStatus.Issued);
        quote.AcceptedAtUtc.ShouldBeNull();
        quote.DequeueDomainEvents().ShouldBeEmpty();
    }

    [Fact]
    public void Accept_AlreadyAccepted_ThrowsInvalidStateTransition()
    {
        var quote = Issue();
        quote.Accept(NowUtc.AddHours(1), Today);

        Should.Throw<DomainException>(() => quote.Accept(NowUtc.AddHours(2), Today))
            .Code.ShouldBe("QUOTE.INVALID_STATE_TRANSITION");
    }

    // ---- INV-16 on acceptance: the quote must still be convertible into an event request (BR-05) ----

    [Theory]
    [InlineData(3)]  // event day = acceptance day + 3: lower bound
    [InlineData(10)]
    public void Accept_Inv16_EventDateStillInsideBookingWindow_Succeeds(int daysBetweenAcceptanceAndEvent)
    {
        var quote = Issue(Slot(9, 13, Today.AddDays(6)));
        var acceptanceDay = Today.AddDays(6 - daysBetweenAcceptanceAndEvent);

        quote.Accept(NowUtc.AddHours(30), acceptanceDay);

        quote.Status.ShouldBe(QuoteStatus.Accepted);
    }

    [Fact]
    public void Accept_Inv16_EventDateNoLongerInsideBookingWindow_ThrowsDateOutOfRangeAndKeepsQuoteIssued()
    {
        var quote = Issue(Slot(9, 13, Today.AddDays(6)));
        quote.DequeueDomainEvents();
        var acceptanceDay = Today.AddDays(4); // event is only 2 days away

        Should.Throw<DomainException>(() => quote.Accept(NowUtc.AddHours(30), acceptanceDay))
            .Code.ShouldBe("QUOTE.DATE_OUT_OF_RANGE");

        quote.Status.ShouldBe(QuoteStatus.Issued);
        quote.AcceptedAtUtc.ShouldBeNull();
        quote.DequeueDomainEvents().ShouldBeEmpty();
    }

    // ---- BR-07: the plan's discount applies only while the membership is active ----

    [Theory]
    [InlineData(true, 5, 5)]
    [InlineData(true, 10, 10)]
    [InlineData(true, 0, 0)]
    [InlineData(false, 8, 0)]
    public void MembershipDiscountPolicyEffective_Br07_OnlyActiveMembershipsGetTheDiscount(bool active, int plan, int expected) =>
        MembershipDiscountPolicy.Effective(active, plan).ShouldBe(expected);

    // ---- INV-15: price and services frozen by design ----

    [Fact]
    public void Accept_Inv15_KeepsPriceAndServicesUnchanged()
    {
        var quote = Issue(extras: [Equipment("PROJECTOR", "Proyector", 150m)], discount: 5);
        var price = quote.Price;
        var services = quote.ExtraServices.ToList();

        quote.Accept(NowUtc.AddHours(1), Today);

        quote.Price.ShouldBeSameAs(price);
        quote.ExtraServices.ShouldBe(services);
    }

    [Fact]
    public void Quote_Inv15_ExposesNoMethodThatChangesPriceOrServices()
    {
        var methods = typeof(Quote)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .ToHashSet();

        methods.ShouldBe(["Accept", "Expire", "IsExpiredAt"], ignoreOrder: true);
    }

    // ---- INV-16 / BR-02: operating hours 07:00-23:00 including 1 h buffers ----

    [Theory]
    [InlineData(8, 12)]  // earliest start
    [InlineData(18, 22)] // latest end
    public void Issue_Inv16_WithinOperatingHours_Succeeds(int start, int end) =>
        Issue(Slot(start, end)).Status.ShouldBe(QuoteStatus.Issued);

    [Theory]
    [InlineData(7, 11)]  // starts at 07:00: no room for the setup hour
    [InlineData(19, 23)] // ends at 23:00: no room for cleanup
    public void Issue_Inv16_OutsideOperatingHours_ThrowsSlotOutsideOperatingHours(int start, int end) =>
        Should.Throw<DomainException>(() => Issue(Slot(start, end)))
            .Code.ShouldBe("QUOTE.SLOT_OUTSIDE_OPERATING_HOURS");

    // ---- INV-16 / BR-05: booking window today+3 .. today+180 ----

    [Theory]
    [InlineData(3)]
    [InlineData(90)]
    [InlineData(180)]
    public void Issue_Inv16_DateWithinWindow_Succeeds(int daysAhead) =>
        Issue(Slot(9, 13, Today.AddDays(daysAhead))).Slot.Date.ShouldBe(Today.AddDays(daysAhead));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(181)]
    [InlineData(-1)]
    public void Issue_Inv16_DateOutsideWindow_ThrowsDateOutOfRange(int daysAhead) =>
        Should.Throw<DomainException>(() => Issue(Slot(9, 13, Today.AddDays(daysAhead))))
            .Code.ShouldBe("QUOTE.DATE_OUT_OF_RANGE");

    [Fact]
    public void Issue_Inv16_WindowUsesLocalDateNotUtc()
    {
        // 03:00 UTC on Nov 2 is still Nov 1 at 23:00 in La Paz: "today" for the window is the local date.
        var nowUtc = new DateTime(2026, 11, 2, 3, 0, 0, DateTimeKind.Utc);
        var localToday = DateOnly.FromDateTime(NodoTimeZone.ToLocal(nowUtc));

        localToday.ShouldBe(new DateOnly(2026, 11, 1));
        Should.NotThrow(() => Issue(Slot(9, 13, localToday.AddDays(3)), nowUtc: nowUtc, today: localToday));
    }

    // ---- INV-17 / BR-07: membership discount 0..10 % and only on the hall rental ----

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(10)]
    public void Issue_Inv17_DiscountWithinRange_Succeeds(int percentage) =>
        Issue(discount: percentage).Price.MembershipDiscountPercentage.ShouldBe(percentage);

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    [InlineData(50)]
    public void Issue_Inv17_DiscountOutOfRange_ThrowsInvalidDiscount(int percentage) =>
        Should.Throw<DomainException>(() => Issue(discount: percentage)).Code.ShouldBe("QUOTE.INVALID_DISCOUNT");

    [Fact]
    public void Issue_Inv17_DiscountAppliesOnlyToHallRental_NotToExtras()
    {
        // 4 h = 1.800 Bs rental; 10 % off = 180; sound system 250 stays untouched.
        var quote = Issue(extras: [Equipment("SOUND", "Sistema de sonido", 250m)], discount: 10);

        quote.Price.HallRental.Amount.ShouldBe(1800m);
        quote.Price.MembershipDiscount.Amount.ShouldBe(180m);
        quote.Price.ExtrasTotal.Amount.ShouldBe(250m);
        quote.Price.Total.Amount.ShouldBe(1870m);
    }

    // ---- BR-08 / INV-13: own equipment never above stock ----

    [Fact]
    public void Issue_Br08_EquipmentUpToStock_Succeeds() =>
        Issue(extras: [Equipment("TV", "Pantalla TV", 100m, quantity: 2)]).ExtraServices.Count.ShouldBe(1);

    [Fact]
    public void Issue_Br08_EquipmentAboveStock_ThrowsExtraServiceInvalid() =>
        Should.Throw<DomainException>(() => Issue(extras: [Equipment("TV", "Pantalla TV", 100m, quantity: 3)]))
            .Code.ShouldBe("EVENT.EXTRA_SERVICE_INVALID");

    [Fact]
    public void Issue_Br08_CateringIsNotLimitedByEquipmentStock() =>
        Should.NotThrow(() => Issue(attendance: 80, extras: [Catering("LUNCH", "Almuerzo ejecutivo", 90m, 80)]));

    [Fact]
    public void Issue_Br08_RepeatedServiceCode_ThrowsExtraServiceInvalid() =>
        Should.Throw<DomainException>(() => Issue(extras:
            [Equipment("TV", "Pantalla TV", 100m), Equipment("TV", "Pantalla TV", 100m)]))
            .Code.ShouldBe("EVENT.EXTRA_SERVICE_INVALID");

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void ExtraServiceLineCreate_Br08_QuantityBelowOne_ThrowsExtraServiceInvalid(int quantity) =>
        Should.Throw<DomainException>(() => Equipment("TV", "Pantalla TV", 100m, quantity))
            .Code.ShouldBe("EVENT.EXTRA_SERVICE_INVALID");

    // ---- Expire ----

    [Fact]
    public void Expire_Before96h_DoesNothing()
    {
        var quote = Issue();
        quote.DequeueDomainEvents();

        quote.Expire(NowUtc.AddHours(95).AddMinutes(59)).ShouldBeFalse();

        quote.Status.ShouldBe(QuoteStatus.Issued);
        quote.DequeueDomainEvents().ShouldBeEmpty();
    }

    [Fact]
    public void Expire_At96h_MarksExpiredOnceAndIsIdempotent()
    {
        var quote = Issue();
        quote.DequeueDomainEvents();

        quote.Expire(NowUtc.AddHours(96)).ShouldBeTrue();
        quote.Expire(NowUtc.AddHours(97)).ShouldBeFalse();

        quote.Status.ShouldBe(QuoteStatus.Expired);
        quote.DequeueDomainEvents().ShouldHaveSingleItem().ShouldBeOfType<QuoteExpired>();
    }

    [Fact]
    public void Expire_AcceptedQuote_IsIgnored()
    {
        var quote = Issue();
        quote.Accept(NowUtc.AddHours(1), Today);

        quote.Expire(NowUtc.AddHours(200)).ShouldBeFalse();

        quote.Status.ShouldBe(QuoteStatus.Accepted);
    }
}
