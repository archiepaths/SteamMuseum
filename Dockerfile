# Build from the API repository root: docker build -t steam-museum-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY global.json Directory.Build.props NuGet.Config ./
COPY src/SteamMuseum.Domain/SteamMuseum.Domain.csproj src/SteamMuseum.Domain/
COPY src/SteamMuseum.Application/SteamMuseum.Application.csproj src/SteamMuseum.Application/
COPY src/SteamMuseum.Infrastructure/SteamMuseum.Infrastructure.csproj src/SteamMuseum.Infrastructure/
COPY src/SteamMuseum.SqlServerMigrations/SteamMuseum.SqlServerMigrations.csproj src/SteamMuseum.SqlServerMigrations/
COPY src/SteamMuseum.Api/SteamMuseum.Api.csproj src/SteamMuseum.Api/
RUN dotnet restore src/SteamMuseum.Api/SteamMuseum.Api.csproj
COPY src/ ./src/
RUN dotnet publish src/SteamMuseum.Api/SteamMuseum.Api.csproj --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Database__Provider=SqlServer \
    DataProtection__KeysPath=/var/steam-museum/keys
EXPOSE 8080
RUN mkdir -p /var/steam-museum/keys && chown -R app:app /var/steam-museum
COPY --from=build /app/publish ./
USER app
ENTRYPOINT ["dotnet", "SteamMuseum.Api.dll"]
