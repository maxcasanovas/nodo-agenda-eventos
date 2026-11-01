using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Nodo.Quotes.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers validators and the application services. MediatR handlers are registered by the host from this
    /// assembly; ports (repositories, unit of work, outbox, external adapters) are registered by Infrastructure.
    /// </summary>
    public static IServiceCollection AddQuotesApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(AssemblyMarker).Assembly, includeInternalTypes: true);
        services.AddScoped<QuoteDrafter>();
        return services;
    }
}
