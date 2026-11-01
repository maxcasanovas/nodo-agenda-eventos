using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Nodo.BuildingBlocks.Application;
using Nodo.BuildingBlocks.Infrastructure.Messaging;

namespace Nodo.BuildingBlocks.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>The pieces shared by all modules' messaging: event serializer, dispatcher, in-memory bus and event log.</summary>
    /// <param name="contractAssemblies">The <c>*.Contracts</c> assemblies, to find the integration event types.</param>
    /// <param name="moduleSchemas">Database schemas whose outbox tables feed the event log.</param>
    public static IServiceCollection AddMessagingInfrastructure(
        this IServiceCollection services,
        string connectionString,
        IEnumerable<Assembly> contractAssemblies,
        IReadOnlyCollection<string> moduleSchemas)
    {
        services.AddSingleton(new IntegrationEventSerializer(contractAssemblies));
        services.AddScoped<IDomainEventDispatcher, MediatRDomainEventDispatcher>();
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton(new IntegrationEventLog(connectionString, moduleSchemas));
        return services;
    }
}
