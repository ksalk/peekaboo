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

FROM mcr.microsoft.com/dotnet/aspnet:10.0.15 AS runtime-base
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl ffmpeg libuuid1 \
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

# Optional private image with the proprietary Hikvision Linux SDK. The SDK is
# deliberately not part of the default image or the public repository. Bundled
# OpenSSL and OpenAL copies are moved out of the loader path so they cannot
# shadow the distro libraries for the rest of the process; libz.so is kept
# because it does not collide with the system's libz.so.1 soname.
FROM runtime-base AS runtime-sdk

COPY vendor/hikvision/ /app/hikvision/

USER root
RUN mkdir -p /app/hikvision/private \
    && mv /app/hikvision/libssl.so.3 \
          /app/hikvision/libcrypto.so.3 \
          /app/hikvision/libopenal.so.1 \
          /app/hikvision/private/
USER app

ENV PEEKABOO_HIK_SDK_PATH=/app/hikvision \
    LD_LIBRARY_PATH=/app/hikvision:/app/hikvision/HCNetSDKCom

# Keep the SDK-free image as the final stage so a no-target `docker build`
# remains safe for public clones.
FROM runtime-base AS runtime
