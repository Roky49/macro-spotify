# 🎵 Macro Spotify

> **P R O Y E C T O   C E R R A D O** ✅ — Descarga masiva de playlists y canciones de Spotify y YouTube (más SoundCloud, Bandcamp, Internet Archive) **sin repetir canciones**, con reproductor integrado, filtros y metadatos automáticos.

**Stack:** .NET 9 + Angular
**GitHub:** https://github.com/Roky49/macro-spotify
**Licencia:** MIT (libre para cualquiera)
**Estado:** ✅ **Terminado / cerrado**

---

## 📊 Ficha técnica

| Campo | Valor |
|-------|-------|
| **Stack** | .NET 9 + Angular |
| **Despliegue** | Docker |
| **API** | .NET 9 + Swagger |
| **Licencia** | MIT |
| **Tests** | 8/8 |
| **Historial git** | Unificado en 1 commit |

## 🎯 Funcionalidades

- **Descarga multi-plataforma** con yt-dlp: YouTube, **Spotify (vía YouTube, sin DRM)**, SoundCloud, Bandcamp, Internet Archive. Formatos MP3/M4A/Opus/FLAC/WAV.
- **No repite canciones** — `--download-archive` + `--no-overwrites`.
- **No repite playlists** ya descargadas — registro persistente de fuentes; botón "Re-descargar".
- **Reproductor integrado**: play/pausa, anterior/siguiente, **shuffle**, **repeat**, volumen, seek, autonext, barra fija inferior.
- **Pantalla de inicio** con buscador de la carpeta de música + **filtros por género y artista**.
- **Metadatos automáticos**: MusicBrainz (título/artista/álbum) + **género real** vía iTunes Search (descarta "Music" genérico); botón "🔍 info" para completar archivos sin autor.
- **Barra de progreso** de descargas (async + polling): "X de Y · N quedan".
- **Elegir carpeta de guardado** compatible con todos los navegadores (fallback `webkitdirectory`).
- Badge "✅ API conectada" en la cabecera.
- Cola de descargas con estados (Queued/Processing/Completed/Failed).

## 🚀 Ejecutar

```bash
docker compose up --build
# UI: http://localhost:5480
# Swagger: http://localhost:5480/swagger
```

> **Raspberry Pi 5 / ARM64:** El Dockerfile usa `TARGETARCH` para bajar `yt-dlp_linux_aarch64` y `deno-aarch64-unknown-linux-gnu.zip`. En la Pi 5 ejecuta `docker compose up --build` con BuildKit activo (por defecto en Docker 23+). Si has forzado una imagen x86_64 previamente, limpia con `docker compose down --rmi all` antes del build.

## 📁 Estructura

```
macro-spotify/
├── Api/              ← Backend .NET 9
│   ├── Controllers/  ← Download, Library, Spotify, Sync, Stats, Health
│   ├── Services/     ← DownloadQueue, SpotifyService
│   └── Program.cs
├── Api.Tests/        ← cola + dedupe (8 tests)
├── frontend/         ← Angular (inicio/reproductor/descargas)
├── Dockerfile
├── docker-compose.yml  → :5480
├── README.md
└── LICENSE           ← MIT
```

---

## ✅ Estado — [Verificado en vivo 2026-08-24]

- ✅ **Spotify vía YouTube sin DRM**: detecta URL de Spotify, extrae las pistas del embed público (título+artista) y las descarga automáticamente buscándolas en YouTube. Playlist real probada: 86 pistas, repeticiones omitidas.
- ✅ **Deduplicación de canciones**: `--download-archive` + `--no-overwrites`.
- ✅ **No repite playlists**: registro `.spotify-macro-sources.json`; normaliza URL (quita `?si`, utm).
- ✅ **Reproductor**: streaming de audio con range/seek (`GET /api/download/audio`), + shuffle/repeat.
- ✅ **Filtros por género/artista**: el backend lee `genre` de los tags; al listar descarta "Music" genérico.
- ✅ **Género real**: iTunes Search al descargar/enriquecer (probado: "Losing It" → *Dance*, "It's That Time" → *House*).
- ✅ **Elegir carpeta en todos los navegadores**: Chrome/Edge usan `showDirectoryPicker`; Firefox/Safari/Opera caen al selector clásico.
- ✅ **Barra de progreso**: `POST /api/download/async` + polling de `GET /api/download/progress/{id}`.
- ✅ **Inicio con búsqueda automática** de la carpeta guardada (misma que Descargas).
- ✅ **API conectada** (badge verde) + pantalla de inicio sin la grid/credenciales.
- ✅ **Licencia MIT** y **README** actualizado.
- ✅ **Historial git unificado** en 1 commit + push forzado.
- ✅ Backend 0 errores, **8/8 tests**, contenedor `Up`.
