# The browser scripts are TypeScript. tsc compiles them into wwwroot before the
# .NET publish copies that directory into the image.
FROM --platform=$BUILDPLATFORM node:24-alpine AS web
WORKDIR /web
COPY package.json package-lock.json tsconfig.json ./
RUN npm ci
COPY src/ src/
RUN npx tsc

# Alpine (musl) images, matching the RuntimeIdentifiers pinned in MeshMuster.csproj.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG TARGETARCH
WORKDIR /src

COPY MeshMuster/MeshMuster.csproj MeshMuster/
RUN dotnet restore MeshMuster/MeshMuster.csproj -a "$TARGETARCH"

COPY MeshMuster/ MeshMuster/
COPY --from=web /web/MeshMuster/wwwroot/ MeshMuster/wwwroot/
RUN dotnet publish MeshMuster/MeshMuster.csproj \
        -a "$TARGETARCH" \
        -c Release \
        --no-restore \
        --self-contained false \
        -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

ENV STATE_PATH=/state
ENV LISTEN_ADDR=:8080
EXPOSE 8080

RUN addgroup -S meshmuster && adduser -S -G meshmuster meshmuster \
    && mkdir -p /state && chown -R meshmuster:meshmuster /state \
    && apk add --no-cache sqlite
USER meshmuster
VOLUME /state

COPY --from=build /app .

HEALTHCHECK --interval=30s --timeout=3s --start-period=10s \
    CMD wget -qO- http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["dotnet", "MeshMuster.dll"]
