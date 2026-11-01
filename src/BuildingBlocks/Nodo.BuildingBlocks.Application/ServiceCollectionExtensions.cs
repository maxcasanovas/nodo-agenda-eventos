using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Nodo.BuildingBlocks.Application;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers every <see cref="IIntegrationEventHandler{TEvent}"/> of the assembly (scoped), for the event bus to resolve.</summary>
    public static IServiceCollection AddIntegrationEventHandlers(this IServiceCollection services, Assembly assembly)
    {
        var handlerInterface = typeof(IIntegrationEventHandler<>);
        var registrations = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false })
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == handlerInterface)
                .Select(i => (Service: i, Implementation: t)));

        foreach (var (service, implementation) in registrations)
            services.AddScoped(service, implementation);

        return services;
    }
}
