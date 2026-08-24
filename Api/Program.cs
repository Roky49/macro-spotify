using Api.Services;
using Api.Hubs;

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient<SpotifyService>();
builder.Services.AddSingleton<DownloadQueue>();
builder.Services.AddSignalR();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

if (app.Environment.IsDevelopment() || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RENDER")))
    { app.UseSwagger(); app.UseSwaggerUI(); }

app.UseDefaultFiles(); app.UseStaticFiles(); app.UseCors();
app.UseAuthorization(); app.MapControllers(); app.MapHub<SpotifyHub>("/hub/spotify"); app.MapFallbackToFile("index.html");

// Arrancar el worker de fondo de la cola de descargas.
app.Services.GetRequiredService<DownloadQueue>().Start();

app.Run();
