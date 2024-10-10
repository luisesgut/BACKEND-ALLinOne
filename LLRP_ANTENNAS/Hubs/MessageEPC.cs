using Microsoft.AspNetCore.SignalR;
using System;
using System.Threading.Tasks;
using Serilog;

namespace LLRP_ANTENNAS.Hubs
{
    public class MessageEPC : Hub
    {
        // Método para que los clientes se unan a un grupo de antenas específico
        public async Task JoinGroup(string groupName)
        {
            var connectionId = Context.ConnectionId;
            await Groups.AddToGroupAsync(connectionId, groupName);
            Console.WriteLine($"Cliente {connectionId} se unió al grupo {groupName}.");
        }

        // Método para que los clientes abandonen un grupo de antenas
        public async Task LeaveGroup(string groupName)
        {
            var connectionId = Context.ConnectionId;
            await Groups.RemoveFromGroupAsync(connectionId, groupName);
            Console.WriteLine($"Cliente {connectionId} dejó el grupo {groupName}.");
        }

        // Método para enviar mensajes a todos los clientes conectados a un grupo
        public async Task SendMessageToGroup(string antennaGroup, string message)
        {
            await Clients.Group(antennaGroup).SendAsync("sendMessage", message);
            Log.Information($"Mensaje enviado al grupo {antennaGroup}: {message}");
        }

        public override async Task OnConnectedAsync()
        {
            var connectionId = Context.ConnectionId;
            Log.Information($"Cliente conectado: {connectionId}");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            var connectionId = Context.ConnectionId;
            if (exception != null)
            {
                Log.Error($"Cliente desconectado con error: {connectionId}, Error: {exception.Message}");
            }
            else
            {
                Log.Information($"Cliente desconectado: {connectionId}");
            }

            await base.OnDisconnectedAsync(exception);
        }
    }

}
