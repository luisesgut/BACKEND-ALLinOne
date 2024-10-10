using Impinj.OctaneSdk;
using LLRP_ANTENNAS.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LLRP_ANTENNAS.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class Antenna2Controller : ControllerBase
    {
        private static ImpinjReader _reader = new ImpinjReader(); // ImpinjReader compartido
        private static HashSet<string> _epcsLeidos = new HashSet<string>(); // Almacenar EPCs �nicos
        private readonly IHubContext<MessageEPC> _hubContext; // Inyecci�n del hub de SignalR
        private readonly HttpClient _httpClient; // Inyecci�n de HttpClient
        private readonly string _readerIp; // IP de esta antena
        private static Timer _keepAliveTimer; // Temporizador para el keep-alive
        private const int MaxReconnectAttempts = 5; // M�ximo n�mero de intentos de reconexi�n
        private int _reconnectAttempts = 0;

        // Constructor con inyecci�n de IHttpClientFactory
        public Antenna2Controller(IHubContext<MessageEPC> hubContext, IHttpClientFactory httpClientFactory)
        {
            _hubContext = hubContext;
            _httpClient = httpClientFactory.CreateClient(); // Crear HttpClient
            _readerIp = "172.16.100.199";
        }

        [HttpPost("start-reading")]
        public IActionResult StartReading([FromBody] int carril)
        {
            try
            {
                if (!_reader.IsConnected)
                {
                    _reader.Connect("172.16.100.199");

                    Settings settings = _reader.QueryDefaultSettings();

                    // Configurar antenas seg�n el carril seleccionado
                    if (carril == 2)
                    {
                        // Activar solo carril 2 (puertos 1 y 2)
                        for (ushort i = 1; i <= 2; i++)
                        {
                            var antenna = settings.Antennas.GetAntenna(i);
                            antenna.IsEnabled = true;
                            antenna.TxPowerInDbm = 20;
                            antenna.RxSensitivityInDbm = -75;
                        }
                        // Desactivar carril 3 (puertos 3 y 4)
                        for (ushort i = 3; i <= 4; i++)
                        {
                            var antenna = settings.Antennas.GetAntenna(i);
                            antenna.IsEnabled = false;
                        }
                        Console.WriteLine("Lectura de EPCs iniciada solo en el carril 2.");
                    }
                    else if (carril == 3)
                    {
                        // Activar solo carril 3 (puertos 3 y 4)
                        for (ushort i = 3; i <= 4; i++)
                        {
                            var antenna = settings.Antennas.GetAntenna(i);
                            antenna.IsEnabled = true;
                            antenna.TxPowerInDbm = 20;
                            antenna.RxSensitivityInDbm = -45;
                        }
                        // Desactivar carril 2 (puertos 1 y 2)
                        for (ushort i = 1; i <= 2; i++)
                        {
                            var antenna = settings.Antennas.GetAntenna(i);
                            antenna.IsEnabled = false;
                        }
                        Console.WriteLine("Lectura de EPCs iniciada solo en el carril 3.");
                    }
                    else if (carril == 0) // Si se selecciona 0, activar ambos carriles
                    {
                        // Activar ambos carriles (puertos 1, 2, 3 y 4)
                        for (ushort i = 1; i <= 4; i++)
                        {
                            var antenna = settings.Antennas.GetAntenna(i);
                            antenna.IsEnabled = true;
                            antenna.TxPowerInDbm = 20;
                            antenna.RxSensitivityInDbm = -45;
                        }
                        Console.WriteLine("Lectura de EPCs iniciada en ambos carriles.");
                    }

                    // Configurar el reporte de etiquetas
                    settings.Report.IncludeAntennaPortNumber = true;
                    settings.Report.IncludeFirstSeenTime = true;
                    settings.Report.IncludeLastSeenTime = true;
                    settings.Report.IncludePeakRssi = true;
                    settings.Report.Mode = ReportMode.Individual;

                    _reader.TagsReported += OnTagsReported;
                    _reader.ApplySettings(settings);
                }

                _reader.Start();

                // Iniciar el temporizador de keep-alive
                StartKeepAlive();
                return Ok("Lectura de EPCs iniciada.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al iniciar la lectura: {ex.Message}");
            }
        }

        //M�todo para intentar reconectar
        private void TryReconnect()
        {
            while (_reconnectAttempts < MaxReconnectAttempts)
            {
                try
                {
                    if (!_reader.IsConnected)
                    {
                        _reader.Connect(_readerIp);
                        Console.WriteLine("Reconexi�n exitosa.");
                        _reconnectAttempts = 0; // Reiniciar contador al conectar con �xito
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _reconnectAttempts++;
                    Console.WriteLine($"Error al intentar reconectar: {ex.Message}. Intento {_reconnectAttempts}/{MaxReconnectAttempts}");

                    // Espera antes del pr�ximo intento de reconexi�n
                    Thread.Sleep(5000); // Esperar 5 segundos
                }
            }

            if (_reconnectAttempts >= MaxReconnectAttempts)
            {
                Console.WriteLine("N�mero m�ximo de intentos de reconexi�n alcanzado. Requiere intervenci�n manual.");
                // Aqu� podr�as lanzar una alerta o loguear un evento cr�tico
            }
        }

        // M�todo para manejar las etiquetas reportadas
        private async void OnTagsReported(ImpinjReader reader, TagReport report)
        {
            string epcPattern = @"^[0-9A-Fa-f]{4} [0-9A-Fa-f]{4} [0-9A-Fa-f]{4} [0-9A-Fa-f]{4}$";

            foreach (Tag tag in report)
            {
                string epc = tag.Epc.ToString();

                // Verificar si el AntennaPortNumber est� presente
                Console.WriteLine($"Tag reportado - EPC: {tag.Epc}, AntennaPortNumber: {tag.AntennaPortNumber}");

                // Verificar si el EPC ya ha sido le�do
                if (!_epcsLeidos.Contains(epc))
                {
                    _epcsLeidos.Add(epc);

                    string carrilEmbarque = string.Empty;
                    string antennaGroupName = string.Empty;

                    if (_readerIp == "172.16.100.199")
                    {
                        if (tag.AntennaPortNumber == 1 || tag.AntennaPortNumber == 2)
                        {
                            carrilEmbarque = "Embarque-Carril-2";
                            antennaGroupName = "Carril2";
                        }
                        else if (tag.AntennaPortNumber == 3 || tag.AntennaPortNumber == 4)
                        {
                            carrilEmbarque = "Embarque-Carril-3";
                            antennaGroupName = "Carril3";
                        }
                    }

                    string formattedFirstSeenTime = tag.FirstSeenTime.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
                    string formattedLastSeenTime = tag.LastSeenTime.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
                    //string antennaGroupName = "Embarque-Carril-2y3"; // grupo de signalR

                    await _hubContext.Clients.Group(antennaGroupName).SendAsync("sendEpc", new
                    {
                        EPC = tag.Epc.ToString(),
                        AntennaPort = tag.AntennaPortNumber,
                        RSSI = $"{tag.PeakRssiInDbm} dBm",
                        FirstSeenTime = formattedFirstSeenTime,
                        LastSeenTime = formattedLastSeenTime,
                        ReaderIP = _readerIp,
                        Carril = carrilEmbarque
                    });

                    Console.WriteLine($"EPC: {tag.Epc}, AntennaPort: {tag.AntennaPortNumber}, RSSI: {tag.PeakRssiInDbm} dBm, Carril: {carrilEmbarque}");

                    // Verificar si el EPC sigue el formato definido
                    bool isValidEpc = Regex.IsMatch(epc, epcPattern);

                    if (isValidEpc)
                    {
                        // EPC v�lido, manipular GPOs dentro de la misma conexi�n
                        Console.WriteLine($"EPC v�lido detectado: {epc}");

                        // Activar GPO 3 (sirena) por 1.5 segundos
                        _reader.SetGpo(3, true);
                        Console.WriteLine("GPO 3 activado (HIGH).");

                        await Task.Delay(1500); // Esperar 1.5 segundos

                        // Desactivar GPO 3
                        _reader.SetGpo(3, false);
                        Console.WriteLine("GPO 3 desactivado (LOW).");
                    }
                    else
                    {
                        // EPC inv�lido, manejar GPO 1
                        Console.WriteLine($"EPC inv�lido detectado: {epc}");

                        // Poner GPO 1 (semaforo) en rojo (HIGH) por 5 segundos
                        _reader.SetGpo(1, true);
                        Console.WriteLine("GPO 1 activado (HIGH).");

                        _reader.SetGpo(3, true); // Activar GPO 3 (sirena)
                        await Task.Delay(5000);  // Esperar 5 segundos

                        // Desactivar ambos GPOs
                        _reader.SetGpo(1, false);
                        _reader.SetGpo(3, false);
                        Console.WriteLine("GPO 1 y GPO 3 desactivados (LOW).");
                    }
                }
            }
        }




        //metodo para detener la lectura por carril o en general
        [HttpPost("stop-reading")]
        public IActionResult StopReading([FromBody] int carril)
        {
            try
            {
                if (_reader.IsConnected)
                {
                    Settings settings = _reader.QueryDefaultSettings();

                    // Desactivar antenas seg�n el carril seleccionado
                    if (carril == 2)
                    {
                        // Desactivar antenas del carril 2 (puertos 1 y 2)
                        for (ushort i = 1; i <= 2; i++)
                        {
                            var antenna = settings.Antennas.GetAntenna(i);
                            antenna.IsEnabled = false;
                        }
                        Console.WriteLine("Lectura de EPCs detenida en el carril 2.");
                    }
                    else if (carril == 3)
                    {
                        // Desactivar antenas del carril 3 (puertos 3 y 4)
                        for (ushort i = 3; i <= 4; i++)
                        {
                            var antenna = settings.Antennas.GetAntenna(i);
                            antenna.IsEnabled = false;
                        }
                        Console.WriteLine("Lectura de EPCs detenida en el carril 3.");
                    }
                    else if (carril == 0) // Si se selecciona 0, detener ambos carriles
                    {
                        // Desactivar todas las antenas (puertos 1, 2, 3 y 4)
                        for (ushort i = 1; i <= 4; i++)
                        {
                            var antenna = settings.Antennas.GetAntenna(i);
                            antenna.IsEnabled = false;
                        }
                        Console.WriteLine("Lectura de EPCs detenida en ambos carriles.");
                    }

                    _reader.ApplySettings(settings); // Aplicar la configuraci�n actualizada

                    _epcsLeidos.Clear(); // Limpiar los EPCs le�dos para una nueva sesi�n
                                         // Detener el temporizador de keep-alive si ambas antenas est�n desactivadas
                    if (carril == 0 || (settings.Antennas.GetAntenna(1).IsEnabled == false && settings.Antennas.GetAntenna(3).IsEnabled == false))
                    {
                        _reader.Stop();  // Detener completamente la lectura
                        StopKeepAlive();
                    }

                    return Ok($"Lectura de EPCs detenida para el carril {carril}.");
                }
                else
                {
                    return BadRequest("El lector no est� conectado.");
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al detener la lectura: {ex.Message}");
            }
        }


        // M�todo para forzar el cierre de la conexi�n
        [HttpPost("force-disconnect")]
        public IActionResult ForceDisconnect([FromBody] int carril)
        {
            try
            {
                if (_reader.IsConnected)
                {
                    Settings settings = _reader.QueryDefaultSettings();

                    // Desactivar antenas seg�n el carril seleccionado
                    if (carril == 2)
                    {
                        // Desactivar antenas del carril 2 (puertos 1 y 2)
                        for (ushort i = 1; i <= 2; i++)
                        {
                            var antenna = settings.Antennas.GetAntenna(i);
                            antenna.IsEnabled = false;
                        }
                        Console.WriteLine("Conexi�n cerrada forzadamente para el carril 2.");
                    }
                    else if (carril == 3)
                    {
                        // Desactivar antenas del carril 3 (puertos 3 y 4)
                        for (ushort i = 3; i <= 4; i++)
                        {
                            var antenna = settings.Antennas.GetAntenna(i);
                            antenna.IsEnabled = false;
                        }
                        Console.WriteLine("Conexi�n cerrada forzadamente para el carril 3.");
                    }
                    else if (carril == 0) // Si se selecciona 0, desconectar todo
                    {
                        _reader.Disconnect(); // Desconectar el lector completamente
                        StopKeepAlive();
                        Console.WriteLine("Conexi�n cerrada forzadamente para ambos carriles.");
                    }

                    _reader.ApplySettings(settings); // Aplicar la configuraci�n actualizada
                    return Ok($"Conexi�n cerrada forzadamente para el carril {carril}.");
                }
                else
                {
                    Console.WriteLine("Lector ya est� desconectado, intentando reconectar...");
                    TryReconnect(); // Intentar reconectar si es necesario
                    return BadRequest("El lector no estaba conectado, reconexi�n requerida.");
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al desconectar: {ex.Message}");
            }
        }


        [HttpGet("verify-settings")]
        public IActionResult VerifySettings([FromQuery] int carril = 0)
        {
            try
            {
                if (!_reader.IsConnected)
                {
                    // Conectar al lector antes de consultar las configuraciones
                    _reader.Connect("172.16.100.199");
                }

                // Obtener la configuraci�n actual del lector
                Settings settings = _reader.QuerySettings();

                var settingsList = new List<object>();

                if (carril == 2)
                {
                    // Verificar solo las configuraciones del carril 2 (puertos 1 y 2)
                    for (ushort i = 1; i <= 2; i++)
                    {
                        var antenna = settings.Antennas.GetAntenna(i);
                        settingsList.Add(new
                        {
                            AntennaPort = i,
                            TxPower = antenna.TxPowerInDbm,
                            RxSensitivity = antenna.RxSensitivityInDbm,
                            IsEnabled = antenna.IsEnabled
                        });
                    }
                    Console.WriteLine("Configuraciones verificadas para el carril 2.");
                }
                else if (carril == 3)
                {
                    // Verificar solo las configuraciones del carril 3 (puertos 3 y 4)
                    for (ushort i = 3; i <= 4; i++)
                    {
                        var antenna = settings.Antennas.GetAntenna(i);
                        settingsList.Add(new
                        {
                            AntennaPort = i,
                            TxPower = antenna.TxPowerInDbm,
                            RxSensitivity = antenna.RxSensitivityInDbm,
                            IsEnabled = antenna.IsEnabled
                        });
                    }
                    Console.WriteLine("Configuraciones verificadas para el carril 3.");
                }
                else
                {
                    // Verificar las configuraciones de todos los puertos (carril 2 y carril 3)
                    for (ushort i = 1; i <= 4; i++)
                    {
                        var antenna = settings.Antennas.GetAntenna(i);
                        settingsList.Add(new
                        {
                            AntennaPort = i,
                            TxPower = antenna.TxPowerInDbm,
                            RxSensitivity = antenna.RxSensitivityInDbm,
                            IsEnabled = antenna.IsEnabled
                        });
                    }
                    Console.WriteLine("Configuraciones verificadas para todos los carriles.");
                }

                return Ok(settingsList);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al verificar la configuraci�n: {ex.Message}");
            }
        }

        // M�todo que env�a el keep-alive simulando una consulta de configuraci�n
        private void SendKeepAlive(object state)
        {
            try
            {
                if (_reader.IsConnected)
                {
                    // Realizar una operaci�n simple para mantener la conexi�n activa
                    var settings = _reader.QuerySettings(); // Consulta de configuraci�n como keep-alive
                    Console.WriteLine("Keep-alive enviado: configuraci�n consultada.");
                }
                else
                {
                    Console.WriteLine("Lector desconectado. Intentando reconectar...");
                    TryReconnect();  // Intenta reconectar si no est� conectado 
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error en keep-alive: {ex.Message}");
            }
        }
        // Inicia el temporizador cuando se establece la conexi�n
        private void StartKeepAlive()
        {
            _keepAliveTimer = new Timer(SendKeepAlive, null, 0, 30000); // Cada 30 segundos
        }

        // Detiene el temporizador cuando se desconecta el lector
        private void StopKeepAlive()
        {
            if (_keepAliveTimer != null)
            {
                _keepAliveTimer.Dispose();
                _keepAliveTimer = null;
            }
        }
    }
}
