# Build context is the repository root — same reasoning as api.Dockerfile.
# The ingestion worker is a separate deployable from the API (see
# docs/architecture/system-architecture.md): it owns the MQTT/TCP
# listeners and the periodic sweeps, while the API owns HTTP ingestion,
# REST, and SignalR.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/backend/Bms.Domain/Bms.Domain.csproj src/backend/Bms.Domain/
COPY src/backend/Bms.Application/Bms.Application.csproj src/backend/Bms.Application/
COPY src/backend/Bms.Infrastructure/Bms.Infrastructure.csproj src/backend/Bms.Infrastructure/
COPY src/backend/Bms.Protocols/Bms.Protocols.csproj src/backend/Bms.Protocols/
COPY src/backend/Bms.Alerting/Bms.Alerting.csproj src/backend/Bms.Alerting/
COPY src/backend/Bms.Calculations/Bms.Calculations.csproj src/backend/Bms.Calculations/
COPY src/backend/Bms.DeviceManagement/Bms.DeviceManagement.csproj src/backend/Bms.DeviceManagement/
COPY src/backend/Bms.Ingestion/Bms.Ingestion.csproj src/backend/Bms.Ingestion/
RUN dotnet restore src/backend/Bms.Ingestion/Bms.Ingestion.csproj

COPY src/backend/ src/backend/
RUN dotnet publish src/backend/Bms.Ingestion/Bms.Ingestion.csproj -c Release -o /app/publish --no-restore -m:1 /p:UseSharedCompilation=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Bms.Ingestion.dll"]
