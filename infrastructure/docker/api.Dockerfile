# Build context is the repository root (see docker-compose.yml) because
# Bms.Api references sibling projects under src/backend/.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/backend/Bms.Domain/Bms.Domain.csproj src/backend/Bms.Domain/
COPY src/backend/Bms.Application/Bms.Application.csproj src/backend/Bms.Application/
COPY src/backend/Bms.Infrastructure/Bms.Infrastructure.csproj src/backend/Bms.Infrastructure/
COPY src/backend/Bms.Protocols/Bms.Protocols.csproj src/backend/Bms.Protocols/
COPY src/backend/Bms.Ingestion/Bms.Ingestion.csproj src/backend/Bms.Ingestion/
COPY src/backend/Bms.Alerting/Bms.Alerting.csproj src/backend/Bms.Alerting/
COPY src/backend/Bms.Calculations/Bms.Calculations.csproj src/backend/Bms.Calculations/
COPY src/backend/Bms.DeviceManagement/Bms.DeviceManagement.csproj src/backend/Bms.DeviceManagement/
COPY src/backend/Bms.Api/Bms.Api.csproj src/backend/Bms.Api/
RUN dotnet restore src/backend/Bms.Api/Bms.Api.csproj

COPY src/backend/ src/backend/
RUN dotnet publish src/backend/Bms.Api/Bms.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Bms.Api.dll"]
