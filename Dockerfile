# syntax=docker/dockerfile:1
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0@sha256:bef136a371963aaa46ec97882845ef6f54dec15728bb4b128b1c2a400ffbc9b4 AS build
WORKDIR /src
ARG BUILDARCH
ARG TARGETARCH

RUN set -eu; \
    case "$BUILDARCH" in \
      amd64) node_arch=x64; node_sha=88fd1ce767091fd8d4a99fdb2356e98c819f93f3b1f8663853a2dee9b438068a ;; \
      arm64) node_arch=arm64; node_sha=e9e1930fd321a470e29bb68f30318bf58e3ecb4acb4f1533fb19c58328a091fe ;; \
      *) echo "Unsupported build architecture: $BUILDARCH" >&2; exit 1 ;; \
    esac; \
    apt-get update; \
    apt-get install -y --no-install-recommends curl xz-utils ca-certificates; \
    curl -fsSL "https://nodejs.org/dist/v22.22.2/node-v22.22.2-linux-$node_arch.tar.xz" -o /tmp/node.tar.xz; \
    printf '%s  /tmp/node.tar.xz\n' "$node_sha" | sha256sum -c -; \
    tar -xJf /tmp/node.tar.xz -C /usr/local --strip-components=1; \
    rm -f /tmp/node.tar.xz; \
    rm -rf /var/lib/apt/lists/*; \
    npm install -g yarn@1.22.19

COPY . .
RUN ./build.sh --frontend

ARG BIBLIOPHILARRVERSION
RUN --mount=type=secret,id=sixlabors_license,required=true \
    set -eu; \
    case "$TARGETARCH" in \
      amd64) rid=linux-x64 ;; \
      arm64) rid=linux-arm64 ;; \
      *) echo "Unsupported target architecture: $TARGETARCH" >&2; exit 1 ;; \
    esac; \
    SIXLABORS_LICENSE_FILE=/run/secrets/sixlabors_license ./build.sh --backend -r "$rid" -f net10.0; \
    ./build.sh --packages -r "$rid" -f net10.0; \
    mv "/src/_artifacts/$rid/net10.0/Bibliophilarr" /release

FROM mcr.microsoft.com/dotnet/aspnet:11.0@sha256:bc0d6653979efba491ab3d6f589d691f96c309c3c3ef81325efa6978e7b7f242 AS runtime

LABEL org.opencontainers.image.title="Bibliophilarr" \
      org.opencontainers.image.description="Ebook and audiobook library manager" \
      org.opencontainers.image.url="https://github.com/Swartdraak/Bibliophilarr" \
      org.opencontainers.image.source="https://github.com/Swartdraak/Bibliophilarr" \
      org.opencontainers.image.licenses="GPL-3.0-only" \
      org.opencontainers.image.vendor="Bibliophilarr"

RUN set -eu; \
    if ! getent group 1000 >/dev/null; then groupadd --gid 1000 bibliophilarr; fi; \
    if ! getent passwd 1000 >/dev/null; then \
      useradd --uid 1000 --gid 1000 --shell /bin/false --create-home bibliophilarr; \
    fi; \
    install -d -o 1000 -g 1000 /config; \
    apt-get update; \
    apt-get install -y --no-install-recommends curl; \
    rm -rf /var/lib/apt/lists/*

WORKDIR /app

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    ASPNETCORE_URLS=http://+:8787

COPY --from=build --chown=1000:1000 /release/ /app/

USER 1000:1000

EXPOSE 8787

HEALTHCHECK --interval=30s --timeout=10s --start-period=15s --retries=3 \
    CMD curl -f http://localhost:8787/ping || exit 1

ENTRYPOINT ["sh", "-c", "umask 077 && exec ./Bibliophilarr \"$@\"", "--"]
CMD ["/data=/config"]
