# ── SDK stage ──
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore
COPY QuickShare.slnx ./
COPY src/QuickShare/QuickShare.csproj src/QuickShare/
COPY src/QuickShare.Querying/QuickShare.Querying.csproj src/QuickShare.Querying/
COPY src/QuickShare.Querying.Generators/QuickShare.Querying.Generators.csproj src/QuickShare.Querying.Generators/
# Runner network to nuget.org is slow/flaky — sequential restore + more retries.
# Parallel restore floods the pipe and trips "no data received for 60000ms" timeouts.
RUN dotnet restore src/QuickShare/QuickShare.csproj --disable-parallel -p:RestoreRetryCount=6

# Node.js for the TypeScript build (BuildTypeScript MSBuild target runs before publish)
RUN apt-get update && \
    apt-get install -y --no-install-recommends nodejs npm && \
    rm -rf /var/lib/apt/lists/*

# TypeScript toolchain — cached layer, only rebuilds when package files change
COPY src/QuickShare/package.json src/QuickShare/package-lock.json src/QuickShare/
RUN cd src/QuickShare && npm ci --no-audit --no-fund

# Build + Publish
COPY . .
RUN dotnet publish src/QuickShare/QuickShare.csproj -c Release -o /app --no-restore

# ── Runtime stage ──
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# AWSSDK (R2) needs the Kerberos GSSAPI native lib for SigV4a signing.
# tesseract-ocr powers image text search (issue #243) — the binary, not an interop package, so
# nothing in the build depends on it and a host without it simply never offers the feature.
# The language packs match what the product is localised in; adding one is an apt line.
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        libgssapi-krb5-2 \
        tesseract-ocr tesseract-ocr-eng tesseract-ocr-vie && \
    rm -rf /var/lib/apt/lists/*

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
