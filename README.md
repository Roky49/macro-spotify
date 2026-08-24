# 🎵 Macro Spotify

> Descarga masiva de playlists y canciones de **YouTube, Spotify, SoundCloud, Bandcamp e Internet Archive** — **sin repetir canciones** — con reproductor integrado, filtros y metadatos automáticos.

**Stack:** .NET 9 + Angular · **Despliegue:** Docker · **Licencia:** MIT

---

## ✨ Funcionalidades

- **Descarga multi-plataforma** con yt-dlp: YouTube, Spotify (vía YouTube), SoundCloud, Bandcamp, Internet Archive. Formatos: MP3, M4A, Opus, FLAC, WAV.
- **No repite canciones** — `--download-archive` (memoria persistente de lo ya descargado).
- **No repite playlists** ya descargadas — registro de fuentes; si pegas una URL repetida, la omite y avisa (con opción "Re-descargar").
- **Reproductor integrado**: play/pausa, anterior/siguiente, **shuffle (aleatorio)**, **repeat**, volumen, barra de tiempo con seek, autonext.
- **Pantalla de inicio** con buscador de la carpeta de música + **filtros por género y artista**.
- **Metadatos automáticos**: al descargar rellena título/artista/álbum (MusicBrainz) y **género real** (iTunes Search); botón "🔍 info" para completar archivos sin autor.
- **Barra de progreso** de descargas (async + polling): "X de Y · N quedan".
- **Elegir carpeta** de guardado desde el dispositivo (compatible con todos los navegadores).
- Cola de descargas con estados (en-cola/procesando/completado/fallo).

## 🚀 Ejecutar

```bash
docker compose up --build
# UI: http://localhost:5480
# Swagger: http://localhost:5480/swagger
```

O en desarrollo:

```bash
cd Api && dotnet run          # backend en :8080
cd frontend && npm i && ng serve   # frontend en :4200
```

## 📁 Estructura

```
macro-spotify/
├── Api/               ← Backend .NET 9 + Swagger
│   ├── Controllers/   ← Downloads, Library, Spotify, Sync, Stats...
│   ├── Services/      ← DownloadQueue, SpotifyService
│   └── Program.cs
├── Api.Tests/         ← Tests (cola de descargas + dedupe)
├── frontend/          ← Angular (pantalla de inicio, reproductor, descargas)
├── Dockerfile
├── docker-compose.yml  → :5480
└── LICENSE            ← MIT
```

## 🔌 API destacada

| Endpoint | Descripción |
|---|---|
| `POST /api/download/async` | Lanza una descarga en background (URL o playlist de Spotify) |
| `GET /api/download/progress/{id}` | Progreso: total / done / failed / skipped |
| `GET /api/download/files` | Lista la música de la carpeta con metadatos (título, artista, género) |
| `POST /api/download/enrich` | Busca y añade metadatos/ género a un archivo |
| `GET /api/download/audio?path=` | Sirve el archivo de audio (seek/range) para el reproductor |
| `GET /api/library` | Biblioteca de lo descargado |

## ⚖️ Licencia

**MIT** — libre para que cualquiera lo use, modifique, copie y distribuya. Ver [`LICENSE`](LICENSE).
