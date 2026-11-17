# syntax=docker/dockerfile:1

FROM node:26.12.1-alpine AS frontend
WORKDIR /src/frontend

COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci

COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0.101 AS backend
WORKDIR /src

COPY global.json Directory.Build.props ./
COPY backend/Peekaboo.Api/Peekaboo.Api.csproj backend/Peekaboo.Api/
RUN dotnet restore backend/Peekaboo.Api/Peekaboo.Api.csproj

COPY backend/Peekaboo.Api/ backend/Peekaboo.Api/
COPY --from=frontend /src/frontend/dist/ backend/Peekaboo.Api/wwwroot/
RUN dotnet publish backend/Peekaboo.Api/Peekaboo.Api.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0.15 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl ffmpeg \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data \
    && chown app:app /data

COPY --from=backend /app/publish/ ./

USER app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    PEEKABOO_DB_PATH=/data/peekaboo.db

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD ["curl", "-fsS", "http://127.0.0.1:8080/api/health"]

ENTRYPOINT ["dotnet", "Peekaboo.Api.dll"]
