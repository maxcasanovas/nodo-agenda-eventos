using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Nodo.BuildingBlocks.Application;
using Nodo.HallSchedule.Contracts;

namespace Nodo.HallSchedule.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers validators, integration event handlers and the module API. MediatR handlers are registered by the
    /// host from this assembly; ports (repositories, unit of work, outbox, catalogs) are registered by Infrastructure.
    /// </summary>
    public static IServiceCollection AddHallScheduleApplication(this IServiceCollection services)
    {
        var assembly = typeof(AssemblyMarker).Assembly;
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        services.AddIntegrationEventHandlers(assembly);
        services.AddScoped<SlotReleaser>();
        services.AddScoped<IHallScheduleModuleApi, HallScheduleModuleApi>();
        return services;
    }
}
