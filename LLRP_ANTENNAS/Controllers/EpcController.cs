using LLRP_ANTENNAS.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Serilog;

namespace LLRP_ANTENNAS.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class EpcController : ControllerBase
    {
        private IHubContext<MessageEPC> _hubContext;

        public EpcController(IHubContext<MessageEPC> hubContext)
        {
            _hubContext = hubContext;
        }

        [HttpGet]
        public async Task<IActionResult> Send(string message, string antennaGroup)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message))
                {
                    Log.Warning("Intento de enviar mensaje vacío.");
                    return BadRequest("El mensaje no puede estar vacío.");
                }

                if (string.IsNullOrWhiteSpace(antennaGroup))
                {
                    Log.Warning("El grupo de antena no está especificado.");
                    return BadRequest("El grupo de antena no puede estar vacío.");
                }

                // Enviar el mensaje solo al grupo de la antena
                await _hubContext.Clients.Group(antennaGroup).SendAsync("sendMessage", message);
                Log.Information($"Mensaje enviado correctamente al grupo {antennaGroup}.");
                return Ok(new { success = true, message = $"Mensaje enviado correctamente al grupo {antennaGroup}" });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error al enviar el mensaje desde el controlador.");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

    }
}

