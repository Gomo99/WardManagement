using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace WARDMANAGEMENTSYSTEM.Hubs
{
    public class NotificationHub : Hub
    {
        // Clients join groups based on their user ID and role
        public async Task SubscribeToUser(string userId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userId}");
        }

        public async Task SubscribeToRole(string role)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"role-{role}");
        }

        // The groups "all" will be used for broadcast notifications
        public async Task SubscribeToAll()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "all");
        }
    }
}