using Nodo.Quotes.Domain;
using static Nodo.Quotes.UnitTests.TestData;

namespace Nodo.Quotes.UnitTests;

public class QuoteBehaviorTests
{
    [Fact]
    public void Issue_ValidInput_CreatesIssuedQuoteWithSnapshotAndRaisesQuoteIssued()
    {
        var quote = Issue(extras: [Equipment("PROJECTOR", "Proyector", 150m)], discount: 5);

        quote.Status.ShouldBe(QuoteStatus.Issued);
        quote.CustomerId.ShouldBe(Customer);
        quote.HallId.ShouldBe(Hall);
        quote.IssuedAtUtc.ShouldBe(NowUtc);
        quote.Attendance.Value.ShouldBe(40);
        var issued = quote.DequeueDomainEvents().ShouldHaveSingleItem().ShouldBeOfType<QuoteIssued>();
        issued.QuoteId.ShouldBe(quote.Id);
        issued.Total.ShouldBe(quote.Price.Total);
        issued.ExpiresAtUtc.ShouldBe(quote.ExpiresAtUtc);
    }

    [Fact]
    public void Accept_IssuedQuote_RaisesQuoteAcceptedWithFullSnapshot()
    {
        var quote = Issue(extras: [Catering("LUNCH", "Almuerzo ejecutivo", 90m, 40)], discount: 5);
        quote.DequeueDomainEvents();

        quote.Accept(NowUtc.AddHours(2), Today);

        quote.Status.ShouldBe(QuoteStatus.Accepted);
        quote.AcceptedAtUtc.ShouldBe(NowUtc.AddHours(2));
        var accepted = quote.DequeueDomainEvents().ShouldHaveSingleItem().ShouldBeOfType<QuoteAccepted>();
        accepted.Slot.ShouldBe(quote.Slot);
        accepted.Price.ShouldBe(quote.Price);
        accepted.ExtraServices.ShouldBe(quote.ExtraServices);
        accepted.Attendance.ShouldBe(quote.Attendance);
    }

    [Fact]
    public void Issue_DoesNotRequireCalendarAvailability_Br10()
    {
        // BR-10: issuing never blocks the calendar; the domain has no dependency on HallSchedule at all.
        // Two quotes for the same slot can coexist.
        var first = Issue();
        var second = Issue();

        first.Slot.ShouldBe(second.Slot);
        first.Id.ShouldNotBe(second.Id);
    }

    [Fact]
    public void ExtraServices_ExposedAsReadOnly()
    {
        var quote = Issue(extras: [Equipment("TV", "Pantalla TV", 100m)]);

        (quote.ExtraServices is List<ExtraServiceLine>).ShouldBeFalse();
    }

    [Fact]
    public void Issue_CallerCollectionChangedAfterwards_DoesNotAffectTheQuote()
    {
        var extras = new List<ExtraServiceLine> { Equipment("TV", "Pantalla TV", 100m) };
        var quote = Issue(extras: extras);

        extras.Add(Equipment("SOUND", "Sistema de sonido", 250m));

        quote.ExtraServices.Count.ShouldBe(1);
    }
}
