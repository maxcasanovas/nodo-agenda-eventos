using System.Reflection;
using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Nodo.Api.Infrastructure;
using Nodo.Api.Invariants;
using Nodo.Api.OpenApi;
using Nodo.BuildingBlocks.Application;
using Nodo.BuildingBlocks.Infrastructure;
using Nodo.EventLifecycle.Infrastructure.Persistence;
using Nodo.HallSchedule.Infrastructure.Persistence;
using Nodo.Quotes.Infrastructure.Persistence;
using Nodo.SharedKernel;

namespace Nodo.Infrastructure.UnitTests;

/// <summary>The invariant catalog the API publishes and the probes run against.</summary>
public class InvariantCatalogTests
{
    private static readonly Assembly[] DomainAssemblies =
    [
        typeof(Nodo.SharedKernel.AssemblyMarker).Assembly,
        typeof(Nodo.Quotes.Domain.AssemblyMarker).Assembly,
        typeof(Nodo.EventLifecycle.Domain.AssemblyMarker).Assembly,
        typeof(Nodo.HallSchedule.Domain.AssemblyMarker).Assembly
    ];

    /// <summary>Every code constant declared in the modules' InvariantCodes classes.</summary>
    private static HashSet<string> DeclaredCodes() => DomainAssemblies
        .SelectMany(a => a.GetTypes())
        .Where(t => t.Name == "InvariantCodes" && t.IsAbstract && t.IsSealed)
        .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToHashSet();

    [Fact]
    public void Catalog_HasThe22InvariantsOfBusinessRulesInOrder() =>
        InvariantCatalog.All.Select(i => i.Id).ShouldBe(Enumerable.Range(1, 22).Select(n => $"INV-{n:00}"));

    [Fact]
    public void Catalog_EveryCodeIsARealCodeOfTheDomain()
    {
        var declared = DeclaredCodes();

        foreach (var invariant in InvariantCatalog.All.Where(i => !i.ByDesign))
        {
            foreach (var code in invariant.Code.Split(" / "))
                declared.ShouldContain(code, $"{invariant.Id}: '{code}' is not declared by any InvariantCodes class");
        }
    }

    [Fact]
    public void Catalog_ByDesignInvariantsAreExactlyTheOnesWithoutAnOperation()
    {
        InvariantCatalog.All.Where(i => i.ByDesign).Select(i => i.Id).ShouldBe(["INV-15", "INV-22"]);
        InvariantCatalog.All.Where(i => i.ByDesign).ShouldAllBe(i => i.Code == "(por diseño)");
    }

    [Fact]
    public void Catalog_EveryEntryIsDescribedInSpanishWithAnOwnerAndAnExampleMessage()
    {
        foreach (var invariant in InvariantCatalog.All)
        {
            invariant.Description.ShouldNotBeNullOrWhiteSpace(invariant.Id);
            invariant.Owner.ShouldNotBeNullOrWhiteSpace(invariant.Id);
            invariant.ExampleMessage.ShouldNotBeNullOrWhiteSpace(invariant.Id);
        }
    }

    [Fact]
    public void Catalog_DatabaseEnforcedFlagMatchesTheConstraintsActuallyInTheModels()
    {
        // INV-xx appears in the NAME of its constraint (ck_..._inv04_..., ex_..._inv18_..., ux_..._inv19_...).
        var names = new List<string>();
        foreach (var context in new DbContext[] { NewContext<QuotesDbContext>(), NewContext<EventLifecycleDbContext>(), NewContext<HallScheduleDbContext>() })
        {
            using (context)
            {
                var model = context.GetService<IDesignTimeModel>().Model;
                names.AddRange(model.GetEntityTypes().SelectMany(e => e.GetCheckConstraints()).Select(c => c.Name!));
                names.AddRange(model.GetEntityTypes().SelectMany(e => e.GetIndexes()).Select(i => i.GetDatabaseName()!));
            }
        }

        names.Add("ex_schedule_blocks_inv18_overlap"); // the exclusion constraint lives in the migration SQL, not in the model

        var withConstraint = names
            .Select(n => System.Text.RegularExpressions.Regex.Match(n, @"_inv(\d{2})_"))
            .Where(m => m.Success)
            .Select(m => $"INV-{m.Groups[1].Value}")
            .ToHashSet();

        var declared = InvariantCatalog.All.Where(i => i.DatabaseEnforced).Select(i => i.Id).ToHashSet();
        foreach (var id in declared)
            withConstraint.ShouldContain(id, $"{id} says it is database-enforced but no constraint carries its id");
        withConstraint.Where(id => !declared.Contains(id)).ShouldBeEmpty("a constraint guards an invariant the catalog says is not database-enforced");
    }

    [Fact]
    public void Catalog_Inv17IsDatabaseEnforcedByTheQuotesDiscountCheck()
    {
        using var context = NewContext<QuotesDbContext>();

        var names = context.GetService<IDesignTimeModel>().Model.GetEntityTypes()
            .SelectMany(e => e.GetCheckConstraints()).Select(c => c.Name);

        names.ShouldContain("ck_quotes_inv17_membership_discount");
    }

    [Fact]
    public void Probes_ExistForEveryInvariant()
    {
        foreach (var invariant in InvariantCatalog.All)
            InvariantProbes.Has(invariant.Id).ShouldBeTrue($"{invariant.Id} has no probe");
    }

    [Fact]
    public void Probes_AreFoundRegardlessOfCase() => InvariantProbes.Has("inv-01").ShouldBeTrue();

    [Fact]
    public void Probes_UnknownIdHasNone() => InvariantProbes.Has("INV-99").ShouldBeFalse();

    [Fact]
    public async Task Runner_UnknownInvariant_IsNotFoundBeforeTouchingTheDatabase()
    {
        var runner = new InvariantProbeRunner(null!, null!);

        await Should.ThrowAsync<NotFoundException>(() => runner.RunAsync("INV-99", CancellationToken.None));
    }

    [Theory]
    [InlineData("INV-04", 1)]
    [InlineData("INV-18", 1)]
    public void FirstCode_ReturnsTheMainCode(string id, int parts) =>
        InvariantCatalog.FirstCode(InvariantCatalog.Find(id)!).Split(" / ").Length.ShouldBe(parts);

    [Fact]
    public void FirstCode_OfAnInvariantWithTwoCodes_IsTheFirstOne() =>
        InvariantCatalog.FirstCode(InvariantCatalog.Find("INV-16")!).ShouldBe("QUOTE.SLOT_OUTSIDE_OPERATING_HOURS");

    private static TContext NewContext<TContext>() where TContext : DbContext =>
        (TContext)Activator.CreateInstance(typeof(TContext),
            new DbContextOptionsBuilder<TContext>().UseNpgsql("Host=localhost;Database=model-only").UseSnakeCaseNamingConvention().Options)!;
}

/// <summary>How a failure is shown over HTTP. The same mapping serves real requests and the probes.</summary>
public class ProblemMappingTests
{
    [Fact]
    public void DomainException_Is422WithItsCodeAndSpanishMessage()
    {
        var problem = ProblemMapping.Map(new DomainException("EVENT.DURATION_OUT_OF_RANGE", "La duración no es válida."))!;

        problem.Status.ShouldBe(422);
        problem.Code.ShouldBe("EVENT.DURATION_OUT_OF_RANGE");
        problem.Detail.ShouldBe("La duración no es válida.");
    }

    [Fact]
    public void ValidationException_Is400WithTheErrorsPerField()
    {
        var exception = new ValidationException([
            new ValidationFailure("ApprovedBy", "Indica quién aprueba."),
            new ValidationFailure("ApprovedBy", "Otro problema."),
            new ValidationFailure("Reason", "Es obligatorio.")
        ]);

        var problem = ProblemMapping.Map(exception)!;

        problem.Status.ShouldBe(400);
        problem.Code.ShouldBeNull();
        problem.Errors!["ApprovedBy"].ShouldBe(["Indica quién aprueba.", "Otro problema."]);
        problem.Errors["Reason"].ShouldBe(["Es obligatorio."]);
    }

    [Fact]
    public void NotFoundException_Is404() =>
        ProblemMapping.Map(new NotFoundException("No existe."))!.Status.ShouldBe(404);

    [Fact]
    public void ConflictException_Is409_CarryingTheInvariantCodeWhenThereIsOne()
    {
        var withCode = ProblemMapping.Map(new ConflictException("choque", "SCHEDULE.SLOT_OVERLAP"))!;
        var withoutCode = ProblemMapping.Map(new ConflictException("otra operación"))!;

        withCode.Status.ShouldBe(409);
        withCode.Code.ShouldBe("SCHEDULE.SLOT_OVERLAP");
        withoutCode.Code.ShouldBeNull();
    }

    [Fact]
    public void UnexpectedException_IsNotMapped_SoItStaysAnInternalError() =>
        ProblemMapping.Map(new InvalidOperationException("boom")).ShouldBeNull();
}

/// <summary>The examples shown in Scalar must be bodies the API would actually accept (or produce).</summary>
public class OpenApiExampleTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static TheoryData<string> DocumentedTypes()
    {
        var data = new TheoryData<string>();
        var field = typeof(Examples).GetField("Documented", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var type in ((HashSet<Type>)field.GetValue(null)!).OrderBy(t => t.Name))
            data.Add(type.AssemblyQualifiedName!);
        return data;
    }

    [Theory]
    [MemberData(nameof(DocumentedTypes))]
    public void Example_IsValidJsonThatDeserializesIntoItsOwnType(string typeName)
    {
        var type = Type.GetType(typeName)!;

        var example = Examples.For(type, new DateOnly(2026, 11, 1));

        example.ShouldNotBeNull($"{type.Name} is declared as documented but has no example");
        Should.NotThrow(() => JsonSerializer.Deserialize(example.ToJsonString(), type, Web), type.Name);
    }

    [Fact]
    public void Example_DatesFollowTheClock_SoTheyStayInsideTheBookingWindow()
    {
        var early = Examples.For(typeof(Nodo.Api.Contracts.IssueQuoteRequest), new DateOnly(2026, 11, 1))!["date"]!.ToString();
        var later = Examples.For(typeof(Nodo.Api.Contracts.IssueQuoteRequest), new DateOnly(2027, 3, 1))!["date"]!.ToString();

        early.ShouldBe("2026-11-21"); // today + 20 days: between the 3 and 180 allowed
        later.ShouldBe("2027-03-21");
    }

    [Fact]
    public void Example_IssueQuoteRequestRoundTripsToTheFieldsTheEndpointExpects()
    {
        var request = JsonSerializer.Deserialize<Nodo.Api.Contracts.IssueQuoteRequest>(
            Examples.For(typeof(Nodo.Api.Contracts.IssueQuoteRequest), new DateOnly(2026, 11, 1))!.ToJsonString(), Web)!;

        request.Attendance.ShouldBe(40);
        request.StartTime.ShouldBe(new TimeOnly(9, 0));
        request.ExtraServices!.Single().Code.ShouldBe("PROJECTOR");
    }
}

/// <summary>The scoped clock: the demo's time plus an offset that only one scope sees.</summary>
public class ScopedClockTests
{
    [Fact]
    public void WithoutOffset_ItShowsTheDemoClock()
    {
        var demo = new DemoClock();
        var clock = new ScopedClock(demo, new ClockOffset());

        (clock.UtcNow - demo.UtcNow).Duration().ShouldBeLessThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Offset_MovesOnlyThatScopesClock()
    {
        var demo = new DemoClock();
        var probeOffset = new ClockOffset { Extra = TimeSpan.FromDays(5) };
        var probe = new ScopedClock(demo, probeOffset);
        var everyoneElse = new ScopedClock(demo, new ClockOffset());

        (probe.UtcNow - everyoneElse.UtcNow).ShouldBeInRange(TimeSpan.FromDays(5) - TimeSpan.FromSeconds(1), TimeSpan.FromDays(5) + TimeSpan.FromSeconds(1));
        (demo.UtcNow - everyoneElse.UtcNow).Duration().ShouldBeLessThan(TimeSpan.FromSeconds(1)); // the demo clock itself is untouched
    }

    [Fact]
    public void DemoOffsetAndScopeOffset_Add()
    {
        var demo = new DemoClock();
        demo.Advance(TimeSpan.FromDays(2));
        var clock = new ScopedClock(demo, new ClockOffset { Extra = TimeSpan.FromDays(1) });

        (clock.UtcNow - new DemoClock().UtcNow).ShouldBeInRange(TimeSpan.FromDays(3) - TimeSpan.FromSeconds(1), TimeSpan.FromDays(3) + TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void LocalTimeAndToday_FollowTheOffsetInLaPazTime()
    {
        var demo = new DemoClock();
        var clock = new ScopedClock(demo, new ClockOffset { Extra = TimeSpan.FromDays(1) });

        // The clock keeps ticking between calls, so compare with a tolerance instead of exact values.
        (clock.LocalNow - NodoTimeZone.ToLocal(clock.UtcNow)).Duration().ShouldBeLessThan(TimeSpan.FromSeconds(1));
        clock.TodayLocal.ShouldBe(DateOnly.FromDateTime(NodoTimeZone.ToLocal(demo.UtcNow + TimeSpan.FromDays(1))));
    }
}
