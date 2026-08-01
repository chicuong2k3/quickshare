# ── SDK stage ──
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore
COPY QuickShare.slnx ./
COPY src/QuickShare/QuickShare.csproj src/QuickShare/
COPY src/QuickShare.Querying/QuickShare.Querying.csproj src/QuickShare.Querying/
COPY src/QuickShare.Querying.Generators/QuickShare.Querying.Generators.csproj src/QuickShare.Querying.Generators/
RUN dotnet restore src/QuickShare/QuickShare.csproj

# Build + Publish
COPY . .
RUN dotnet publish src/QuickShare/QuickShare.csproj -c Release -o /app --no-restore

# ── Runtime stage ──
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Create non-root user
RUN useradd --create-home --user-group appuser && \
    mkdir -p /app/keys && chown -R appuser:appuser /app

# DataProtection keys persisted to volume
VOLUME ["/app/keys"]

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DataProtection__KeyPath=/app/keys

USER appuser
EXPOSE 8080

ENTRYPOINT ["dotnet", "QuickShare.dll"]
