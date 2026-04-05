FROM mcr.microsoft.com/dotnet/sdk:8.0@sha256:b2fbc92fd05f5238358b3c38a33b8dbb44522446db85aa3b5f68bf69368be410 AS build
WORKDIR /src

COPY . .

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl xz-utils ca-certificates \
    && curl -fsSL https://nodejs.org/dist/v20.19.2/node-v20.19.2-linux-x64.tar.xz -o /tmp/node.tar.xz \
    && echo "cbe59620b21732313774df4428586f7222a84af29e556f848abf624ba41caf90  /tmp/node.tar.xz" | sha256sum -c - \
    && tar -xJf /tmp/node.tar.xz -C /usr/local --strip-components=1 \
    && rm -f /tmp/node.tar.xz \
    && rm -rf /var/lib/apt/lists/*

RUN npm install -g yarn@1.22.19

RUN ./build.sh --backend -r linux-x64 -f net8.0
RUN ./build.sh --frontend
RUN ./build.sh --packages -r linux-x64 -f net8.0

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:ccdca44cd4f256d50187f920dc8ccc2a9ea7a8a4597ac1d51e08fddb2e3b3205 AS runtime

LABEL org.opencontainers.image.title="Bibliophilarr" \
      org.opencontainers.image.description="Ebook and audiobook library manager" \
      org.opencontainers.image.url="https://github.com/Swartdraak/Bibliophilarr" \
      org.opencontainers.image.source="https://github.com/Swartdraak/Bibliophilarr" \
      org.opencontainers.image.licenses="GPL-3.0-only"

RUN groupadd --gid 1000 bibliophilarr \
    && useradd --uid 1000 --gid bibliophilarr --shell /bin/false --create-home bibliophilarr \
    && apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    ASPNETCORE_URLS=http://+:8787

COPY --from=build --chown=bibliophilarr:bibliophilarr /src/_artifacts/linux-x64/net8.0/Bibliophilarr/ /app/

USER bibliophilarr

EXPOSE 8787

HEALTHCHECK --interval=30s --timeout=10s --start-period=15s --retries=3 \
    CMD curl -f http://localhost:8787/ping || exit 1

ENTRYPOINT ["sh", "-c", "umask 077 && exec ./Bibliophilarr \"$@\"", "--"]
