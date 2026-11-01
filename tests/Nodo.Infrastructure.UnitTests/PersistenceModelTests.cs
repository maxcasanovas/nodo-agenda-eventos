using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Nodo.BuildingBlocks.Infrastructure.Messaging;
using Nodo.EventLifecycle.Domain;
using Nodo.EventLifecycle.Infrastructure.Persistence;
using Nodo.HallSchedule.Infrastructure.Persistence;
using Nodo.Quotes.Domain;
using Nodo.Quotes.Infrastructure.Persistence;

namespace Nodo.Infrastructure.UnitTests;

/// <summary>
/// Builds each module's EF model without touching a database and checks what the specs require of it: schemas,
/// optimistic concurrency, enums as readable strings, the invariant constraints of the catalog (named after their INV id)
/// and that the migrations are in sync with the model.
/// </summary>
public class PersistenceModelTests
{
    private const string FakeConnection = "Host=localhost;Database=model-only;Username=x;Password=x";

    private static TContext Create<TContext>() where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(FakeConnection)
            .UseSnakeCaseNamingConvention()
            .Options;
        return (TContext)Activator.CreateInstance(typeof(TContext), options)!;
    }

    // Check constraints and index filters are design-time metadata: the runtime model drops them.
    private static IModel DesignModel(DbContext context) => context.GetService<IDesignTimeModel>().Model;

    private static IEnumerable<string> CheckConstraintNames(IModel model) => model.GetEntityTypes()
        .SelectMany(e => e.GetCheckConstraints())
        .Select(c => c.Name!);

    private static IEnumerable<string> IndexNames(IModel model) => model.GetEntityTypes()
        .SelectMany(e => e.GetIndexes())
        .Select(i => i.GetDatabaseName()!);

    // ---- schemas and messaging tables ----

    [Theory]
    [InlineData(typeof(QuotesDbContext), "quotes")]
    [InlineData(typeof(EventLifecycleDbContext), "event_lifecycle")]
    [InlineData(typeof(HallScheduleDbContext), "hall_schedule")]
    public void EachModule_LivesInItsOwnSchema_WithItsOwnOutboxAndInbox(Type contextType, string schema)
    {
        using var context = (DbContext)(contextType == typeof(QuotesDbContext) ? Create<QuotesDbContext>()
            : contextType == typeof(EventLifecycleDbContext) ? Create<EventLifecycleDbContext>()
            : Create<HallScheduleDbContext>());

        var model = context.Model;

        model.GetDefaultSchema().ShouldBe(schema);
        model.GetEntityTypes().Where(e => e.GetTableName() is not null).ShouldAllBe(e => e.GetSchema() == schema);
        model.FindEntityType(typeof(OutboxMessage))!.GetTableName().ShouldBe("outbox_messages");
        model.FindEntityType(typeof(InboxMessage))!.GetTableName().ShouldBe("inbox_messages");
    }

    [Fact]
    public void OutboxMessage_StoresItsContentAsJsonb()
    {
        using var context = Create<QuotesDbContext>();

        var content = context.Model.FindEntityType(typeof(OutboxMessage))!.FindProperty(nameof(OutboxMessage.Content))!;

        content.GetColumnType().ShouldBe("jsonb");
    }

    // ---- optimistic concurrency ----

    [Fact]
    public void AggregateRoots_UseXminAsConcurrencyToken()
    {
        using var quotes = Create<QuotesDbContext>();
        using var events = Create<EventLifecycleDbContext>();
        using var schedule = Create<HallScheduleDbContext>();

        var roots = new[]
        {
            quotes.Model.FindEntityType(typeof(Quote))!,
            events.Model.FindEntityType(typeof(HallEvent))!,
            events.Model.FindEntityType(typeof(CustomerCredit))!,
            schedule.Model.FindEntityType(typeof(Nodo.HallSchedule.Domain.HallSchedule))!
        };

        foreach (var root in roots)
        {
            var xmin = root.FindProperty("xmin");
            xmin.ShouldNotBeNull(root.DisplayName());
            xmin.IsConcurrencyToken.ShouldBeTrue(root.DisplayName());
            xmin.GetColumnName().ShouldBe("xmin");
        }
    }

    // ---- enums as readable strings ----

    [Fact]
    public void Enums_AreStoredAsStrings()
    {
        using var quotes = Create<QuotesDbContext>();
        using var events = Create<EventLifecycleDbContext>();
        using var schedule = Create<HallScheduleDbContext>();

        quotes.Model.FindEntityType(typeof(Quote))!.FindProperty(nameof(Quote.Status))!.GetProviderClrType().ShouldBe(typeof(string));
        events.Model.FindEntityType(typeof(HallEvent))!.FindProperty(nameof(HallEvent.Status))!.GetProviderClrType().ShouldBe(typeof(string));
        events.Model.FindEntityType(typeof(ExtraServiceItem))!.FindProperty(nameof(ExtraServiceItem.Type))!.GetProviderClrType().ShouldBe(typeof(string));
        schedule.Model.FindEntityType(typeof(Nodo.HallSchedule.Domain.ScheduleBlock))!
            .FindProperty(nameof(Nodo.HallSchedule.Domain.ScheduleBlock.Status))!.GetProviderClrType().ShouldBe(typeof(string));
    }

    // ---- the catalog's "BD" column (business-rules.md §9) ----

    [Fact]
    public void Quotes_HasTheCheckConstraintsOfItsInvariants()
    {
        using var context = Create<QuotesDbContext>();

        CheckConstraintNames(DesignModel(context)).ShouldBe(
        [
            "ck_quotes_inv01_duration", "ck_quotes_inv04_attendance",
            "ck_quotes_inv14_validity_96h", "ck_quotes_inv17_membership_discount"
        ], ignoreOrder: true);
    }

    [Fact]
    public void EventLifecycle_HasTheCheckConstraintsOfItsInvariants()
    {
        using var context = Create<EventLifecycleDbContext>();

        CheckConstraintNames(DesignModel(context)).ShouldBe(
        [
            "ck_hall_events_inv01_duration", "ck_hall_events_inv02_deposit_percentage",
            "ck_hall_events_inv04_attendance", "ck_hall_events_inv05_credit_generated",
            "ck_customer_credits_inv21_balance_non_negative"
        ], ignoreOrder: true);
    }

    [Fact]
    public void HallSchedule_HasTheUniqueActiveBlockIndexAndBlockChecks()
    {
        using var context = Create<HallScheduleDbContext>();

        IndexNames(DesignModel(context)).ShouldContain("ux_schedule_blocks_inv19_one_active_block_per_event");
        var index = DesignModel(context).GetEntityTypes().SelectMany(e => e.GetIndexes())
            .Single(i => i.GetDatabaseName() == "ux_schedule_blocks_inv19_one_active_block_per_event");
        index.IsUnique.ShouldBeTrue();
        index.GetFilter().ShouldBe("status = 'Active'");
        CheckConstraintNames(DesignModel(context)).ShouldBe(["ck_schedule_blocks_inv01_duration", "ck_schedule_blocks_same_day"], ignoreOrder: true);
    }

    [Fact]
    public void ScheduleBlocks_BufferedRangeIsAStoredGeneratedColumn()
    {
        using var context = Create<HallScheduleDbContext>();
        var block = context.Model.FindEntityType(typeof(Nodo.HallSchedule.Domain.ScheduleBlock))!;

        foreach (var name in new[] { "BufferedStart", "BufferedEnd" })
        {
            var property = block.FindProperty(name)!;
            property.GetComputedColumnSql().ShouldNotBeNullOrWhiteSpace();
            property.GetIsStored().ShouldBe(true);
        }
    }

    [Fact]
    public void EveryInvariantConstraintName_ResolvesToACodeInItsModulesMap()
    {
        var expected = new Dictionary<string, string>
        {
            ["ck_quotes_inv01_duration"] = "EVENT.DURATION_OUT_OF_RANGE",
            ["ck_quotes_inv04_attendance"] = "EVENT.ATTENDANCE_OUT_OF_RANGE",
            ["ck_quotes_inv14_validity_96h"] = "QUOTE.EXPIRED",
            ["ck_quotes_inv17_membership_discount"] = "QUOTE.INVALID_DISCOUNT",
        };
        using var quotes = Create<QuotesDbContext>();
        foreach (var name in CheckConstraintNames(DesignModel(quotes)))
            Nodo.Quotes.Infrastructure.ConstraintCodes.Map.CodeOf(name).ShouldBe(expected[name], name);

        using var events = Create<EventLifecycleDbContext>();
        foreach (var name in CheckConstraintNames(DesignModel(events)))
            Nodo.EventLifecycle.Infrastructure.ConstraintCodes.Map.CodeOf(name).ShouldNotBeNull(name);

        using var schedule = Create<HallScheduleDbContext>();
        foreach (var name in CheckConstraintNames(DesignModel(schedule)).Where(n => n.Contains("_inv")))
            Nodo.HallSchedule.Infrastructure.ConstraintCodes.Map.CodeOf(name).ShouldNotBeNull(name);
        Nodo.HallSchedule.Infrastructure.ConstraintCodes.Map.CodeOf("ex_schedule_blocks_inv18_overlap").ShouldBe("SCHEDULE.SLOT_OVERLAP");
        Nodo.HallSchedule.Infrastructure.ConstraintCodes.Map.CodeOf("ux_schedule_blocks_inv19_one_active_block_per_event").ShouldBe("SCHEDULE.DUPLICATE_BLOCK");
    }

    // ---- migrations ----

    [Fact]
    public void HallScheduleInitialCreate_EnablesBtreeGistAndAddsTheOverlapExclusionConstraint()
    {
        var sql = SqlOf(new Nodo.HallSchedule.Infrastructure.Persistence.Migrations.InitialCreate());

        sql.ShouldContain(s => s.Contains("CREATE EXTENSION IF NOT EXISTS btree_gist"));
        var exclusion = sql.Single(s => s.Contains("ex_schedule_blocks_inv18_overlap"));
        exclusion.ShouldContain("EXCLUDE USING gist");
        exclusion.ShouldContain("hall_id WITH =");
        exclusion.ShouldContain("tsrange(buffered_start, buffered_end, '[)') WITH &&");
        exclusion.ShouldContain("WHERE (status = 'Active')");
    }

    [Fact]
    public void SeedMigrations_LoadTheDemoDataOfBusinessRules()
    {
        var quotes = string.Join("\n", SqlOf(new Nodo.Quotes.Infrastructure.Persistence.Migrations.SeedDemoData()));
        var events = string.Join("\n", SqlOf(new Nodo.EventLifecycle.Infrastructure.Persistence.Migrations.SeedDemoData()));
        var schedule = string.Join("\n", SqlOf(new Nodo.HallSchedule.Infrastructure.Persistence.Migrations.SeedDemoData()));

        for (var n = 1; n <= 8; n++)
            quotes.ShouldContain($"33333333-0000-0000-0000-00000000000{n}");
        for (var n = 1; n <= 6; n++)
        {
            events.ShouldContain($"44444444-0000-0000-0000-00000000000{n}");
            schedule.ShouldContain($"66666666-0000-0000-0000-00000000000{n}");
        }

        events.ShouldContain("customer_credits");
        events.ShouldContain("VALUES (techsol, 600, 'BOB')"); // TechSol's 600 Bs of credit
    }

    [Fact]
    public void Migrations_AreInSyncWithTheModel_NoPendingChanges()
    {
        AssertNoPendingChanges(Create<QuotesDbContext>());
        AssertNoPendingChanges(Create<EventLifecycleDbContext>());
        AssertNoPendingChanges(Create<HallScheduleDbContext>());
    }

    private static void AssertNoPendingChanges(DbContext context)
    {
        using (context)
        {
            var assembly = context.GetService<IMigrationsAssembly>();
            var snapshot = assembly.ModelSnapshot?.Model;
            snapshot.ShouldNotBeNull($"{context.GetType().Name} has no model snapshot");

            if (snapshot is IMutableModel mutable)
                snapshot = mutable.FinalizeModel();
            snapshot = context.GetService<IModelRuntimeInitializer>().Initialize(snapshot);

            var differences = context.GetService<IMigrationsModelDiffer>().GetDifferences(
                snapshot.GetRelationalModel(),
                context.GetService<IDesignTimeModel>().Model.GetRelationalModel());

            differences.ShouldBeEmpty($"{context.GetType().Name}: the model changed without a migration");
        }
    }

    private static List<string> SqlOf(Migration migration) =>
        migration.UpOperations.OfType<SqlOperation>().Select(o => o.Sql).ToList();
}
