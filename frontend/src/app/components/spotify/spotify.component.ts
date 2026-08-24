import { Component, OnInit, ViewChild, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';

@Component({
  selector: 'app-spotify',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="app">
      <nav class="nav">
        <span class="logo">🎵 Spotify Macro</span>
        <div class="tabs">
          <button [class.active]="tab==='home'" (click)="tab='home'">🏠 Inicio</button>
          <button [class.active]="tab==='downloads'" (click)="tab='downloads'">⬇️ Descargas</button>
        </div>
        <div class="api-badge" [class.ok]="apiOk" [class.bad]="!apiOk">
          <span class="dot">{{ apiOk ? '✓' : '✕' }}</span> API {{ apiOk ? 'conectada' : 'sin conexión' }}
        </div>
      </nav>

      <!-- INICIO: buscador de la carpeta de música -->
      <div class="container" *ngIf="tab==='home'">
        <div class="home-head">
          <h2>🎧 Tu música</h2>
          <span class="home-folder">📁 {{ downloadDir }}</span>
        </div>

        <div class="filters">
          <input [(ngModel)]="fileQuery" (input)="applyFilters()" placeholder="🔍 Filtrar por título, artista, álbum..." class="input search" style="flex:1">
          <select class="input" (change)="applyFilters()" [(ngModel)]="filterGenre">
            <option value="">🎵 Género: todos</option>
            <option *ngFor="let g of genresList" [value]="g">{{ g }}</option>
          </select>
          <select class="input" (change)="applyFilters()" [(ngModel)]="filterArtist">
            <option value="">🎤 Artista: todos</option>
            <option *ngFor="let a of artistsList" [value]="a">{{ a }}</option>
          </select>
        </div>

        <div class="file-list" *ngIf="filteredFiles.length">
          <div class="file-item" *ngFor="let f of filteredFiles" [class.playing]="playing && current?.path===f.path" (dblclick)="playTrack(f)">
            <span class="idx">{{ filteredFiles.indexOf(f)+1 }}</span>
            <button class="fi-play" (click)="playTrack(f)">{{ (playing && current?.path===f.path) ? '⏸' : '▶' }}</button>
            <div class="fi-info">
              <strong>{{ f.title || f.fileName }}</strong>
              <span *ngIf="f.artist">🎤 {{ f.artist }}<ng-container *ngIf="f.album"> · {{ f.album }}</ng-container><ng-container *ngIf="f.genre"> · 🏷 {{ f.genre }}</ng-container></span>
              <span *ngIf="!f.artist" class="no-meta">⚠️ Sin autor/información</span>
            </div>
            <button *ngIf="!f.hasMeta" class="btn-meta" (click)="enrich(f)" [disabled]="f._loading">{{ f._loading ? '⏳' : '🔍 info' }}</button>
            <span *ngIf="f._msg" class="enrich-msg" [class.err]="f._err">{{ f._msg }}</span>
            <span class="fi-sub">{{ formatSize(f.size) }}</span>
          </div>
        </div>
        <div *ngIf="!filteredFiles.length && loaded" class="empty">No hay resultados con ese filtro.</div>
        <div *ngIf="!loaded" class="empty">Cargando tu música...</div>
      </div>

      <!-- Descargas -->
      <div class="container" *ngIf="tab==='downloads'">
        <h2>⬇️ Descargar música</h2>
        <div class="card" style="margin-bottom:1rem">
          <div class="dir-picker">
            <span style="color:#888;font-size:13px">📁 Guardar en:</span>
            <input [(ngModel)]="downloadDir" class="input" style="flex:1;font-size:13px" placeholder="Ruta...">
            <button (click)="chooseDir()" class="btn-sm" style="background:#333">📂 Elegir carpeta</button>
            <button (click)="setDir()" class="btn-sm" style="background:#333">Aplicar</button>
          </div>
          <p style="color:#888;margin:1rem 0">Pega una URL de YouTube, playlist, Spotify, SoundCloud, etc. Los repetidos se omiten automáticamente.</p>
          <div class="search-bar">
            <input [(ngModel)]="dlUrl" placeholder="https://youtube.com/playlist?list=...  o  https://open.spotify.com/..." class="input" style="flex:1">
            <select [(ngModel)]="dlFormat" class="input" style="width:100px">
              <option value="mp3">MP3</option><option value="m4a">M4A</option><option value="opus">Opus</option><option value="flac">FLAC</option><option value="wav">WAV</option>
            </select>
            <button (click)="download()" class="btn-primary" [disabled]="dlLoading">{{ dlLoading ? '⏳' : '⬇️ Descargar' }}</button>
          </div>
          <label class="redl-toggle"><input type="checkbox" [(ngModel)]="reDownload"> Re-descargar aunque ya esté</label>

          <!-- Progreso de descarga -->
          <div *ngIf="dlProgress" class="dl-progress">
            <div class="prog-head">
              <span>{{ dlProgress.message || 'Descargando...' }}</span>
              <span *ngIf="dlProgress.running && dlProgress.total > 1">
                {{ dlProgress.done }} de {{ dlProgress.total }} · <strong>{{ dlProgress.total - dlProgress.done - dlProgress.failed - dlProgress.skipped }} quedan</strong>
              </span>
              <span *ngIf="!dlProgress.running">{{ dlProgress.total }} canciones</span>
            </div>
            <div class="prog-bar"><div class="prog-fill" [style.width.%]="dlProgress.percent"></div></div>
            <div class="prog-sub">
              <span *ngIf="dlProgress.skipped" class="ok">✓ {{ dlProgress.skipped }} repetidas omitidas</span>
              <span *ngIf="dlProgress.failed" class="err">✕ {{ dlProgress.failed }} con error</span>
            </div>
          </div>
          <div *ngIf="dlSkipped" class="dl-skip">⚠️ {{ dlSkipped }}</div>
          <div *ngIf="dlResult" class="dl-result">✅ {{ dlResult.fileName }} ({{ formatSize(dlResult.fileSize) }})</div>
          <div *ngIf="dlError" class="dl-error">❌ {{ dlError }}</div>
        </div>
        <h3>📚 Biblioteca ({{ library.length }})</h3>
        <input [(ngModel)]="libQuery" (input)="loadLibrary()" placeholder="Buscar..." class="input" style="width:100%;margin-bottom:1rem">
        <div class="track" *ngFor="let e of library">
          <div style="flex:1"><strong>{{ e.title }}</strong> <span style="color:#888;font-size:12px">.{{ e.format }} · {{ formatSize(e.fileSize) }}</span></div>
          <button class="btn-sm" (click)="deleteTrack(e.id)" style="background:#b71c1c;color:#fff">🗑️</button>
        </div>
        <div *ngIf="!library.length" class="empty">Vacía</div>
      </div>

      <!-- Reproductor fijo -->
      <div class="player" *ngIf="current">
        <div class="pl-track">
          <strong>{{ current.title || current.fileName }}</strong>
          <span>{{ current.artist || '—' }}</span>
        </div>
        <div class="pl-controls">
          <button class="pl-btn" [class.on]="shuffle" (click)="toggleShuffle()" title="Aleatorio">🔀</button>
          <button class="pl-btn" (click)="prevTrack()" title="Anterior">⏮</button>
          <button class="pl-btn pl-main" (click)="togglePlay()">{{ playing ? '⏸' : '▶' }}</button>
          <button class="pl-btn" (click)="nextTrack()" title="Siguiente">⏭</button>
          <button class="pl-btn" [class.on]="repeat" (click)="toggleRepeat()" title="Repetir">🔁</button>
        </div>
        <div class="pl-progress">
          <span class="pl-time">{{ fmtTime(currentTime) }}</span>
          <input type="range" class="pl-range" [min]="0" [max]="duration||0" [value]="currentTime"
                 (input)="seek($event)" (change)="seek($event)">
          <span class="pl-time">{{ fmtTime(duration) }}</span>
        </div>
        <div class="pl-volume">
          <span>🔊</span>
          <input type="range" class="pl-range vol" min="0" max="1" step="0.01" [value]="volume" (input)="setVolume($event)">
        </div>
      </div>
    </div>
    <audio #audioEl style="display:none"></audio>
  `,
  styles: [`
    .app{background:#1a1a1a;min-height:100vh;color:#e0e0e0;font-family:'Inter',sans-serif}
    .nav{background:#2a2a2a;border-bottom:1px solid #333;padding:12px 24px;display:flex;align-items:center;gap:2rem}
    .logo{font-size:1.2rem;font-weight:700;color:#1ed760;margin-right:auto}
    .tabs{display:flex;gap:4px}
    .tabs button{background:none;border:none;color:#888;padding:8px 16px;border-radius:8px;cursor:pointer;font-size:14px;transition:all .2s}
    .tabs button.active{background:#1ed760;color:#111;font-weight:600}
    .api-badge{display:flex;align-items:center;gap:6px;font-size:12px;padding:4px 12px;border-radius:999px;background:#1e1e1e;border:1px solid #333;color:#888}
    .api-badge.ok{color:#1ed760;border-color:#1ed76055;background:#1ed76011}
    .api-badge.bad{color:#ff8a80;border-color:#ff8a8055;background:#ff8a8011}
    .api-badge .dot{font-weight:700}
    .container{max-width:900px;margin:0 auto;padding:2rem}
    h2{color:#fff;margin-bottom:1.5rem}
    .home-head{display:flex;align-items:center;justify-content:space-between;gap:1rem;flex-wrap:wrap}
    .dir-picker{display:flex;gap:8px;align-items:center}
    .card{background:#2a2a2a;padding:1.5rem;border-radius:12px}
    .input{padding:10px 16px;border-radius:8px;border:1px solid #444;background:#1e1e1e;color:#e0e0e0;font-size:14px}
    .btn-primary{padding:10px 20px;background:#1ed760;color:#111;border:none;border-radius:8px;font-weight:600;cursor:pointer}
    .btn-sm{padding:6px 12px;background:#1ed760;color:#111;border:none;border-radius:6px;cursor:pointer;font-weight:600;white-space:nowrap}
    .search-bar{display:flex;gap:8px;margin-bottom:1rem}
    .dl-progress{background:#2a2a2a;border:1px solid #333;border-radius:10px;padding:14px 16px;margin-top:12px}
    .prog-head{display:flex;justify-content:space-between;align-items:center;font-size:13px;color:#ddd;gap:12px;margin-bottom:8px}
    .prog-head strong{color:#1ed760}
    .prog-bar{height:10px;background:#1e1e1e;border-radius:999px;overflow:hidden;border:1px solid #333}
    .prog-fill{height:100%;background:linear-gradient(90deg,#1ed760,#4caf50);border-radius:999px;transition:width .4s ease}
    .prog-sub{display:flex;gap:16px;font-size:12px;margin-top:8px}
    .prog-sub .ok{color:#1ed760}
    .prog-sub .err{color:#ff8a80}
    .home-folder{color:#888;font-size:13px;background:#1e1e1e;padding:6px 12px;border-radius:8px;border:1px solid #333}
    .filters{display:flex;gap:8px;margin:1rem 0 1rem;flex-wrap:wrap}
    .filters .input.search{flex:1;min-width:180px}
    .filters select{flex:0 1 auto}
    .track{color:#aaa;font-size:13px;padding:4px 0;border-bottom:1px solid #333;display:flex;align-items:center;gap:12px}
    .empty{text-align:center;color:#666;padding:3rem}
    .dl-result{color:#1ed760;font-size:13px;margin-top:10px}
    .dl-skip{background:#3a3a00;color:#ffd54f;padding:10px 14px;border-radius:8px;margin-top:10px;font-size:13px}
    .dl-error{background:#3a0a0a;color:#ff8a80;padding:10px 14px;border-radius:8px;margin-top:10px;font-size:13px}
    .redl-toggle{display:flex;align-items:center;gap:6px;font-size:13px;color:#aaa;margin-top:8px;cursor:pointer}
    .file-list{display:flex;flex-direction:column;gap:2px;margin-bottom:80px}
    .file-item{display:flex;align-items:center;gap:10px;background:#242424;padding:6px 12px;border-radius:6px;cursor:pointer;transition:background .15s;border:1px solid transparent}
    .file-item:hover{background:#2d2d2d}
    .file-item.playing{background:#1ed76022;border-color:#1ed76055}
    .idx{color:#666;font-size:11px;width:22px;text-align:right;flex-shrink:0}
    .fi-play{background:none;border:none;color:#1ed760;font-size:14px;cursor:pointer;width:26px;height:26px;flex-shrink:0;border-radius:50%}
    .fi-play:hover{background:#1ed76022}
    .fi-info{flex:1;display:flex;flex-direction:column;min-width:0}
    .fi-info strong{font-size:13px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
    .fi-info span{color:#888;font-size:11.5px}
    .fi-info .no-meta{color:#ffd54f;font-size:11px}
    .btn-meta{padding:3px 8px;background:#333;color:#ffd54f;border:none;border-radius:4px;cursor:pointer;font-size:11px;white-space:nowrap}
    .enrich-msg{font-size:11px;color:#1ed760;max-width:180px;flex-shrink:0}
    .enrich-msg.err{color:#ff8a80}
    .fi-sub{color:#666!important;font-size:11px!important;flex-shrink:0;margin-left:4px}
    /* Reproductor fijo inferior */
    .player{position:fixed;bottom:0;left:0;right:0;background:#111c11;border-top:1px solid #1ed76044;display:flex;align-items:center;gap:18px;padding:10px 24px;z-index:1000}
    .pl-track{min-width:200px;max-width:260px;display:flex;flex-direction:column}
    .pl-track strong{font-size:13px;color:#fff;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
    .pl-track span{font-size:11px;color:#888}
    .pl-controls{display:flex;align-items:center;gap:6px}
    .pl-btn{background:none;border:none;color:#ccc;font-size:18px;cursor:pointer;padding:6px 8px;border-radius:8px}
    .pl-btn:hover{background:#1ed76022;color:#1ed760}
    .pl-btn.on{color:#1ed760;background:#1ed76022}
    .pl-main{font-size:26px;color:#1ed760}
    .pl-progress{flex:1;display:flex;align-items:center;gap:8px}
    .pl-time{font-size:11px;color:#888;min-width:34px;text-align:center}
    .pl-range{flex:1;accent-color:#1ed760;height:4px;cursor:pointer}
    .pl-volume{display:flex;align-items:center;gap:6px;width:140px}
    .pl-volume span{font-size:13px}
    .pl-range.vol{width:90px;flex:none}
  `]
})
export class SpotifyComponent implements OnInit {
  @ViewChild('audioEl') audioEl!: ElementRef<HTMLAudioElement>;
  tab = 'home';
  apiOk = false;
  fileQuery = '';
  files: any[] = [];
  loaded = false;
  // Filtros
  filterGenre = '';
  filterArtist = '';
  genresList: string[] = [];
  artistsList: string[] = [];
  filteredFiles: any[] = [];
  // Reproductor
  current: any = null;
  playing = false;
  currentTime = 0;
  duration = 0;
  volume = 0.8;
  shuffle = false;
  repeat = false;
  private audioLoaded = false;
  // Descargas
  dlUrl = '';
  dlFormat = 'mp3';
  reDownload = false;
  dlLoading = false;
  dlResult: any = null;
  dlError = '';
  dlSkipped = '';
  dlProgress: any = null;
  private dlTimer: any = null;
  library: any[] = [];
  libQuery = '';
  downloadDir = '';

  constructor(private http: HttpClient) {}

  ngOnInit() {
    this.checkApi();
    this.loadLibrary();
    this.http.get<any>('/api/download/dir').subscribe(d => {
      // Usa la carpeta guardada anteriormente (la misma que en descargas).
      this.downloadDir = d.downloadDir;
      this.loadFiles(); // busca automáticamente al entrar
    });

    // Global hotkeys
    document.addEventListener('keydown', (e) => {
      if (e.target instanceof HTMLInputElement || e.target instanceof HTMLTextAreaElement || e.target instanceof HTMLSelectElement) return;
      switch (e.code) {
        case 'KeyH': e.preventDefault(); this.tab = 'home'; break;
        case 'KeyD': e.preventDefault(); this.tab = 'downloads'; break;
        case 'Space': e.preventDefault(); this.tab = 'home'; break;
      }
    });
  }

  checkApi() {
    this.http.get<any>('/api/download/health').subscribe({
      next: () => this.apiOk = true,
      error: () => this.apiOk = false
    });
  }

  loadFiles() {
    const q = this.fileQuery ? `&q=${encodeURIComponent(this.fileQuery)}` : '';
    // Usa la misma carpeta de descargas (la guardada anteriormente).
    this.http.get<any[]>(`/api/download/files${q}`).subscribe({
      next: d => {
        this.files = d.map(f => ({ ...f, _loading: false, _msg: '' }));
        this.loaded = true;
        this.rebuildFilterOptions();
        this.applyFilters();
      },
      error: e => { this.dlError = e.error?.error || 'Error al listar'; this.loaded = true; }
    });
  }

  // Construye las opciones únicas de género y artista para los desplegables.
  rebuildFilterOptions() {
    const g = new Set<string>();
    const a = new Set<string>();
    for (const f of this.files) {
      if (f.genre) g.add(f.genre);
      if (f.artist) a.add(f.artist);
    }
    this.genresList = Array.from(g).sort();
    this.artistsList = Array.from(a).sort();
  }

  // Filtra la lista actual por texto + género + artista (todo en cliente).
  applyFilters() {
    const text = this.fileQuery.trim().toLowerCase();
    this.filteredFiles = this.files.filter(f =>
      (!text || (f.title || '').toLowerCase().includes(text)
        || (f.artist || '').toLowerCase().includes(text)
        || (f.album || '').toLowerCase().includes(text)
        || f.fileName.toLowerCase().includes(text))
      && (!this.filterGenre || f.genre === this.filterGenre)
      && (!this.filterArtist || f.artist === this.filterArtist)
    );
  }

  chooseDir() {
    const anyWin = window as any;
    if (anyWin.showDirectoryPicker) {
      try {
        anyWin.showDirectoryPicker({ mode: 'readwrite' }).then((h: any) => {
          this.downloadDir = h.name; this.setDir();
        }).catch((err: any) => { if (err?.name !== 'AbortError') this.dlError = 'No se pudo elegir la carpeta.'; });
        return;
      } catch { }
    }
    // Fallback universal (Firefox/Safari/Opera).
    this.pickViaInput();
  }

  // Selector clásico (<input type=file webkitdirectory>) para navegadores que
  // no soportan la File System Access API. Lee el nombre de la carpeta elegida.
  pickViaInput() {
    const input = document.createElement('input');
    input.type = 'file';
    input.setAttribute('webkitdirectory', '');
    input.setAttribute('directory', '');
    input.style.display = 'none';
    input.onchange = (e: any) => {
      const files: File[] = Array.from(e.target.files || []);
      if (!files.length) return;
      // El primer archivo lleva la ruta relativa: "NombreCarpeta/sub/fichero"
      const rel = (files[0] as any).webkitRelativePath || '';
      const folderName = rel.split('/')[0] || 'music';
      this.downloadDir = folderName;
      this.setDir();
    };
    document.body.appendChild(input);
    input.click();
    input.remove();
  }

  enrich(f: any) {
    f._loading = true; f._msg = '';
    this.http.post<any>('/api/download/enrich', { path: f.path }).subscribe({
      next: r => {
        f._loading = false;
        f.artist = r.file?.artist; f.album = r.file?.album; f.title = r.file?.title; f.hasMeta = r.file?.hasMeta;
        f._msg = r.message || 'Info añadida';
      },
      error: e => { f._loading = false; f._err = true; f._msg = e.error?.error || 'No se encontró info'; this.loadLibrary(); }
    });
  }

  setDir() {
    this.http.post<any>('/api/download/set-dir', { path: this.downloadDir })
      .subscribe({
        next: d => { this.downloadDir = d.downloadDir; this.dlError = ''; this.loadLibrary(); this.loadFiles(); },
        error: e => this.dlError = e.error?.error || 'Error al cambiar la ubicación'
      });
  }

  download() {
    if (!this.dlUrl) return;
    this.dlLoading = true; this.dlError = ''; this.dlResult = null; this.dlSkipped = '';
    this.dlProgress = null;
    if (this.dlTimer) { clearInterval(this.dlTimer); this.dlTimer = null; }

    this.http.post<any>('/api/download/async', { url: this.dlUrl, format: this.dlFormat, reDownload: this.reDownload })
      .subscribe({
        next: r => {
          this.dlLoading = false;
          if (r?.skipped) { this.dlSkipped = r.message || 'Ya descargada antes. Se omite.'; return; }
          if (!r?.jobId) { this.dlError = 'Error al iniciar la descarga'; return; }
          this.dlProgress = { jobId: r.jobId, running: true, percent: 0, done: 0, failed: 0, skipped: 0, total: r.total };
          // Polling del progreso
          this.dlTimer = setInterval(() => this.pollProgress(r.jobId), 1500);
          this.pollProgress(r.jobId);
        },
        error: e => { this.dlLoading = false; this.dlError = e.error?.error || 'Error'; }
      });
  }

  pollProgress(jobId: string) {
    this.http.get<any>(`/api/download/progress/${jobId}`).subscribe({
      next: p => {
        this.dlProgress = { ...p };
        if (!p.running) {
          if (this.dlTimer) { clearInterval(this.dlTimer); this.dlTimer = null; }
          this.loadLibrary(); this.loadFiles();
          if (p.failed && !p.done) this.dlError = p.lastError || `${p.failed} canciones fallaron`;
        }
      },
      error: () => { if (this.dlTimer) { clearInterval(this.dlTimer); this.dlTimer = null; } }
    });
  }

  loadLibrary() {
    const q = this.libQuery ? `?q=${this.libQuery}` : '';
    this.http.get<any[]>(`/api/library${q}`).subscribe(d => this.library = d);
  }

  deleteTrack(id: string) { this.http.delete(`/api/library/${id}`).subscribe(() => this.loadLibrary()); }

  formatSize(bytes: number): string {
    if (!bytes) return '0 B';
    const units = ['B', 'KB', 'MB', 'GB'];
    let i = 0;
    while (bytes >= 1024 && i < units.length - 1) { bytes /= 1024; i++; }
    return bytes.toFixed(1) + ' ' + units[i];
  }

  // ------------------------------------------------------------------
  // Reproductor: play/pause, prev/next, seek, volumen.
  // ------------------------------------------------------------------
  private get audio(): HTMLAudioElement {
    return this.audioEl?.nativeElement as HTMLAudioElement;
  }

  playTrack(f: any) {
    if (this.current && this.current.path === f.path) { this.togglePlay(); return; }
    this.setupAudioEvents();
    this.current = f;
    this.audio.src = `/api/download/audio?path=${encodeURIComponent(f.path)}`;
    this.audio.load();
    this.audio.play();
    this.playing = true;
  }

  togglePlay() {
    if (!this.current) return;
    if (this.playing) { this.audio.pause(); this.playing = false; }
    else { this.audio.play(); this.playing = true; }
  }

  nextTrack() {
    if (!this.current || !this.filteredFiles.length) return;
    let next: any;
    if (this.shuffle) {
      // Canción aleatoria distinta a la actual (o la misma si solo hay 1).
      if (this.filteredFiles.length === 1) next = this.filteredFiles[0];
      else {
        do { next = this.filteredFiles[Math.floor(Math.random() * this.filteredFiles.length)]; }
        while (next.path === this.current.path);
      }
    } else {
      const idx = this.filteredFiles.findIndex(f => f.path === this.current.path);
      next = this.filteredFiles[(idx + 1) % this.filteredFiles.length];
    }
    this.playTrack(next);
  }

  prevTrack() {
    if (!this.current || !this.filteredFiles.length) return;
    const idx = this.filteredFiles.findIndex(f => f.path === this.current.path);
    // Si llevan más de 3s, reiniciar la canción actual; si no, ir a la anterior.
    if (this.currentTime > 3) { this.audio.currentTime = 0; return; }
    const prev = this.filteredFiles[(idx - 1 + this.filteredFiles.length) % this.filteredFiles.length];
    this.playTrack(prev);
  }

  toggleShuffle() { this.shuffle = !this.shuffle; }

  toggleRepeat() { this.repeat = !this.repeat; }

  seek(e: any) {
    const val = parseFloat(e.target.value);
    if (this.audio && isFinite(val)) this.audio.currentTime = val;
  }

  setVolume(e: any) {
    this.volume = parseFloat(e.target.value);
    if (this.audio) this.audio.volume = this.volume;
  }

  fmtTime(s: number): string {
    if (!isFinite(s) || s < 0) return '0:00';
    const m = Math.floor(s / 60);
    const sec = Math.floor(s % 60);
    return `${m}:${sec.toString().padStart(2, '0')}`;
  }

  private setupAudioEvents() {
    if (this.audioLoaded) return;
    this.audioLoaded = true;
    const a = this.audio;
    a.volume = this.volume;
    a.addEventListener('timeupdate', () => this.currentTime = a.currentTime);
    a.addEventListener('loadedmetadata', () => this.duration = a.duration || 0);
    a.addEventListener('durationchange', () => this.duration = a.duration || 0);
    a.addEventListener('ended', () => { if (this.repeat) { this.audio.currentTime = 0; this.audio.play(); } else this.nextTrack(); });
    a.addEventListener('play', () => this.playing = true);
    a.addEventListener('pause', () => this.playing = false);
  }
}
