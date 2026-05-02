# syntax=docker/dockerfile:1.7

# ---- Build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore separately to maximize layer caching.
COPY CRM.sln ./
COPY src/CRM.Domain/CRM.Domain.csproj           src/CRM.Domain/
COPY src/CRM.Application/CRM.Application.csproj src/CRM.Application/
COPY src/CRM.Infrastructure/CRM.Infrastructure.csproj src/CRM.Infrastructure/
COPY src/CRM.Api/CRM.Api.csproj                 src/CRM.Api/
COPY tests/CRM.Domain.Tests/CRM.Domain.Tests.csproj           tests/CRM.Domain.Tests/
COPY tests/CRM.Application.Tests/CRM.Application.Tests.csproj tests/CRM.Application.Tests/
COPY tests/CRM.Api.IntegrationTests/CRM.Api.IntegrationTests.csproj tests/CRM.Api.IntegrationTests/
RUN dotnet restore src/CRM.Api/CRM.Api.csproj

COPY . .
RUN dotnet publish src/CRM.Api/CRM.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---- Runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Persist SQLite (when used) and any future file uploads under /data.
RUN mkdir -p /data && chown -R 1000:1000 /data /app
USER 1000

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080
ENTRYPOINT ["dotnet", "CRM.Api.dll"]
