# Build context: repository root (docker-compose.yml sets context: .)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore layer: only project files and build props, so it is cached until a dependency changes.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/BuildingBlocks/Nodo.SharedKernel/Nodo.SharedKernel.csproj src/BuildingBlocks/Nodo.SharedKernel/
COPY src/BuildingBlocks/Nodo.BuildingBlocks.Application/Nodo.BuildingBlocks.Application.csproj src/BuildingBlocks/Nodo.BuildingBlocks.Application/
COPY src/BuildingBlocks/Nodo.BuildingBlocks.Infrastructure/Nodo.BuildingBlocks.Infrastructure.csproj src/BuildingBlocks/Nodo.BuildingBlocks.Infrastructure/
COPY src/External/Nodo.External.Simulated/Nodo.External.Simulated.csproj src/External/Nodo.External.Simulated/
COPY src/Modules/Quotes/Nodo.Quotes.Domain/Nodo.Quotes.Domain.csproj src/Modules/Quotes/Nodo.Quotes.Domain/
COPY src/Modules/Quotes/Nodo.Quotes.Application/Nodo.Quotes.Application.csproj src/Modules/Quotes/Nodo.Quotes.Application/
COPY src/Modules/Quotes/Nodo.Quotes.Infrastructure/Nodo.Quotes.Infrastructure.csproj src/Modules/Quotes/Nodo.Quotes.Infrastructure/
COPY src/Modules/Quotes/Nodo.Quotes.Contracts/Nodo.Quotes.Contracts.csproj src/Modules/Quotes/Nodo.Quotes.Contracts/
COPY src/Modules/EventLifecycle/Nodo.EventLifecycle.Domain/Nodo.EventLifecycle.Domain.csproj src/Modules/EventLifecycle/Nodo.EventLifecycle.Domain/
COPY src/Modules/EventLifecycle/Nodo.EventLifecycle.Application/Nodo.EventLifecycle.Application.csproj src/Modules/EventLifecycle/Nodo.EventLifecycle.Application/
COPY src/Modules/EventLifecycle/Nodo.EventLifecycle.Infrastructure/Nodo.EventLifecycle.Infrastructure.csproj src/Modules/EventLifecycle/Nodo.EventLifecycle.Infrastructure/
COPY src/Modules/EventLifecycle/Nodo.EventLifecycle.Contracts/Nodo.EventLifecycle.Contracts.csproj src/Modules/EventLifecycle/Nodo.EventLifecycle.Contracts/
COPY src/Modules/HallSchedule/Nodo.HallSchedule.Domain/Nodo.HallSchedule.Domain.csproj src/Modules/HallSchedule/Nodo.HallSchedule.Domain/
COPY src/Modules/HallSchedule/Nodo.HallSchedule.Application/Nodo.HallSchedule.Application.csproj src/Modules/HallSchedule/Nodo.HallSchedule.Application/
COPY src/Modules/HallSchedule/Nodo.HallSchedule.Infrastructure/Nodo.HallSchedule.Infrastructure.csproj src/Modules/HallSchedule/Nodo.HallSchedule.Infrastructure/
COPY src/Modules/HallSchedule/Nodo.HallSchedule.Contracts/Nodo.HallSchedule.Contracts.csproj src/Modules/HallSchedule/Nodo.HallSchedule.Contracts/
COPY src/Nodo.Api/Nodo.Api.csproj src/Nodo.Api/
RUN dotnet restore src/Nodo.Api/Nodo.Api.csproj

COPY src/ src/
RUN dotnet publish src/Nodo.Api/Nodo.Api.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# curl is only needed by the compose healthcheck.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Nodo.Api.dll"]
