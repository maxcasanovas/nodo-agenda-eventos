using Microsoft.Extensions.DependencyInjection;
using Nodo.BuildingBlocks.Application;
using Nodo.External.Simulated.Downstream;
using Nodo.External.Simulated.Handlers;
using Nodo.External.Simulated.Memberships;
using Nodo.External.Simulated.Payments;
using Nodo.External.Simulated.Spaces;

namespace Nodo.External.Simulated;

public static class DependencyInjection
{
    /// <summary>
    /// The seven simulated contexts (in-memory singletons) and their downstream handlers. Notifications register one
    /// handler per integration event contract found in <paramref name="contractAssemblies"/>.
    /// </summary>
    public static IServiceCollection AddExternalSimulated(
        this IServiceCollection services, params System.Reflection.Assembly[] contractAssemblies)
    {
        services.AddSingleton<SpacesContext>();
        services.AddSingleton<MembershipsContext>();
        services.AddSingleton<PaymentGatewayContext>();
        services.AddSingleton<BillingContext>();
        services.AddSingleton<AccessControlContext>();
        services.AddSingleton<NotificationsContext>();
        services.AddSingleton<CateringContext>();
        services.AddSingleton<EventDirectory>();
        services.AddSingleton<ProcessedMessages>();
        services.AddSingleton<NotificationFormatter>();

        services.AddIntegrationEventHandlers(typeof(AssemblyMarker).Assembly);

        var contracts = contractAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsAssignableTo(typeof(IIntegrationEvent)));
        foreach (var contract in contracts)
        {
            services.AddScoped(
                typeof(IIntegrationEventHandler<>).MakeGenericType(contract),
                typeof(NotificationHandler<>).MakeGenericType(contract));
        }

        return services;
    }
}
