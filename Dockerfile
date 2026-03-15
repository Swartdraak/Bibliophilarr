FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY . .

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl xz-utils ca-certificates \
    && curl -fsSL https://nodejs.org/dist/v20.19.2/node-v20.19.2-linux-x64.tar.xz -o /tmp/node.tar.xz \
    && tar -xJf /tmp/node.tar.xz -C /usr/local --strip-components=1 \
    && rm -f /tmp/node.tar.xz \
    && rm -rf /var/lib/apt/lists/*

RUN npm install -g yarn@1.22.19

RUN ./build.sh --backend -r linux-x64 -f net8.0
RUN ./build.sh --frontend
RUN ./build.sh --packages -r linux-x64 -f net8.0

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    ASPNETCORE_URLS=http://+:8787

COPY --from=build /src/_artifacts/linux-x64/net8.0/Readarr/ /app/

EXPOSE 8787

ENTRYPOINT ["./Readarr"]
