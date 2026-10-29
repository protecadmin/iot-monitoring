# Build context is the repository root — same reasoning as api.Dockerfile.
# The Connector Service is its own deployable: it listens on the connector
# port range for devices that send their own format, and publishes their
# readings to the broker. It has no database; it reads connectors and
# devices from the API.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/backend/Bms.Domain/Bms.Domain.csproj src/backend/Bms.Domain/
COPY src/backend/Bms.Connectors.Core/Bms.Connectors.Core.csproj src/backend/Bms.Connectors.Core/
COPY src/backend/Bms.Connectors/Bms.Connectors.csproj src/backend/Bms.Connectors/
RUN dotnet restore src/backend/Bms.Connectors/Bms.Connectors.csproj

COPY src/backend/ src/backend/
RUN dotnet publish src/backend/Bms.Connectors/Bms.Connectors.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
# Not root: binding ports above 1024 needs no privilege.
USER app
ENTRYPOINT ["dotnet", "Bms.Connectors.dll"]
