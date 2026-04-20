using Diva.Core.Models;
using Microsoft.AspNetCore.SignalR;

namespace Diva.Host.Hubs;

/// <summary>
/// SignalR hub for pushing agent stream chunks to connected browser clients.
/// Clients join a tenant-scoped group on connect via ?tenantId= query param.
/// </summary>
public class AgentStreamHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var tenantId = Context.GetHttpContext()?.Request.Query["tenantId"].ToString() ?? "1";
        await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant-{tenantId}");
        await base.OnConnectedAsync();
    }

    /// <summary>Push a stream chunk to all clients in a tenant group.</summary>
    public static Task PushChunkAsync(
        IHubContext<AgentStreamHub> hub,
        string tenantId,
        AgentStreamChunk chunk)
        => hub.Clients.Group($"tenant-{tenantId}").SendAsync("AgentChunk", chunk);
}
