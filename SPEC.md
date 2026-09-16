# SPEC.md — macro-spotify

> Gestión y automatización de Spotify: librería, playlists, descargas y stats con streaming en tiempo real. Stack: .NET 9 + PostgreSQL central (192.168.1.180:5432) + EF Core + JWT + Angular 18 standalone + Docker linux/arm64 (Raspberry Pi 5).

## 1) Descripción y objetivos
Macro-Spotify es un orquestador local para la biblioteca de Spotify: sincroniza tracks/playlists, gestiona descargas en cola, expone stats y notifica progreso en tiempo real por SignalR. Objetivo: centralizar la gestión musical con API propia, cola desduplicada, almacenamiento desacoplado y frontend Angular ligero, persistiendo todo en PG central en vez de memoria/fs disperso. Desplegable en Pi con Docker, con auth JWT y healthchecks.

## 2) Alcance y requisitos
**Funcionales:**
- Integración Spotify: OAuth / token (config por env), listado de playlists, tracks de librería, búsqueda.
- Playlists: CRUD local + sync con Spotify (`/api/playlists`, `/api/spotify`).
- Librería: `LibraryController` con paginación y búsqueda.
- Descargas: `DownloadController`/`DownloadsController` con cola (`DownloadQueue`), deduplicación, estado (queued/downloading/done/failed), endpoint remoto (`remote.html`).
- Sync y Stats: `SyncController` para sincronización manual/programada, `StatsController` para métricas.
- Tiempo real: `Hubs/SpotifyHub` (SignalR) para progreso de cola y notificaciones.
- Auth JWT (alineado a stack) aunque el dominio sea interno; opcional para exponer fuera.

**No funcionales:**
- `dotnet build` + `ng build` OK; `dotnet test` (DownloadQueueTests, DownloadDedupTests → ampliar).
- PG central Host=192.168.1.180:5432 SslMode=Disable como fuente de verdad (EF Core 9 + Npgsql 9.0.4).
- JWT HMAC-SHA256, CORS configurable, logging estructurado, OpenAPI en dev.
- Docker linux/arm64, healthchecks, restart unless-stopped, imágenes multi-stage.
- Manejo de rate-limit de Spotify, reintentos, concurrencia de cola.

## 3) Arquitectura acordada
Capas modulares:
`Api/Controllers` (Spotify, Playlists, Library, Downloads, Health, Stats, Sync) → `Application/Services` (SpotifyService, DownloadQueue) → `Domain/Models` → `Infrastructure/Data` (AppDbContext PG).
Transversal: Hubs/SignalR, validación, middleware de errores, Swagger.
Frontend Angular 18 standalone: `core/` (auth, spotify.service, signalr.service, interceptors), `features/` (library, playlists, downloads/queue, stats), `shared/` (track-card, queue-table), `environments/`.
Flujo: Frontend → REST + SignalR ↔ Backend → Spotify API (HttpClient con token) + DownloadQueue (in-memory con persistencia PG si aplica) → PG central.
Auth: JWT Bearer para APIs propias; tokens Spotify separados (no exponer).

## 4) Modelo de datos
EF Core + Npgsql en PG central. Aunque el estado actual es in-memory/fs, el modelo canónico es:

```csharp
User { Id Guid PK, Email string unique, SpotifyUserId string?, PasswordHash string?, Role string, CreatedAt DateTimeOffset }
Playlist { Id Guid PK, SpotifyId string unique, Name string, OwnerId Guid FK?, TrackCount int, SyncedAt DateTimeOffset, SnapshotId string? }
Track { Id Guid PK, SpotifyId string unique, Title string, Artist string, Album string?, DurationMs int, AddedAt DateTimeOffset }
PlaylistTrack { PlaylistId Guid FK, TrackId Guid FK, Position int, PK(PlaylistId,TrackId) }
DownloadJob { Id Guid PK, TrackId Guid FK, SpotifyId string, Url string?, Status string (Queued/Downloading/Done/Failed), Progress int, Error string?, CreatedAt DateTimeOffset, StartedAt DateTimeOffset?, FinishedAt DateTimeOffset? }
DownloadQueueState { Id Guid PK, IsPaused bool, Concurrency int }
SyncLog { Id Guid PK, Type string, StartedAt DateTimeOffset, FinishedAt DateTimeOffset?, ItemsProcessed int, Status string }
```

Índices: SpotifyId únicos, (TrackId, Status), CreatedAt. Migración: si hoy no hay PG, Fase 1 crea DB `macro_spotify` en 192.168.1.180 y aplica migraciones.

## 5) Contratos y endpoints API
Base `/api`, SignalR en `/hubs/spotify`. JWT en `Authorization: Bearer`.

- `GET /health` / `GET /api/health` → 200 {status,version,pg,spotify}
- `POST /api/auth/register` / `POST /api/auth/login` → JWT (nuevo para unificar stack)
- `GET /api/spotify/me` → perfil Spotify (requiere token Spotify)
- `GET /api/spotify/search?q=&type=track,playlist` → 200 {items}
- `GET /api/library?query=&page=&pageSize=` → 200 {items,total}
- `GET /api/playlists` → 200 [{id,spotifyId,name,trackCount}]
- `GET /api/playlists/{id}/tracks` → 200 {tracks}
- `POST /api/playlists {name, trackIds}` → 201 (crea local y opcional Spotify)
- `GET /api/downloads` → cola completa
- `POST /api/downloads {spotifyId, url}` → 202 {jobId} / 409 si duplicado (deduplicación)
- `DELETE /api/downloads/{id}` → cancela
- `POST /api/sync` → dispara sincronización
- `GET /api/stats` → {totalTracks, totalPlaylists, queueStats, syncStatus}
- `Hub SpotifyHub`: eventos `queueUpdated`, `downloadProgress {jobId,percent}`, `syncCompleted`

Códigos: 400 validación, 401 JWT, 409 duplicado, 429 rate-limit Spotify mapeado a 429.

## 6) Páginas y flujos frontend
Angular 18 standalone, lazy, OnPush, RxJS, SignalR client.

- `/login` (público) → JWT → guarda token
- `/` o `/library` (protegida) → búsqueda + paginación de tracks, player preview
- `/playlists` → listado playlists, detalle `/playlists/:id` con tracks
- `/downloads` → tabla de cola con estados, barra progreso (SignalR), acciones pausar/cancelar/reintentar
- `/stats` → dashboard métricas (total tracks, downloads done/failed, sync logs)
- `/sync` o botón global Sync → dispara POST /api/sync → notificación hub
- Flujo descarga: usuario pega URL Spotify → POST /api/downloads → backend deduplica (DownloadDedupTests) → encola → hub emite progreso → frontend actualiza fila.
- Proxy dev: `proxy.conf.json` → `http://localhost:5000`; prod Nginx fallback SPA + `/hubs` websockets upgrade.

## 7) Estructura de carpetas
```
macro-spotify/
├── Api/                         # .NET 9 API
│   ├── Controllers/  (Spotify, Playlists, Library, Download, Downloads, Stats, Sync, Health)
│   ├── Hubs/  (SpotifyHub.cs)
│   ├── Services/  (SpotifyService, DownloadQueue, SyncService)
│   ├── Domain/Models/  (Playlist, Track, DownloadJob, SyncLog) — crear
│   ├── Infrastructure/Data/  (AppDbContext PG) — crear
│   ├── Properties/launchSettings.json
│   ├── appsettings.json / appsettings.Development.json
│   ├── Program.cs
│   └── Api.csproj  (añadir Npgsql.EntityFrameworkCore.PostgreSQL, JWT, SignalR ya presente)
├── Api.Tests/  (DownloadQueueTests, DownloadDedupTests + nuevos)
├── frontend/                    # Angular 18 standalone (actual src/app/components/spotify → expandir)
│   ├── src/app/core/  (auth, spotify.service, signalr.service, guards, interceptors)
│   ├── src/app/features/  (library, playlists, downloads, stats)
│   ├── src/app/shared/  (track-card, etc.)
│   ├── src/environments/  (apiUrl, hubUrl)
│   └── angular.json / proxy.conf.json
├── docker-compose.yml
├── Dockerfile  (backend)  → evolucionar a Dockerfile.backend multi-stage
├── Dockerfile.frontend  (crear) + nginx.conf
├── .env.example
└── SPEC.md
```

## 8) Decisiones técnicas
- PG central: `Host=192.168.1.180;Port=5432;Database=macro_spotify;Username=postgres;Password=${POSTGRES_PASSWORD};SslMode=Disable`; via `ConnectionStrings__DefaultConnection` y `DATABASE_URL`; EFCore 9 + Npgsql 9.0.4; migraciones versionadas, `EnsureCreated()` solo dev si no hay migraciones.
- JWT: HMAC-SHA256 24h, issuer/audience = MacroSpotify, validación completa; Spotify tokens separados en `SpotifyService` (no JWT).
- DownloadQueue: deduplicación por SpotifyId (ya testeada en DownloadDedupTests), concurrency configurable, retry con backoff, estado persistido en PG para sobrevivir reinicios Pi.
- SignalR: transporte WebSockets, reconexión automática, auth opcional por query token.
- Docker: multi-stage `mcr.microsoft.com/dotnet/sdk:9.0` → `mcr.microsoft.com/dotnet/aspnet:9.0` (arm64 multi-arch), backend 5000, frontend Nginx 80 con `proxy_set_header Upgrade` para hubs.
- CORS: orígenes `http://localhost:4200,http://localhost:80` configurables.
- Observabilidad: `/health` verifica PG y Spotify reachability (timeout corto); logs con correlación jobId.

## 9) Plan de implementación por fases
1. **Fase 0 — Espec y env:** SPEC.md (hecho), `.env.example` con DATABASE_URL, Spotify ClientId/Secret, JWT.
2. **Fase 1 — PG y auth:** Crear DB macro_spotify en 192.168.1.180, AppDbContext, migraciones, JWT endpoints.
3. **Fase 2 — Modelos y cola persistida:** Entidades Playlist/Track/DownloadJob, migrar DownloadQueue a PG, tests de persistencia.
4. **Fase 3 — Servicios Spotify:** Robustecer SpotifyService (refresh, rate-limit), SyncController, StatsController.
5. **Fase 4 — Frontend Angular:** Migrar spotify.component.ts a features completas, integrar SignalR, guards/interceptores, proxy.
6. **Fase 5 — Tests:** xUnit (≥3 por controller: ok/401/404-409) + hub tests + Jasmine/Karma para frontend.
7. **Fase 6 — DevOps Pi:** Dockerfiles multi-stage arm64, compose con PG central, healthchecks, `docker compose up -d --build`.

## 10) Criterios de aceptación
- `dotnet build` y `dotnet test` verdes (incluye DownloadQueueTests); `ng build` OK.
- `docker compose config` válido; `curl http://localhost:5000/health` → 200 con `pg:Healthy`.
- Cola: POST duplicado → 409, POST válido → 202 y hub emite `queueUpdated`; reinicio backend mantiene cola (persistida en PG).
- Sync: POST /api/sync sincroniza playlists y refleja en GET /api/library.
- SignalR: cliente Angular recibe `downloadProgress` sin polling.
- No secretos en repo; Authorization exige JWT; Spotify secrets solo env.
