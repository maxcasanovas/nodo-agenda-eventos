using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nodo.BuildingBlocks.Application;
using Nodo.BuildingBlocks.Infrastructure.Jobs;
using Nodo.BuildingBlocks.Infrastructure.Messaging;
using Nodo.BuildingBlocks.Infrastructure.Persistence;
using Nodo.Quotes.Application;
using Nodo.Quotes.Domain;
using Nodo.Quotes.Infrastructure.Persistence;

namespace Nodo.Quotes.Infrastructure;

internal static class ConstraintCodes
{
    public static readonly InvariantConstraintMap Map = InvariantConstraintMap.Of(
        ("INV-01", Domain.InvariantCodes.DurationOutOfRange),
        ("INV-04", Domain.InvariantCodes.AttendanceOutOfRange),
        ("INV-14", Domain.InvariantCodes.Expired),
        ("INV-17", Domain.InvariantCodes.InvalidDiscount));
}

internal sealed class QuotesUnitOfWork(QuotesDbContext context, IDomainEventDispatcher dispatcher)
    : EfUnitOfWork<QuotesDbContext>(context, dispatcher, ConstraintCodes.Map), IQuotesUnitOfWork;

internal sealed class QuotesOutbox(QuotesDbContext context, IntegrationEventSerializer serializer)
    : EfOutbox<QuotesDbContext>(context, serializer), IQuotesOutbox;

/// <summary>Marks overdue quotes as expired every 30 s (BR-09), using the demo-aware clock through the use case.</summary>
internal sealed class QuoteExpirationJob(IServiceScopeFactory scopeFactory, ILogger<QuoteExpirationJob> logger)
    : PeriodicJob(scopeFactory, logger)
{
    public override string Name => "QuoteExpirationJob";

    protected override async Task<int> RunAsync(IServiceProvider scopedServices, CancellationToken cancellationToken) =>
        await scopedServices.GetRequiredService<MediatR.IMediator>().Send(new ExpireDueQuotesCommand(), cancellationToken);
}

public static class DependencyInjection
{
    public static IServiceCollection AddQuotesInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<QuotesDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable("__ef_migrations_history", QuotesDbContext.Schema)
                .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddScoped<IQuoteQueries, QuoteQueries>();
        services.AddScoped<IQuotesUnitOfWork, QuotesUnitOfWork>();
        services.AddScoped<IQuotesOutbox, QuotesOutbox>();

        // Adapters towards the external contexts (anti-corruption at the border).
        services.AddScoped<ISpacesCatalog, SpacesCatalogAdapter>();
        services.AddScoped<IMembershipDirectory, MembershipDirectoryAdapter>();
        services.AddScoped<ICustomerDirectory, CustomerDirectoryAdapter>();

        services.AddHostedService<OutboxProcessor<QuotesDbContext>>();
        services.AddSingleton<QuoteExpirationJob>();
        services.AddSingleton<IRunnableJob>(sp => sp.GetRequiredService<QuoteExpirationJob>());
        services.AddHostedService(sp => sp.GetRequiredService<QuoteExpirationJob>());
        return services;
    }
}
