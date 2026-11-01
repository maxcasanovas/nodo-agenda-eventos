using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Nodo.BuildingBlocks.Application;

namespace Nodo.BuildingBlocks.Infrastructure.Messaging;

/// <summary>
/// Tells the bus how a module deduplicates: which inbox and unit of work belong to the handlers of that module's
/// Application assembly. Registered by each module's Infrastructure.
/// </summary>
public sealed record ModuleMessaging(
    Assembly ApplicationAssembly,
    Func<IServiceProvider, IInbox> ResolveInbox,
    Func<IServiceProvider, IUnitOfWork> ResolveUnitOfWork);

/// <summary>
/// In-process stand-in for a message broker. Every handler runs in its own DI scope (its own DbContexts and
/// transaction) and is wrapped by the inbox decorator of its module.
/// </summary>
public sealed class InMemoryEventBus(IServiceScopeFactory scopeFactory, IEnumerable<ModuleMessaging> modules) : IEventBus
{
    private readonly IReadOnlyCollection<ModuleMessaging> _modules = modules.ToList();

    public async Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var eventType = integrationEvent.GetType();
        var handlerInterface = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);

        List<Type> handlerTypes;
        using (var probe = scopeFactory.CreateScope())
            handlerTypes = probe.ServiceProvider.GetServices(handlerInterface).Select(h => h!.GetType()).ToList();

        var failures = new List<Exception>();
        foreach (var handlerType in handlerTypes)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var handler = scope.ServiceProvider.GetServices(handlerInterface).First(h => h!.GetType() == handlerType)!;
                await InvokeAsync(scope.ServiceProvider, handler, handlerType, eventType, handlerInterface, integrationEvent, cancellationToken);
            }
            catch (Exception ex)
            {
                // Keep delivering to the other handlers; the message is retried and finished handlers are skipped by the inbox.
                failures.Add(new InvalidOperationException($"{handlerType.Name} failed on {eventType.Name}: {ex.Message}", ex));
            }
        }

        if (failures.Count > 0)
            throw new AggregateException($"Delivery of {eventType.Name} failed.", failures);
    }

    private Task InvokeAsync(
        IServiceProvider scopedServices, object handler, Type handlerType, Type eventType, Type handlerInterface,
        IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var module = _modules.FirstOrDefault(m => m.ApplicationAssembly == handlerType.Assembly);
        if (module is not null)
        {
            var decoratorType = typeof(InboxIntegrationEventHandler<>).MakeGenericType(eventType);
            handler = Activator.CreateInstance(
                decoratorType, handler, module.ResolveInbox(scopedServices), module.ResolveUnitOfWork(scopedServices),
                handlerType.Name)!;
            handlerInterface = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
        }

        var method = handlerInterface.GetMethod(nameof(IIntegrationEventHandler<IIntegrationEvent>.HandleAsync))!;
        return (Task)method.Invoke(handler, [integrationEvent, cancellationToken])!;
    }
}
