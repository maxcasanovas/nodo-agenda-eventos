using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Nodo.BuildingBlocks.Application;
using Nodo.BuildingBlocks.Infrastructure.Messaging;

namespace Nodo.Infrastructure.UnitTests;

public sealed record SampleEvent(Guid Id, DateTime OccurredOnUtc) : IIntegrationEvent;

/// <summary>What the database remembers: registrations that were committed. Shared by every scope.</summary>
internal sealed class CommittedRegistrations
{
    public HashSet<(Guid, string)> Rows { get; } = [];
}

/// <summary>
/// Stand-in for a module's DbContext as the inbox and unit of work see it: a registration is only a pending row until
/// the unit of work commits, and a scope that is dropped without committing loses it (like a disposed DbContext).
/// </summary>
internal sealed class FakeInboxStorage(CommittedRegistrations committed) : IInbox, IUnitOfWork
{
    private readonly HashSet<(Guid, string)> _pending = [];
    private bool _touched;

    public FakeInboxStorage() : this(new CommittedRegistrations())
    {
    }

    public int Commits { get; private set; }

    public bool HasPendingChanges => _pending.Count > 0 || _touched;

    public Task<bool> TryRegisterAsync(Guid messageId, string consumer, CancellationToken cancellationToken = default)
    {
        var key = (messageId, consumer);
        if (committed.Rows.Contains(key) || _pending.Contains(key))
            return Task.FromResult(false);

        _pending.Add(key);
        return Task.FromResult(true);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        Commits++;
        committed.Rows.UnionWith(_pending);
        _pending.Clear();
        _touched = false;
        return Task.CompletedTask;
    }

    public void Touch() => _touched = true; // a handler that changed something
}
public class InboxDecoratorTests
{
    private static SampleEvent NewEvent() => new(Guid.NewGuid(), new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc));

    [Fact]
    public async Task Handle_FirstDelivery_RunsTheHandlerAndRegistersTheMessage()
    {
        var storage = new FakeInboxStorage();
        var inner = Substitute.For<IIntegrationEventHandler<SampleEvent>>();
        var decorator = new InboxIntegrationEventHandler<SampleEvent>(inner, storage, storage, "SampleHandler");
        var message = NewEvent();

        await decorator.HandleAsync(message);

        await inner.Received(1).HandleAsync(message, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SecondDeliveryOfTheSameMessage_DoesNotInvokeTheHandler()
    {
        var storage = new FakeInboxStorage();
        var inner = Substitute.For<IIntegrationEventHandler<SampleEvent>>();
        var decorator = new InboxIntegrationEventHandler<SampleEvent>(inner, storage, storage, "SampleHandler");
        var message = NewEvent();

        await decorator.HandleAsync(message);
        await decorator.HandleAsync(message);

        await inner.Received(1).HandleAsync(Arg.Any<SampleEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SameMessageForTwoDifferentConsumers_IsDeliveredToBoth()
    {
        var storage = new FakeInboxStorage();
        var first = Substitute.For<IIntegrationEventHandler<SampleEvent>>();
        var second = Substitute.For<IIntegrationEventHandler<SampleEvent>>();
        var message = NewEvent();

        await new InboxIntegrationEventHandler<SampleEvent>(first, storage, storage, "A").HandleAsync(message);
        await new InboxIntegrationEventHandler<SampleEvent>(second, storage, storage, "B").HandleAsync(message);

        await first.Received(1).HandleAsync(message, Arg.Any<CancellationToken>());
        await second.Received(1).HandleAsync(message, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_HandlerThatDoesNotSave_StillCommitsTheInboxRegistration()
    {
        var storage = new FakeInboxStorage();
        var inner = Substitute.For<IIntegrationEventHandler<SampleEvent>>(); // saves nothing
        var decorator = new InboxIntegrationEventHandler<SampleEvent>(inner, storage, storage, "SampleHandler");

        await decorator.HandleAsync(NewEvent());

        storage.Commits.ShouldBe(1);
        storage.HasPendingChanges.ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_HandlerThatAlreadySaved_DoesNotCommitAgain()
    {
        var storage = new FakeInboxStorage();
        var inner = Substitute.For<IIntegrationEventHandler<SampleEvent>>();
        inner.HandleAsync(Arg.Any<SampleEvent>(), Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            storage.Touch();
            await storage.SaveChangesAsync(); // the handler's own unit of work commits the inbox row with its work
        });
        var decorator = new InboxIntegrationEventHandler<SampleEvent>(inner, storage, storage, "SampleHandler");

        await decorator.HandleAsync(NewEvent());

        storage.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_HandlerThatFails_PropagatesAndNeverCommitsSoTheMessageCanBeRetried()
    {
        var storage = new FakeInboxStorage();
        var inner = Substitute.For<IIntegrationEventHandler<SampleEvent>>();
        inner.HandleAsync(Arg.Any<SampleEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("boom")));
        var decorator = new InboxIntegrationEventHandler<SampleEvent>(inner, storage, storage, "SampleHandler");

        await Should.ThrowAsync<InvalidOperationException>(() => decorator.HandleAsync(NewEvent()));

        storage.Commits.ShouldBe(0); // the registration stays unsaved, so redelivery is processed again
    }
}

public class InMemoryEventBusTests
{
    public sealed class ScopeProbe
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    public sealed class Recorder
    {
        public List<string> Calls { get; } = [];

        public List<Guid> ScopeIds { get; } = [];
    }

    public sealed class FirstHandler(Recorder recorder, ScopeProbe probe) : IIntegrationEventHandler<SampleEvent>
    {
        public Task HandleAsync(SampleEvent integrationEvent, CancellationToken cancellationToken = default)
        {
            recorder.Calls.Add(nameof(FirstHandler));
            recorder.ScopeIds.Add(probe.Id);
            return Task.CompletedTask;
        }
    }

    public sealed class SecondHandler(Recorder recorder, ScopeProbe probe) : IIntegrationEventHandler<SampleEvent>
    {
        public Task HandleAsync(SampleEvent integrationEvent, CancellationToken cancellationToken = default)
        {
            recorder.Calls.Add(nameof(SecondHandler));
            recorder.ScopeIds.Add(probe.Id);
            return Task.CompletedTask;
        }
    }

    public sealed class FailingHandler : IIntegrationEventHandler<SampleEvent>
    {
        public Task HandleAsync(SampleEvent integrationEvent, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("handler exploded");
    }

    private static (IEventBus Bus, Recorder Recorder, CommittedRegistrations Committed) Build(bool withFailingHandler = false, bool withInbox = true)
    {
        var committed = new CommittedRegistrations();
        var services = new ServiceCollection();
        services.AddScoped(_ => new FakeInboxStorage(committed));
        services.AddSingleton<Recorder>();
        services.AddScoped<ScopeProbe>();
        services.AddScoped<IIntegrationEventHandler<SampleEvent>, FirstHandler>();
        services.AddScoped<IIntegrationEventHandler<SampleEvent>, SecondHandler>();
        if (withFailingHandler)
            services.AddScoped<IIntegrationEventHandler<SampleEvent>, FailingHandler>();
        if (withInbox)
        {
            services.AddSingleton(new ModuleMessaging(
                typeof(InMemoryEventBusTests).Assembly,
                sp => sp.GetRequiredService<FakeInboxStorage>(),
                sp => sp.GetRequiredService<FakeInboxStorage>()));
        }

        services.AddSingleton<IEventBus, InMemoryEventBus>();
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IEventBus>(), provider.GetRequiredService<Recorder>(), committed);
    }

    [Fact]
    public async Task Publish_DeliversToEveryRegisteredHandler()
    {
        var (bus, recorder, _) = Build();

        await bus.PublishAsync(new SampleEvent(Guid.NewGuid(), new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc)));

        recorder.Calls.ShouldBe([nameof(FirstHandler), nameof(SecondHandler)], ignoreOrder: true);
    }

    [Fact]
    public async Task Publish_RunsEachHandlerInItsOwnScope()
    {
        var (bus, recorder, _) = Build();

        await bus.PublishAsync(new SampleEvent(Guid.NewGuid(), new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc)));

        recorder.ScopeIds.Distinct().Count().ShouldBe(2); // separate DbContexts and transactions per handler
    }

    [Fact]
    public async Task Publish_SameMessageTwice_EachHandlerRunsOnce_ThroughTheInboxDecorator()
    {
        var (bus, recorder, _) = Build();
        var message = new SampleEvent(Guid.NewGuid(), new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc));

        await bus.PublishAsync(message);
        await bus.PublishAsync(message); // at-least-once delivery from the outbox

        recorder.Calls.Count(c => c == nameof(FirstHandler)).ShouldBe(1);
        recorder.Calls.Count(c => c == nameof(SecondHandler)).ShouldBe(1);
    }

    [Fact]
    public async Task Publish_WithoutModuleMessaging_HandlersRunUndecorated()
    {
        var (bus, recorder, _) = Build(withInbox: false);
        var message = new SampleEvent(Guid.NewGuid(), new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc));

        await bus.PublishAsync(message);
        await bus.PublishAsync(message);

        recorder.Calls.Count.ShouldBe(4);
    }

    [Fact]
    public async Task Publish_OneHandlerFails_OthersStillRun_AndTheFailureIsReportedSoTheMessageIsRetried()
    {
        var (bus, recorder, _) = Build(withFailingHandler: true);

        var ex = await Should.ThrowAsync<AggregateException>(
            () => bus.PublishAsync(new SampleEvent(Guid.NewGuid(), new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc))));

        ex.InnerExceptions.Single().Message.ShouldContain("FailingHandler");
        recorder.Calls.ShouldBe([nameof(FirstHandler), nameof(SecondHandler)], ignoreOrder: true);
    }

    [Fact]
    public async Task Publish_Retry_SkipsHandlersThatAlreadySucceeded()
    {
        var (bus, recorder, committed) = Build(withFailingHandler: true);
        var message = new SampleEvent(Guid.NewGuid(), new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc));

        await Should.ThrowAsync<AggregateException>(() => bus.PublishAsync(message));
        await Should.ThrowAsync<AggregateException>(() => bus.PublishAsync(message));

        recorder.Calls.Count.ShouldBe(2); // First and Second ran once; only the failing handler is attempted again
        committed.Rows.Select(r => r.Item2).ShouldBe([nameof(FirstHandler), nameof(SecondHandler)], ignoreOrder: true);
        // The failing handler left nothing committed, so the next retry will run it again.
    }

    [Fact]
    public async Task Publish_EventWithoutHandlers_IsANoOp()
    {
        var (bus, recorder, _) = Build();

        await bus.PublishAsync(new OtherEvent(Guid.NewGuid(), new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc)));

        recorder.Calls.ShouldBeEmpty();
    }

    private sealed record OtherEvent(Guid Id, DateTime OccurredOnUtc) : IIntegrationEvent;
}
