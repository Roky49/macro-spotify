using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DownloadController : ControllerBase
{
    private static string? _downloadDir;
    private static readonly string SettingsPath = Path.Combine(AppContext.BaseDirectory, "dirsettings.json");

    // Archivo de archivo de descargas (download-archive): memoria persistente
    // de lo que yt-dlp ya ha descargado. Al procesar una playlist re-descarga
    // UNICAMENTE lo que no esté en este archivo -> las canciones repetidas se
    // omiten automáticamente, en cualquier ejecución.
    private static string ArchiveFile
        => Path.Combine(DownloadDir, ".spotify-macro-archive.txt");

    // Registro persistente de las fuentes (URLs de playlist/vídeo) que el
    // usuario ya ha pedido descargar. Si repite la misma URL, se avisa y se
    // omite (salvo que pida forzar con reDownload=true).
    private static string SourcesFile
        => Path.Combine(DownloadDir, ".spotify-macro-sources.json");

    // Directorio de descargas (persistente, elegible desde la UI):
    //  1. el valor que eligió el usuario (dirsettings.json) — manda siempre
    //  2. $DOWNLOAD_DIR (Docker lo mapea a ./downloads del host) como valor inicial
    //  3. por defecto UserProfile/Music/SpotifyMacro
    private static string DownloadDir
    {
        get
        {
            if (_downloadDir != null) return _downloadDir;
            try
            {
                if (System.IO.File.Exists(SettingsPath))
                {
                    using var j = JsonDocument.Parse(System.IO.File.ReadAllText(SettingsPath));
                    var dir = j.RootElement.TryGetProperty("dir", out var p) ? p.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(dir)) return _downloadDir = Path.GetFullPath(dir);
                }
            }
            catch { }
            var env = Environment.GetEnvironmentVariable("DOWNLOAD_DIR");
            if (!string.IsNullOrWhiteSpace(env)) return _downloadDir = env;
            return _downloadDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Music", "SpotifyMacro");
        }
        set => _downloadDir = value;
    }

    public DownloadController() => Directory.CreateDirectory(DownloadDir);

    [HttpPost("set-dir")]
    public IActionResult SetDirectory([FromBody] SetDirRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Path))
            return BadRequest(new { error = "Ruta requerida" });
        try
        {
            // Ruta absoluta -> se usa tal cual. Ruta relativa (subcarpeta elegida
            // desde el selector del navegador) -> se resuelve bajo el directorio
            // base de descargas (que en Docker es /downloads, montado en ./downloads).
            var full = Path.IsPathRooted(req.Path)
                ? Path.GetFullPath(req.Path)
                : Path.Combine(DownloadDir, req.Path);
            Directory.CreateDirectory(full);
            DownloadDir = full;
            System.IO.File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new { dir = DownloadDir }));
            return Ok(new { downloadDir = DownloadDir });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = $"No se pudo usar esa ruta: {ex.Message}" });
        }
    }

    [HttpGet("dir")]
    public IActionResult GetDirectory() => Ok(new { downloadDir = DownloadDir });

    [HttpPost]
    public async Task<IActionResult> Download([FromBody] DownloadRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Url))
            return BadRequest(new { error = "URL requerida" });

        // Deduplicación a nivel de fuente: si esta URL ya se descargó antes,
        // y el usuario no pide forzarla, la omitimos y avisamos.
        if (req.ReDownload != true && IsSourceDownloaded(req.Url))
        {
            return Ok(new
            {
                skipped = true,
                reason = "ya-descargada",
                message = "Esta playlist/URL ya se descargó antes. Se omite para no repetir. Usa re-descargar si quieres bajarla otra vez.",
                url = req.Url
            });
        }

        // Spotify: yt-dlp no puede bajar de open.spotify.com (DRM, solo metadatos).
        // La descarga real se hace extrayendo las pistas de la playlist (embed
        // público) y buscando/descargando cada una en YouTube con yt-dlp.
        if (IsSpotifyUrl(req.Url))
            return await DownloadSpotifyAsync(req);

        var outputTemplate = Path.Combine(DownloadDir, "%(title)s.%(ext)s");

        // Hora de modificación más reciente antes de empezar, para detectar el
        // archivo creado/sobrescrito por esta descarga (funciona aunque ya exista).
        var beforeMaxTime = Directory.GetFiles(DownloadDir)
            .Select(System.IO.File.GetLastWriteTimeUtc)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "yt-dlp",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            // ArgumentList escapa las comillas de forma segura en Windows y Linux.
            psi.ArgumentList.Add("-x"); // extract audio
            psi.ArgumentList.Add("--audio-format");
            psi.ArgumentList.Add(req.Format ?? "mp3");
            psi.ArgumentList.Add("--audio-quality");
            psi.ArgumentList.Add(req.Quality ?? "0"); // 0 = mejor
            // YouTube bloquea el cliente web por defecto con HTTP 403 (anti-bot).
            psi.ArgumentList.Add("--extractor-args"); psi.ArgumentList.Add("youtube:player_client=android");
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add(outputTemplate);
            // Sin --no-playlist: si la URL es de una playlist, se descargan todas las canciones.
            // Deduplicación: --download-archive guarda el id de cada canción ya bajada y
            // --no-overwrites evita re-bajar las que ya existen en disco. Las repetidas se omiten.
            psi.ArgumentList.Add("--download-archive");
            psi.ArgumentList.Add(ArchiveFile);
            psi.ArgumentList.Add("--no-overwrites");
            // Metadatos automáticos en el MP3 (ID3): título, artista, álbum, etc.
            psi.ArgumentList.Add("--embed-metadata");
            psi.ArgumentList.Add("--parse-metadata");
            psi.ArgumentList.Add("%(artist,uploader)s:%(artist)s"); // intérpretes
            psi.ArgumentList.Add("--parse-metadata");
            psi.ArgumentList.Add("%(album,playlist_title)s:%(album)s"); // álbum (o nombre de playlist)
            psi.ArgumentList.Add("--parse-metadata");
            psi.ArgumentList.Add("%(uploader)s:%(album_artist)s"); // artista del álbum
            psi.ArgumentList.Add("--parse-metadata");
            psi.ArgumentList.Add("%(playlist_index,0)s:%(track_number)s"); // nº de pista en la playlist
            psi.ArgumentList.Add(req.Url);

            var process = new Process { StartInfo = psi };
            process.Start();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                return BadRequest(new
                {
                    success = false,
                    error = string.IsNullOrWhiteSpace(error) ? "yt-dlp falló sin dar detalles." : error.Trim()
                });
            }

            // Todos los archivos creados/sobrescritos durante esta descarga (playlist = varios).
            var downloaded = Directory.GetFiles(DownloadDir)
                .Where(f => System.IO.File.GetLastWriteTimeUtc(f) > beforeMaxTime)
                .OrderBy(System.IO.File.GetLastWriteTimeUtc)
                .Select(f => new
                {
                    filePath = f,
                    fileSize = new FileInfo(f).Length
                })
                .ToList();

            if (downloaded.Count == 0)
            {
                return BadRequest(new { success = false, error = "No se pudo descargar el audio (no se generó ningún archivo)." });
            }

            // Enriquecer metadatos desde internet (MusicBrainz) cuando falten.
            foreach (var d in downloaded)
                await EnrichFileAsync(d.filePath);

            // Registrar cada archivo en la biblioteca.
            var libraryIds = new List<string>();
            foreach (var d in downloaded)
            {
                var libEntry = new LibraryEntry
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = Path.GetFileNameWithoutExtension(d.filePath),
                    FilePath = d.filePath,
                    Format = req.Format ?? "mp3",
                    SourceUrl = req.Url,
                    DownloadedAt = DateTime.UtcNow,
                    FileSize = new FileInfo(d.filePath).Length
                };
                LibraryController.AddEntry(libEntry);
                libraryIds.Add(libEntry.Id);
            }

            var newest = downloaded[^1]; // el último descargado

            // Registrar esta fuente como ya descargada (para no repetir la playlist).
            MarkSourceDownloaded(req.Url);

            return Ok(new
            {
                success = true,
                count = downloaded.Count,
                fileName = newest.filePath,
                fileSize = new FileInfo(newest.filePath).Length,
                files = downloaded.Select(d => new { path = d.filePath, fileSize = new FileInfo(d.filePath).Length }).ToArray(),
                libraryIds,
                error = (string?)null
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, error = $"yt-dlp no encontrado: {ex.Message}. Instálalo con: winget install yt-dlp" });
        }
    }

    [HttpGet("formats")]
    public IActionResult Formats() => Ok(new[]
    {
        new { value = "mp3", label = "MP3 (128k)" },
        new { value = "m4a", label = "M4A (AAC)" },
        new { value = "opus", label = "Opus (mejor calidad)" },
        new { value = "flac", label = "FLAC (lossless)" },
        new { value = "wav", label = "WAV (sin compresión)" }
    });

    [HttpGet("status")]
    public IActionResult Status()
    {
        var ytDlpInstalled = false;
        try
        {
            var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "yt-dlp",
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                }
            };
            p.Start();
            var version = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(2000);
            ytDlpInstalled = p.ExitCode == 0 && !string.IsNullOrEmpty(version);
        }
        catch { }

        return Ok(new
        {
            ytDlpInstalled,
            downloadDir = DownloadDir,
            freeSpace = new DriveInfo(Path.GetPathRoot(DownloadDir)!).AvailableFreeSpace
        });
    }

    // ------------------------------------------------------------------
    // Descarga de playlists/alistas de SPOTIFY vía YouTube.
    // yt-dlp no puede bajar de open.spotify.com (DRM). La lista de pistas se
    // extrae del embed público (título + artistas) y cada canción se busca y
    // descarga automáticamente en YouTube con yt-dlp (ytsearch1).
    // ------------------------------------------------------------------
    static bool IsSpotifyUrl(string url)
        => url.Contains("open.spotify.com", StringComparison.OrdinalIgnoreCase)
           || url.Contains("spotify:playlist", StringComparison.OrdinalIgnoreCase);

    async Task<IActionResult> DownloadSpotifyAsync(DownloadRequest req)
    {
        var playlistId = ExtractSpotifyPlaylistId(req.Url);
        var tracks = await FetchSpotifyTracksAsync(playlistId);

        if (tracks.Count == 0)
            return BadRequest(new { success = false, error = "No se pudieron obtener las pistas de la playlist de Spotify (¿es pública?)." });

        var downloaded = new List<(string? path, long size)>();
        var skipped = 0;
        var failed = new List<string>();

        foreach (var t in tracks)
        {
            // Búsqueda "artista - título" en YouTube, mejor resultado.
            var query = string.IsNullOrWhiteSpace(t.Artist)
                ? t.Title
                : $"{t.Artist} - {t.Title}";
            var res = await DownloadYouTubeSearchAsync(query, req.Format ?? "mp3", req.Quality ?? "0");
            if (res.Success) downloaded.Add(res.File);
            else if (res.Skipped) skipped++;
            else failed.Add(res.Query);
        }

        MarkSourceDownloaded(req.Url);

        if (downloaded.Count == 0 && failed.Count > 0)
            return BadRequest(new { success = false, error = $"No se pudo descargar nada de YouTube: {string.Join("; ", failed.Take(3))}" });

        return Ok(new
        {
            success = true,
            count = downloaded.Count,
            skipped,
            failed = failed.ToArray(),
            files = downloaded.Select(d => new { path = d.path, fileSize = d.size }).ToArray(),
            source = "spotify-via-youtube",
            playlistId
        });
    }

    // Extrae el id de una URL o URI de Spotify. Soporta:
    //   https://open.spotify.com/playlist/ID
    //   https://open.spotify.com/playlist/ID?si=...
    //   spotify:playlist:ID
    static string ExtractSpotifyPlaylistId(string url)
    {
        if (url.StartsWith("spotify:playlist:"))
            return url.Split(':')[^1].Trim();
        var m = System.Text.RegularExpressions.Regex.Match(url, @"playlist/([A-Za-z0-9]+)");
        return m.Success ? m.Groups[1].Value : "";
    }

    // Descarga la lista de pistas desde el embed público de Spotify
    // (no requiere credenciales) parseando el JSON incrustado.
    async Task<List<(string Title, string Artist)>> FetchSpotifyTracksAsync(string playlistId)
    {
        var result = new List<(string, string)>();
        if (string.IsNullOrEmpty(playlistId)) return result;
        try
        {
            var embedUrl = $"https://open.spotify.com/embed/playlist/{playlistId}";
            using var resp = await MbClient.GetAsync(embedUrl); // reutiliza el HttpClient con UA
            if (!resp.IsSuccessStatusCode) return result;
            var html = await resp.Content.ReadAsStringAsync();

            var titles = System.Text.RegularExpressions.Regex.Matches(html, "\"title\":\"(.*?)\"");
            var artists = System.Text.RegularExpressions.Regex.Matches(html, "\"subtitle\":\"(.*?)\"");
            // El embed incluye el NOMBRE de la playlist como primera entrada
            // ("title"+"subtitle"). Lo detectamos para no tratar la playlist como pista.
            var playlistName = System.Text.RegularExpressions.Regex.Match(html, "\"name\":\"(.*?)\"")
                .Groups[1].Value.Replace("\\\"", "\"").Trim();
            for (int i = 0; i < titles.Count && i < 200; i++)
            {
                var title = System.Net.WebUtility.HtmlDecode(titles[i].Groups[1].Value).Trim();
                var artist = i < artists.Count ? System.Net.WebUtility.HtmlDecode(artists[i].Groups[1].Value).Trim() : "";
                // Quitar comillas escapadas del JSON.
                title = title.Replace("\\\"", "\"");
                artist = artist.Replace("\\\"", "\"");
                // Saltarse la fila del nombre de la playlist (su título == el nombre).
                if (string.IsNullOrWhiteSpace(title) || title.Equals(playlistName, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add((title, artist));
            }
        }
        catch { }
        return result;
    }

    // Descarga UNA canción buscándola en YouTube (ytsearch1). Devuelve si se
    // descargó, si se omitió (repetida) o si falló. Reutiliza la misma mecánica
    // de deduplicación (download-archive) y registro en biblioteca del método padre.
    async Task<(bool Success, bool Skipped, (string? path, long size) File, string Query, string? Error)>
        DownloadYouTubeSearchAsync(string query, string format, string quality)
    {
        var outputTemplate = Path.Combine(DownloadDir, "%(title)s.%(ext)s");
        var beforeMaxTime = Directory.GetFiles(DownloadDir)
            .Select(System.IO.File.GetLastWriteTimeUtc)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "yt-dlp",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("-x");
            psi.ArgumentList.Add("--audio-format"); psi.ArgumentList.Add(format);
            psi.ArgumentList.Add("--audio-quality"); psi.ArgumentList.Add(quality);
            // YouTube bloquea el cliente web por defecto con HTTP 403 (anti-bot).
            // player_client=android evita el bloqueo y permite descargar.
            psi.ArgumentList.Add("--extractor-args"); psi.ArgumentList.Add("youtube:player_client=android");
            psi.ArgumentList.Add("-o"); psi.ArgumentList.Add(outputTemplate);
            psi.ArgumentList.Add("--download-archive"); psi.ArgumentList.Add(ArchiveFile);
            psi.ArgumentList.Add("--no-overwrites");
            psi.ArgumentList.Add("--embed-metadata");
            psi.ArgumentList.Add("--default-search"); psi.ArgumentList.Add("ytsearch1");
            psi.ArgumentList.Add(query);

            var process = new Process { StartInfo = psi };
            process.Start();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
                return (false, false, (null, 0), query, error.Trim());
        }
        catch (Exception ex)
        {
            return (false, false, (null, 0), query, ex.Message);
        }

        // Archivo nuevo generado (si lo hubo). Si no, es que ya existía (omitido).
        var newFiles = Directory.GetFiles(DownloadDir)
            .Where(f => System.IO.File.GetLastWriteTimeUtc(f) > beforeMaxTime)
            .OrderBy(System.IO.File.GetLastWriteTimeUtc)
            .Select(f => (f, new FileInfo(f).Length))
            .ToList();

        if (newFiles.Count == 0)
            return (false, true, (null, 0), query, null); // omitido (repetida)

        await EnrichFileAsync(newFiles[0].f);

        var libEntry = new LibraryEntry
        {
            Id = Guid.NewGuid().ToString(),
            Title = Path.GetFileNameWithoutExtension(newFiles[0].f),
            FilePath = newFiles[0].f,
            Format = format,
            SourceUrl = query,
            DownloadedAt = DateTime.UtcNow,
            FileSize = newFiles[0].Length
        };
        LibraryController.AddEntry(libEntry);

        return (true, false, (newFiles[0].f, newFiles[0].Length), query, null);
    }

    // ------------------------------------------------------------------
    // Registro persistente de fuentes ya descargadas (playlists/URLs).
    // Evita bajar dos veces la misma playlist aunque las canciones estén
    // en archivos distintos del álbum/colección.
    // ------------------------------------------------------------------
    private static readonly object SourcesLock = new();

    private static List<string> LoadSources()
    {
        try
        {
            if (System.IO.File.Exists(SourcesFile))
            {
                var json = System.IO.File.ReadAllText(SourcesFile);
                return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new();
            }
        }
        catch { }
        return new List<string>();
    }

    static bool IsSourceDownloaded(string url)
    {
        lock (SourcesLock)
        {
            var key = NormalizeUrl(url);
            return LoadSources().Contains(key, StringComparer.OrdinalIgnoreCase);
        }
    }

    static void MarkSourceDownloaded(string url)
    {
        lock (SourcesLock)
        {
            var list = LoadSources();
            var key = NormalizeUrl(url);
            if (list.Contains(key, StringComparer.OrdinalIgnoreCase)) return;
            list.Add(key);
            try
            {
                Directory.CreateDirectory(DownloadDir);
                System.IO.File.WriteAllText(SourcesFile,
                    System.Text.Json.JsonSerializer.Serialize(list, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }

    // Normaliza la URL: quita parámetros de seguimiento/orden para que la misma
    // playlist (p.ej. con ?si=... de share) cuente como la misma fuente.
    public static string NormalizeUrl(string url)
    {
        var uri = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u : new Uri("http://" + url);
        var builder = new UriBuilder(uri);
        // Quitar parámetros irrelevantes comunes de la query string (sin System.Web).
        var keep = new List<string>();
        var raw = builder.Query;
        if (!string.IsNullOrEmpty(raw))
        {
            foreach (var part in raw.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var key = part.Split('=', 2)[0].ToLowerInvariant();
                if (new[] { "si", "utm_source", "utm_medium", "utm_campaign", "feature", "start_radio", "list_seed" }.Contains(key))
                    continue;
                keep.Add(part);
            }
        }
        builder.Query = keep.Count > 0 ? string.Join("&", keep) : "";
        builder.Fragment = "";
        return builder.Uri.ToString();
    }

    // ------------------------------------------------------------------
    // Enriquecimiento de metadatos desde MusicBrainz (sin API key).
    // Busca por título/artista y, si encuentra coincidencia, reescribe
    // los tags ID3 con ffmpeg. Best-effort: los errores se ignoran.
    // ------------------------------------------------------------------
    private static readonly HttpClient MbClient = CreateMbClient();

    private static HttpClient CreateMbClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("SpotifyMacro/1.0 (hermes; +https://github.com/Roky49/macro-spotify)");
        return c;
    }

    private static async Task EnrichFileAsync(string file)
    {
        try
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext != ".mp3" && ext != ".m4a" && ext != ".mp4") return;

            var (title, artist) = GuessFromFile(file);
            if (string.IsNullOrWhiteSpace(title)) return;

            // Género real: buscarlo en iTunes Search (gratis, sin key). Si no lo
            // hay, caer en MusicBrainz (que rara vez trae género).
            var genre = await ResolveGenreAsync(title, artist);

            var mb = await SearchMusicBrainzAsync(title, artist);
            if (mb == null) return;

            await WriteTagsFfmpegAsync(file, mb.Artist, mb.Album, title,
                mb.Position?.ToString() ?? mb.Track, mb.Date?.Year.ToString(),
                genre ?? mb.Genre);
        }
        catch { /* best-effort */ }
    }

    // Resuelve el género real de una canción usando la API gratuita de
    // búsqueda de iTunes (no requiere key). Devuelve null si no lo encuentra
    // o si solo encuentra etiquetas genéricas ("Music").
    private static async Task<string?> ResolveGenreAsync(string title, string? artist)
    {
        try
        {
            var term = string.IsNullOrWhiteSpace(artist) ? title : $"{artist} {title}";
            var url = "https://itunes.apple.com/search?term=" + Uri.EscapeDataString(term)
                + "&media=music&entity=song&limit=1";
            using var resp = await MbClient.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var res) || res.GetArrayLength() == 0)
                return null;
            if (!res[0].TryGetProperty("primaryGenreName", out var g)) return null;
            var genre = g.GetString();
            if (string.IsNullOrWhiteSpace(genre)) return null;
            // Descartar etiquetas genéricas que no son un estilo musical.
            if (IsGenericGenre(genre)) return null;
            return genre;
        }
        catch { return null; }
    }

    static bool IsGenericGenre(string g)
    {
        var lower = g.ToLowerInvariant().Trim();
        return lower.Length == 0
            || lower is "music" or "music genre"
            || lower.Contains("music (" ) && (lower.Contains("download") || lower.Contains("video"));
    }

    private static (string? title, string? artist) GuessFromFile(string file)
    {
        var cleaned = Path.GetFileNameWithoutExtension(file);

        // Quitar lo que venga tras un separador de canal al final: "Título ｜ Canal".
        // Uso '\uFF5C' (｜ fullwidth) y '|' para no depender de codificación del fuente.
        var pipe = cleaned.LastIndexOfAny(new[] { '|', '\uFF5C' });
        if (pipe > 0) cleaned = cleaned[..pipe];

        // Quitar paréntesis/corchetes sobrantes (Lyrics, Official, Audio, Video...).
        cleaned = Regex.Replace(cleaned, @"[\(\[]([^\)\]]*)[\)\]]", " ");
        cleaned = Regex.Replace(cleaned, @"\s{2,}", " ").Trim();
        // Quitar la parte "feat. X" / "ft. X".
        cleaned = Regex.Replace(cleaned, @"\s*([\(\[]?\s*(feat\.?|ft\.).*)$", "", RegexOptions.IgnoreCase);

        var sep = cleaned.IndexOf(" - ", StringComparison.Ordinal);
        if (sep > 0)
            return (cleaned[(sep + 3)..].Trim(), cleaned[..sep].Trim());
        return (cleaned.Trim(), null);
    }

    private static async Task<MbInfo?> SearchMusicBrainzAsync(string title, string? artist)
    {
        var q = $"recording:\"{title}\"";
        if (!string.IsNullOrWhiteSpace(artist))
            q += $" AND artist:\"{artist}\"";
        var url = "https://musicbrainz.org/ws/2/recording?query=" + Uri.EscapeDataString(q) + "&fmt=json&limit=1";

        using var resp = await MbClient.GetAsync(url);
        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("recordings", out var recs) || recs.GetArrayLength() == 0) return null;

        var rec = recs[0];
        var info = new MbInfo();
        if (rec.TryGetProperty("artist-credit", out var ac) && ac.GetArrayLength() > 0
            && ac[0].TryGetProperty("name", out var an)) info.Artist = an.GetString();

        if (rec.TryGetProperty("releases", out var rels) && rels.GetArrayLength() > 0)
        {
            var rel = rels[0];
            if (rel.TryGetProperty("title", out var al)) info.Album = al.GetString();
            if (rel.TryGetProperty("date", out var dt)) info.DateStr = dt.GetString();
            if (rel.TryGetProperty("media", out var media) && media.GetArrayLength() > 0)
            {
                var med = media[0];
                if (med.TryGetProperty("track", out var trk) && trk.GetArrayLength() > 0)
                {
                    var t = trk[0];
                    if (t.TryGetProperty("number", out var num)) info.Track = num.GetString();
                    if (t.TryGetProperty("position", out var pos)) info.Position = pos.GetInt32();
                }
            }
        }
        return info;
    }

    private static async Task WriteTagsFfmpegAsync(string file, string? artist, string? album,
        string? title, string? track, string? year, string? genre)
    {
        var tmp = file + ".tmp.mp3";
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            UseShellExecute = false,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(file);
        psi.ArgumentList.Add("-c"); psi.ArgumentList.Add("copy");
        psi.ArgumentList.Add("-id3v2_version"); psi.ArgumentList.Add("3");

        void Add(string k, string? v)
        {
            if (string.IsNullOrWhiteSpace(v)) return;
            psi.ArgumentList.Add("-metadata");
            psi.ArgumentList.Add($"{k}={v}");
        }
        Add("artist", artist);
        Add("album_artist", artist);
        Add("title", title);
        Add("album", album);
        Add("track", track);
        Add("date", year);
        Add("genre", genre);

        psi.ArgumentList.Add(tmp);

        var p = new Process { StartInfo = psi };
        p.Start();
        await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();

        if (p.ExitCode == 0 && System.IO.File.Exists(tmp))
            System.IO.File.Move(tmp, file, true);
        else if (System.IO.File.Exists(tmp))
            System.IO.File.Delete(tmp);
    }

    private sealed class MbInfo
    {
        public string? Artist { get; set; }
        public string? Album { get; set; }
        public string? Track { get; set; }
        public int? Position { get; set; }
        public string? DateStr { get; set; }
        public DateTime? Date => DateTime.TryParse(DateStr, out var d) ? d : null;
        public string? Genre { get; set; }
    }

    // ------------------------------------------------------------------
    // Pantalla de inicio: listar la música de la carpeta (o subcarpeta
    // elegida) con sus metadatos, y buscar/añadir info a archivos sin autor.
    // ------------------------------------------------------------------
    private static readonly string[] AudioExts = { ".mp3", ".m4a", ".mp4", ".flac", ".wav", ".opus", ".ogg" };

    [HttpGet("files")]
    public IActionResult Files([FromQuery] string? dir, [FromQuery] string? q)
    {
        string target;
        try
        {
            target = string.IsNullOrWhiteSpace(dir)
                ? DownloadDir
                : Path.IsPathRooted(dir) ? Path.GetFullPath(dir) : Path.Combine(DownloadDir, dir);
            if (!Directory.Exists(target))
                return BadRequest(new { error = "Carpeta no existe: " + target });
        }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }

        var files = Directory.GetFiles(target)
            .Where(f => AudioExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Select(f => ReadAudioTags(f))
            .Where(e => e != null)
            .OrderBy(e => e!.FileName)
            .ToList();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.ToLowerInvariant();
            files = files.Where(e => (e!.Title ?? "").ToLowerInvariant().Contains(needle)
                || (e.Artist ?? "").ToLowerInvariant().Contains(needle)
                || (e.Album ?? "").ToLowerInvariant().Contains(needle)
                || (e.Genre ?? "").ToLowerInvariant().Contains(needle)
                || e.FileName.ToLowerInvariant().Contains(needle)).ToList();
        }

        return Ok(files);
    }

    // Lee los tags de un archivo de audio con ffprobe (está en la imagen).
    // Devuelve null si no es audio o falla.
    static AudioFileInfo? ReadAudioTags(string path)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ffprobe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("-v"); psi.ArgumentList.Add("quiet");
            psi.ArgumentList.Add("-show_entries"); psi.ArgumentList.Add("format_tags=title,artist,album,track,date,genre");
            psi.ArgumentList.Add("-of"); psi.ArgumentList.Add("json");
            psi.ArgumentList.Add(path);

            var p = new Process { StartInfo = psi };
            p.Start();
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);

            var info = new AudioFileInfo
            {
                Path = path,
                FileName = Path.GetFileName(path),
                Size = new FileInfo(path).Length
            };

            if (!string.IsNullOrWhiteSpace(output))
            {
                using var doc = JsonDocument.Parse(output);
                if (doc.RootElement.TryGetProperty("format", out var fmt)
                    && fmt.TryGetProperty("tags", out var tags))
                {
                    foreach (var t in new[] { "title", "artist", "album", "track", "date", "genre" })
                        if (tags.TryGetProperty(t, out var v))
                        {
                            var val = v.GetString();
                            if (t == "title") info.Title = val;
                            else if (t == "artist") info.Artist = val;
                            else if (t == "album") info.Album = val;
                            else if (t == "track") info.Track = val;
                            else if (t == "date") info.Year = val;
                            else if (t == "genre" && !string.IsNullOrWhiteSpace(val) && !IsGenericGenre(val))
                                info.Genre = val;
                        }
                }
            }

            // Si no hay título/artista, usar una heurística por nombre de archivo.
            if (string.IsNullOrWhiteSpace(info.Title))
            {
                var g = GuessFromFile(path);
                info.Title = g.title ?? info.FileName;
                info.Artist = info.Artist ?? g.artist;
            }

            info.HasMeta = !string.IsNullOrWhiteSpace(info.Artist);
            return info;
        }
        catch { return null; }
    }

    [HttpPost("enrich")]
    public async Task<IActionResult> Enrich([FromBody] EnrichRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Path))
            return BadRequest(new { error = "Ruta requerida" });

        var file = req.Path;
        if (!System.IO.File.Exists(file))
        {
            // Puede venir como nombre de archivo → resolver bajo DownloadDir.
            var probe = Path.Combine(DownloadDir, Path.GetFileName(req.Path));
            if (System.IO.File.Exists(probe)) file = probe;
            else return NotFound(new { error = "Archivo no encontrado" });
        }

        var (title, artist) = GuessFromFile(file);
        if (string.IsNullOrWhiteSpace(title))
            return BadRequest(new { error = "No se pudo adivinar el título del archivo" });

        var genre = await ResolveGenreAsync(title, artist);
        var mb = await SearchMusicBrainzAsync(title, artist);
        if (mb == null)
            return NotFound(new { error = "No se encontró información para «" + title + "»" });

        await WriteTagsFfmpegAsync(file, mb.Artist, mb.Album, title,
            mb.Position?.ToString() ?? mb.Track, mb.Date?.Year.ToString(), genre ?? mb.Genre);

        var after = ReadAudioTags(file);
        return Ok(new
        {
            success = true,
            searched = title,
            file = after,
            message = $"Añadido: {mb.Artist} — {title}" + (string.IsNullOrWhiteSpace(mb.Album) ? "" : $" · {mb.Album}")
                + (string.IsNullOrWhiteSpace(genre) ? "" : $" 🏷 {genre}")
        });
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        var ytDlpInstalled = IsYtDlpInstalled();
        return Ok(new { status = "healthy", ytDlpInstalled, time = DateTime.UtcNow, downloadDir = DownloadDir });
    }

    static bool IsYtDlpInstalled()
    {
        try
        {
            var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "yt-dlp",
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            p.Start();
            var version = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(2000);
            return p.ExitCode == 0 && !string.IsNullOrEmpty(version);
        }
        catch { return false; }
    }

    // ------------------------------------------------------------------
    // Reproductor: servir el archivo de audio por HTTP (con soporte de
    // rango/seek para el <audio> del navegador).
    // ------------------------------------------------------------------
    [HttpGet("audio")]
    public IActionResult Audio([FromQuery] string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return BadRequest(new { error = "Path requerido" });
        string file;
        try
        {
            // Acepta ruta absoluta (viene del listado) o nombre suelto bajo DownloadDir.
            file = System.IO.File.Exists(path)
                ? Path.GetFullPath(path)
                : Path.Combine(DownloadDir, path);
            if (!System.IO.File.Exists(file))
                return NotFound(new { error = "Archivo no encontrado" });
        }
        catch { return NotFound(new { error = "Archivo no encontrado" }); }

        var ext = Path.GetExtension(file).ToLowerInvariant();
        var mime = ext switch
        {
            ".mp3" => "audio/mpeg",
            ".m4a" or ".mp4" => "audio/mp4",
            ".flac" => "audio/flac",
            ".wav" => "audio/wav",
            ".opus" or ".ogg" => "audio/ogg",
            _ => "application/octet-stream"
        };
        var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, mime, enableRangeProcessing: true);
    }

    // ------------------------------------------------------------------
    // Descarga asíncrona con progreso: lanza el trabajo en background y
    // expone cuántas van listas de cuántas totales (barra de progreso).
    // ------------------------------------------------------------------
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DownloadProgress> _jobs = new();

    [HttpPost("async")]
    public IActionResult StartAsync([FromBody] DownloadRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Url))
            return BadRequest(new { error = "URL requerida" });

        if (req.ReDownload != true && IsSourceDownloaded(req.Url))
        {
            return Ok(new
            {
                skipped = true,
                reason = "ya-descargada",
                message = "Esta playlist/URL ya se descargó antes. Se omite para no repetir. Usa re-descargar si quieres bajarla otra vez."
            });
        }

        var job = new DownloadProgress
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Url = req.Url,
            StartedAt = DateTime.UtcNow
        };
        job.Total = IsSpotifyUrl(req.Url) ? GetSpotifyTrackCount(req.Url) : 1;
        _jobs[job.Id] = job;

        _ = Task.Run(async () => await RunJobAsync(job, req));
        return Ok(new { jobId = job.Id, total = job.Total, started = true });
    }

    [HttpGet("progress/{id}")]
    public IActionResult Progress(string id)
    {
        if (!_jobs.TryGetValue(id, out var job))
            return NotFound(new { error = "Trabajo no encontrado" });
        return Ok(new
        {
            jobId = job.Id,
            total = job.Total,
            done = job.Done,
            failed = job.Failed,
            skipped = job.Skipped,
            running = job.Running,
            message = job.Message,
            percent = job.Percent,
            url = job.Url,
            lastError = job.LastError
        });
    }

    int GetSpotifyTrackCount(string url) => FetchSpotifyTracksAsync(ExtractSpotifyPlaylistId(url)).Result?.Count ?? 1;

    async Task RunJobAsync(DownloadProgress job, DownloadRequest req)
    {
        try
        {
            if (IsSpotifyUrl(req.Url))
            {
                var playlistId = ExtractSpotifyPlaylistId(req.Url);
                var tracks = await FetchSpotifyTracksAsync(playlistId);
                job.Total = tracks.Count;

                foreach (var t in tracks)
                {
                    var query = string.IsNullOrWhiteSpace(t.Artist) ? t.Title : $"{t.Artist} - {t.Title}";
                    job.Message = $"Descargando: {t.Title}";
                    var res = await DownloadYouTubeSearchAsync(query, req.Format ?? "mp3", req.Quality ?? "0");
                    if (res.Success) { Interlocked.Increment(ref job.Done); }
                    else if (res.Skipped) { Interlocked.Increment(ref job.Skipped); }
                    else { Interlocked.Increment(ref job.Failed); job.LastError = res.Error; }
                }
                MarkSourceDownloaded(req.Url);
            }
            else
            {
                job.Total = 1;
                job.Message = "Descargando...";
                var dir = req.Url;
                var ok = await DownloadSingleAsync(dir, req.Format ?? "mp3", req.Quality ?? "0");
                if (ok.success) { job.Done = 1; MarkSourceDownloaded(req.Url); }
                else { job.Failed = 1; job.LastError = ok.error; }
            }
            job.Message = "Completado";
        }
        catch (Exception ex)
        {
            job.Failed++;
            job.LastError = ex.Message;
            job.Message = "Error";
        }
        finally
        {
            job.Running = false;
        }
    }

    // Descarga una URL normal (una sola) y devuelve si fue bien.
    async Task<(bool success, string? error, string? filePath)> DownloadSingleAsync(string url, string format, string quality)
    {
        var outputTemplate = Path.Combine(DownloadDir, "%(title)s.%(ext)s");
        var before = Directory.GetFiles(DownloadDir)
            .Select(System.IO.File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "yt-dlp", UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            psi.ArgumentList.Add("-x");
            psi.ArgumentList.Add("--audio-format"); psi.ArgumentList.Add(format);
            psi.ArgumentList.Add("--audio-quality"); psi.ArgumentList.Add(quality);
            // YouTube bloquea el cliente web por defecto con HTTP 403 (anti-bot).
            psi.ArgumentList.Add("--extractor-args"); psi.ArgumentList.Add("youtube:player_client=android");
            psi.ArgumentList.Add("-o"); psi.ArgumentList.Add(outputTemplate);
            psi.ArgumentList.Add("--download-archive"); psi.ArgumentList.Add(ArchiveFile);
            psi.ArgumentList.Add("--no-overwrites");
            psi.ArgumentList.Add("--embed-metadata");
            psi.ArgumentList.Add(url);
            var p = new Process { StartInfo = psi };
            p.Start();
            var err = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            if (p.ExitCode != 0) return (false, err.Trim(), null);
        }
        catch (Exception ex) { return (false, ex.Message, null); }

        var nf = Directory.GetFiles(DownloadDir)
            .Where(f => System.IO.File.GetLastWriteTimeUtc(f) > before)
            .OrderBy(System.IO.File.GetLastWriteTimeUtc).ToList();
        if (nf.Count == 0) return (true, null, null); // ya existía (omitido)

        await EnrichFileAsync(nf[0]);
        var lib = new LibraryEntry
        {
            Id = Guid.NewGuid().ToString(),
            Title = Path.GetFileNameWithoutExtension(nf[0]),
            FilePath = nf[0],
            Format = format,
            SourceUrl = url,
            DownloadedAt = DateTime.UtcNow,
            FileSize = new FileInfo(nf[0]).Length
        };
        LibraryController.AddEntry(lib);
        return (true, null, nf[0]);
    }
}

public class DownloadProgress
{
    public string Id { get; set; } = "";
    public string Url { get; set; } = "";
    public int Total { get; set; }
    public int Done;
    public int Failed;
    public int Skipped;
    public bool Running { get; set; } = true;
    public string? Message { get; set; }
    public string? LastError { get; set; }
    public DateTime StartedAt { get; set; }
    public double Percent => Total == 0 ? 0 : Math.Min(100, (double)(Done + Failed + Skipped) / Total * 100);
    public int Remaining => Math.Max(0, Total - Done - Failed - Skipped);
}

public record EnrichRequest(string Path);
public record AudioFileInfo
{
    public string Path { get; set; } = "";
    public string FileName { get; set; } = "";
    public long Size { get; set; }
    public string Format => System.IO.Path.GetExtension(FileName).TrimStart('.').ToLowerInvariant();
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public string? Track { get; set; }
    public string? Year { get; set; }
    public string? Genre { get; set; }
    public bool HasMeta { get; set; }
}

public record DownloadRequest(string Url, string? Format = "mp3", string? Quality = "0", bool? ReDownload = null);
public record SetDirRequest(string Path);
