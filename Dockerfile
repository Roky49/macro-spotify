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

# yt-dlp + FFmpeg + Deno (JS runtime para extraer de YouTube) para las descargas
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg ca-certificates curl unzip \
    && rm -rf /var/lib/apt/lists/* \
    && curl -sL -o /usr/local/bin/yt-dlp https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_linux \
    && chmod +x /usr/local/bin/yt-dlp \
    && curl -sL -o /tmp/deno.zip https://github.com/denoland/deno/releases/latest/download/deno-x86_64-unknown-linux-gnu.zip \
    && unzip -o -q /tmp/deno.zip -d /usr/local/bin/ \
    && rm -f /tmp/deno.zip \
    && chmod +x /usr/local/bin/deno \
    && mkdir -p /downloads

COPY --from=build /app .
COPY --from=ng-build /ng/dist /app/wwwroot
EXPOSE 8080
ENV ASPNETCORE_ENVIRONMENT=Production Render=true
ENTRYPOINT ["dotnet", "Api.dll"]
