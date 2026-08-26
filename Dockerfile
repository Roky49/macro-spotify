FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY Api/Api.csproj Api/
RUN dotnet restore Api/Api.csproj
COPY . .
RUN dotnet publish Api/Api.csproj -o /app

FROM node:22-alpine AS ng-build
WORKDIR /ng
COPY frontend/ .
RUN npm install
RUN ./node_modules/.bin/ng build --configuration=production

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app

# yt-dlp + FFmpeg + Deno (JS runtime para extraer de YouTube) para las descargas.
# La Pi 5 es aarch64/ARM64, por lo que descargamos los binarios correctos según
# la arquitectura objetivo de Docker (x86_64 o arm64).
ARG TARGETARCH
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg ca-certificates curl unzip \
    && rm -rf /var/lib/apt/lists/* \
    && if [ "$TARGETARCH" = "arm64" ]; then \
         YTDLP_URL="https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_linux_aarch64"; \
         DENO_URL="https://github.com/denoland/deno/releases/latest/download/deno-aarch64-unknown-linux-gnu.zip"; \
       else \
         YTDLP_URL="https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_linux"; \
         DENO_URL="https://github.com/denoland/deno/releases/latest/download/deno-x86_64-unknown-linux-gnu.zip"; \
       fi \
    && curl -sL -o /usr/local/bin/yt-dlp "$YTDLP_URL" \
    && chmod +x /usr/local/bin/yt-dlp \
    && curl -sL -o /tmp/deno.zip "$DENO_URL" \
    && unzip -o -q /tmp/deno.zip -d /usr/local/bin/ \
    && rm -f /tmp/deno.zip \
    && chmod +x /usr/local/bin/deno \
    && yt-dlp --version \
    && deno --version \
    && mkdir -p /downloads

COPY --from=build /app .
COPY --from=ng-build /ng/dist /app/wwwroot
EXPOSE 8080
ENV ASPNETCORE_ENVIRONMENT=Production Render=true
ENTRYPOINT ["dotnet", "Api.dll"]
