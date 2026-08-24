using Microsoft.AspNetCore.SignalR;

namespace Api.Hubs;

public class SpotifyHub : Hub
{
    public async Task Play() => await Clients.Others.SendAsync("Command", "play");
    public async Task Pause() => await Clients.Others.SendAsync("Command", "pause");
    public async Task Skip() => await Clients.Others.SendAsync("Command", "skip");
    public async Task Previous() => await Clients.Others.SendAsync("Command", "previous");
    public async Task SetVolume(int volume) => await Clients.Others.SendAsync("Volume", volume);
    public async Task PlayTrack(string trackId) => await Clients.Others.SendAsync("PlayTrack", trackId);
    public async Task SyncState(object state) => await Clients.Others.SendAsync("SyncState", state);
}
