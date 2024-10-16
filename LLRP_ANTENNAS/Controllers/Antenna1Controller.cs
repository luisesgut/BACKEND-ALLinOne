﻿using Impinj.OctaneSdk;
using LLRP_ANTENNAS.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;

namespace LLRP_ANTENNAS.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class Antenna1Controller : ControllerBase
    {
        private static ImpinjReader _reader = new ImpinjReader(); // ImpinjReader compartido
        private static HashSet<string> _epcsLeidos = new HashSet<string>(); // Almacenar EPCs únicos
        private readonly IHubContext<MessageEPC> _hubContext; // Inyección del hub de SignalR
        private readonly string _readerIp;
        private static Timer _keepAliveTimer; // Temporizador para el keep-alive
        private const int MaxReconnectAttempts = 5; // Máximo número de intentos de reconexión
        private int _reconnectAttempts = 0;


        public Antenna1Controller(IHubContext<MessageEPC> hubContext)
        {
            _hubContext = hubContext;
            _readerIp = "172.16.100.196";
        }

        // Método para iniciar la lectura de EPCs
        [HttpPost("start-reading")]
        public IActionResult StartReading()
        {
            try
            {
                // Si ya está conectado, no intentamos conectarlo de nuevo
                if (!_reader.IsConnected)
                {
                    // Conectar al lector R700
                    _reader.Connect(_readerIp); // Cambia la IP según tu lector

                    // Obtener la configuración predeterminada y aplicar ajustes
                    Settings settings = _reader.QueryDefaultSettings();

                    // Configurar las antenas con valores específicos para TxPower y RxSensitivity
                    for (ushort i = 1; i <= 4; i++)
                    {
                        var antenna = settings.Antennas.GetAntenna(i);
                        antenna.IsEnabled = true;
                        antenna.TxPowerInDbm = 30;  // Ajustar la potencia de transmisión
                        antenna.RxSensitivityInDbm = -55;  // Ajustar la sensibilidad de recepción
                    }

                    // Configurar el filtro RSSI y otros parámetros de reporte
                    settings.Report.IncludeAntennaPortNumber = true;
                    settings.Report.IncludeFirstSeenTime = true;
                    settings.Report.IncludeLastSeenTime = true;
                    settings.Report.IncludePeakRssi = true;
                    settings.Report.Mode = ReportMode.Individual; // Reportar cada etiqueta individualmente

                    // Método de manejo de la lectura de etiquetas
                    _reader.TagsReported += OnTagsReported;

                    // Aplicar la configuración
                    _reader.ApplySettings(settings);
                }

                // Iniciar la lectura
                _reader.Start();

                // Iniciar el temporizador de keep-alive
                StartKeepAlive();

                Console.WriteLine("Lectura de EPCs iniciada en el carril 1");

                return Ok("Lectura de EPCs iniciada.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al iniciar la lectura: {ex.Message}");
            }
        }

        //Método para intentar reconectar
        private void TryReconnect()
        {
            while (_reconnectAttempts < MaxReconnectAttempts)
            {
                try
                {
                    if (!_reader.IsConnected)
                    {
                        _reader.Connect(_readerIp);
                        Console.WriteLine("Reconexión exitosa.");
                        _reconnectAttempts = 0; // Reiniciar contador al conectar con éxito
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _reconnectAttempts++;
                    Console.WriteLine($"Error al intentar reconectar: {ex.Message}. Intento {_reconnectAttempts}/{MaxReconnectAttempts}");

                    // Espera antes del próximo intento de reconexión
                    Thread.Sleep(5000); // Esperar 5 segundos
                }
            }

            if (_reconnectAttempts >= MaxReconnectAttempts)
            {
                Console.WriteLine("Número máximo de intentos de reconexión alcanzado. Requiere intervención manual.");
                // Aquí podrías lanzar una alerta o loguear un evento crítico
            }
        }

        // Método para manejar las etiquetas reportadas
        private async void OnTagsReported(ImpinjReader reader, TagReport report)
        {
            string epcPattern = @"^[0-9A-Fa-f]{4} [0-9A-Fa-f]{4} [0-9A-Fa-f]{4} [0-9A-Fa-f]{4}$";

            foreach (Tag tag in report)
            {
                string epc = tag.Epc.ToString();

                // Verificar si el EPC ya ha sido leído
                if (!_epcsLeidos.Contains(epc))
                {
                    _epcsLeidos.Add(epc);

                    string carrilEmbarque = string.Empty;

                    if (_readerIp == "172.16.100.196")
                    {
                        if (tag.AntennaPortNumber == 1 || tag.AntennaPortNumber == 2)
                        {
                            carrilEmbarque = "Carril de Embarque 1";
                        }
                    }

                    string formattedFirstSeenTime = tag.FirstSeenTime.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
                    string formattedLastSeenTime = tag.LastSeenTime.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");

                    string antennaGroupName = "Embarque-Carril-1"; // grupo de signalR
                    
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
                        // EPC válido, manipular GPOs dentro de la misma conexión
                        Console.WriteLine($"EPC válido detectado: {epc}");

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
                        // EPC inválido, manejar GPO 1
                        Console.WriteLine($"EPC inválido detectado: {epc}");

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

        // Método para detener la lectura de EPCs y desconectar
        [HttpPost("stop-reading")]
        public IActionResult StopReading()
        {
            try
            {
                if (_reader.IsConnected)
                {
                    _reader.Stop();  // Detener la lectura
                    _epcsLeidos.Clear(); // Limpiar los EPCs leídos para una nueva sesión

                    // Detener el temporizador de keep-alive
                    StopKeepAlive();

                    Console.WriteLine("Lectura de EPCs detenida en el carril 1");

                    return Ok("Lectura detenida.");
                }
                else
                {
                    return BadRequest("El lector no está conectado.");
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al detener la lectura: {ex.Message}");
            }
        }

        // Método para forzar el cierre de la conexión
        [HttpPost("force-disconnect")]
        public IActionResult ForceDisconnect()
        {
            try
            {
                if (_reader.IsConnected)
                {
                    _reader.Disconnect(); // Desconectar el lector
                    StopKeepAlive(); // Detener el temporizador de keep-alive
                    Console.WriteLine("Lectura de EPCs terminada forzadamente en el carril 1");
                    return Ok("Conexión cerrada forzadamente.");
                }
                else
                {
                    Console.WriteLine("Lector ya está desconectado, intentando reconectar...");
                    TryReconnect(); // Intentar reconectar si es necesario
                    return BadRequest("El lector no estaba conectado, reconexión requerida.");
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al desconectar: {ex.Message}");
            }
        }


        // Método para verificar las configuraciones actuales de las antenas
        [HttpGet("verify-settings")]
        public IActionResult VerifySettings()
        {
            try
            {
                if (!_reader.IsConnected)
                {
                    // Conectar al lector antes de consultar las configuraciones
                    _reader.Connect("172.16.100.196");
                }

                // Obtener la configuración actual del lector
                Settings settings = _reader.QuerySettings();

                var settingsList = new List<object>();

                for (ushort i = 1; i <= 4; i++)
                {
                    var antenna = settings.Antennas.GetAntenna(i);
                    settingsList.Add(new
                    {
                        AntennaPort = i,
                        TxPower = antenna.TxPowerInDbm,
                        RxSensitivity = antenna.RxSensitivityInDbm
                    });
                }

                return Ok(settingsList);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al verificar la configuración: {ex.Message}");
            }
        }

        // Método que envía el keep-alive simulando una consulta de configuración
        private void SendKeepAlive(object state)
        {
            try
            {
                if (_reader.IsConnected)
                {
                    // Realizar una operación simple para mantener la conexión activa
                    var settings = _reader.QuerySettings(); // Consulta de configuración como keep-alive
                    Console.WriteLine("Keep-alive enviado: configuración consultada.");
                }
                else
                {
                    Console.WriteLine("Lector desconectado. Intentando reconectar...");
                    TryReconnect();  // Intenta reconectar si no está conectado
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error en keep-alive: {ex.Message}");
            }
        }

        // Inicia el temporizador cuando se establece la conexión
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
