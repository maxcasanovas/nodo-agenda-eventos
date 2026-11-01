using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Nodo.BuildingBlocks.Application;
using Nodo.EventLifecycle.Contracts;

namespace Nodo.EventLifecycle.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers validators, integration event handlers and the module API. MediatR handlers are registered by the
    /// host from this assembly; ports (repositories, unit of work, outbox, inbox, adapters) are registered by Infrastructure.
    /// </summary>
    public static IServiceCollection AddEventLifecycleApplication(this IServiceCollection services)
    {
        var assembly = typeof(AssemblyMarker).Assembly;
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        services.AddIntegrationEventHandlers(assembly);
        services.AddScoped<CustomerCreditAccessor>();
        services.AddScoped<IEventLifecycleModuleApi, EventLifecycleModuleApi>();
        return services;
    }
}
