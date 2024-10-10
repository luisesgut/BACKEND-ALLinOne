using Impinj.OctaneSdk;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace LLRP_ANTENNAS.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GpoController : ControllerBase
    {
        private static ImpinjReader _reader = new ImpinjReader(); // ImpinjReader compartido

        // Método para manejar un EPC válido
        [HttpPost("valid-epc")]
        public async Task<IActionResult> HandleValidEpc()
        {
            try
            {
                if (!_reader.IsConnected)
                {
                    _reader.Connect("172.16.100.199"); // Cambia la IP según tu lector
                }

                // Mantener GPO 1 (semaforo) en verde (LOW)
                _reader.SetGpo(1, false);
                Console.WriteLine("GPO 1 en verde (LOW).");

                // Activar GPO 3 (sirena) por 1.5 segundos
                _reader.SetGpo(3, true);
                Console.WriteLine("GPO 3 activado (HIGH).");

                // Esperar 1.5 segundos
                await Task.Delay(1000);

                // Desactivar GPO 3 (sirena)
                _reader.SetGpo(3, false);
                Console.WriteLine("GPO 3 desactivado (LOW).");

                return Ok("EPC válido procesado. GPO 1 en verde, GPO 3 activado por 1.5 segundos.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al procesar el EPC válido: {ex.Message}");
            }
        }

        // Método para manejar un EPC inválido
        [HttpPost("invalid-epc")]
        public async Task<IActionResult> HandleInvalidEpc()
        {
            try
            {
                if (!_reader.IsConnected)
                {
                    _reader.Connect("172.16.100.199"); // Cambia la IP según tu lector
                }

                // Poner GPO 1 (semaforo) en rojo (HIGH)
                _reader.SetGpo(1, true);
                Console.WriteLine("GPO 1 en rojo (HIGH).");

                // Activar GPO 3 (sirena) por 5 segundos
                _reader.SetGpo(3, true);
                Console.WriteLine("GPO 3 activado (HIGH).");

                // Esperar 5 segundos
                await Task.Delay(5000);

                // Desactivar GPO 3 (sirena)
                _reader.SetGpo(3, false);
                Console.WriteLine("GPO 3 desactivado (LOW).");

                return Ok("EPC inválido procesado. GPO 1 en rojo, GPO 3 activado por 5 segundos.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al procesar el EPC inválido: {ex.Message}");
            }
        }

        [HttpPost("deactivate-all-gpos")]
        public IActionResult DeactivateAllGpos()
        {
            try
            {
                if (!_reader.IsConnected)
                {
                    _reader.Connect("172.16.100.199"); // Cambia la IP según tu lector
                }

                // Desactivar todos los GPOs (1, 2 y 3)
                for (ushort gpoPort = 1; gpoPort <= 3; gpoPort++)
                {
                    _reader.SetGpo(gpoPort, false); // Establecer cada GPO en LOW
                    Console.WriteLine($"GPO {gpoPort} desactivado (LOW).");
                }

                return Ok("Todos los GPOs han sido desactivados.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al desactivar los GPOs: {ex.Message}");
            }
        }
    }
}
