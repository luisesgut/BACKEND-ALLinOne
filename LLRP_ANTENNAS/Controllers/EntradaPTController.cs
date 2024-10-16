using Impinj.OctaneSdk;
using LLRP_ANTENNAS.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace LLRP_ANTENNAS.Controllers
    
{
    [ApiController]
    [Route("api/[controller]")]
    public class EntradaPTController : ControllerBase
    {
        private static ImpinjReader _reader = new ImpinjReader(); // ImpinjReader compartido
        private static HashSet<string> _epcsLeidos = new HashSet<string>(); // Almacenar EPCs únicos
        private readonly IHubContext<MessageEPC> _hubContext; // Inyección del hub de SignalR
        private readonly string _readerIp;
        private static Timer _keepAliveTimer; // Temporizador para el keep-alive
        private const int MaxReconnectAttempts = 5; // Máximo número de intentos de reconexión
        private int _reconnectAttempts = 0;
        private List<Tag> _etiquetasTemporales = new List<Tag>(); // Almacena temporalmente las etiquetas
        private HashSet<string> _epcsEnviados = new HashSet<string>(); // Almacena EPCs ya enviados
        private Timer _eventoTarimaTimer; // Temporizador para agrupar eventos
        private Dictionary<string, ushort> _ultimaAntenaPorEpc = new Dictionary<string, ushort>(); // Última antena por EPC
        private double _txPowerInDbm = 30;  // Valor por defecto
        private double _rxSensitivityInDbm = -80;  // Valor por defecto
        private DateTime? _startTime; // Almacena el tiempo de inicio de la lectura
        private int _totalTagsLeidos = 0; // Almacena el número de etiquetas leídas

        public EntradaPTController(IHubContext<MessageEPC> hubContext)
        {
            _hubContext = hubContext;
            _readerIp = "172.16.100.198";
        }

        [HttpPost("connect")]
        public IActionResult ConnectReader()
        {
            try
            {
                if (_reader.IsConnected)
                {
                    return Ok("El lector ya está conectado.");
                }

                _reader.Connect(_readerIp); // Cambia la IP según tu configuración
                Console.WriteLine("Lector conectado exitosamente.");

                return Ok("Conexión con el lector establecida.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al conectar con el lector: {ex.Message}");
            }
        }



        [HttpPost("configurar-antenas")]
        public IActionResult ConfigurarAntenas([FromBody] ConfiguracionAntenaRequest configuracion)
        {
            try
            {
                if (!_reader.IsConnected)
                {
                    return BadRequest("El lector no está conectado.");
                }

                // Guardar los valores recibidos
                _txPowerInDbm = configuracion.TxPower;
                _rxSensitivityInDbm = configuracion.RxSensitivity;

                // Aplicar la configuración a las 13 antenas
                var settings = _reader.QuerySettings();
                for (ushort i = 1; i <= 13; i++)
                {
                    var antenna = settings.Antennas.GetAntenna(i);
                    antenna.IsEnabled = true;
                    antenna.TxPowerInDbm = _txPowerInDbm;
                    antenna.RxSensitivityInDbm = _rxSensitivityInDbm;
                }

                _reader.ApplySettings(settings);
                return Ok("Configuración aplicada a las 13 antenas.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al configurar las antenas: {ex.Message}");
            }
        }

        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            if (!_reader.IsConnected)
            {
                return Ok(new { mensaje = "El lector no está conectado." });
            }

            if (_startTime == null)
            {
                return Ok(new { mensaje = "La lectura no ha iniciado." });
            }

            // Calcular el tiempo transcurrido desde el inicio de la lectura
            var tiempoActivo = DateTime.Now - _startTime.Value;

            return Ok(new
            {
                mensaje = "La lectura está activa.",
                tiempoActivo = tiempoActivo.ToString(@"hh\:mm\:ss"),
                totalTagsLeidos = _totalTagsLeidos
            });
        }

        // Clase para recibir los valores de configuración
        public class ConfiguracionAntenaRequest
        {
            public double TxPower { get; set; }  // Potencia de transmisión (dBm)
            public double RxSensitivity { get; set; }  // Sensibilidad de recepción (dBm)
        }


        [HttpPost("start-reading")]
        public IActionResult StartReading()
        {
            try
            {
                // Si ya está conectado y la lectura está activa, no iniciar de nuevo
                if (_reader.IsConnected && _startTime != null)
                {
                    return Ok("La lectura ya está en curso.");
                }

                // Conectar si no está conectado
                if (!_reader.IsConnected)
                {
                    _reader.Connect(_readerIp);
                }

                // Obtener la configuración y aplicar ajustes
                var settings = _reader.QueryDefaultSettings();
                for (ushort i = 1; i <= 13; i++)
                {
                    var antenna = settings.Antennas.GetAntenna(i);
                    antenna.IsEnabled = true;
                    antenna.TxPowerInDbm = _txPowerInDbm;
                    antenna.RxSensitivityInDbm = _rxSensitivityInDbm;
                }

                settings.Report.IncludeAntennaPortNumber = true;
                settings.Report.IncludeFirstSeenTime = true;
                settings.Report.IncludeLastSeenTime = true;
                settings.Report.IncludePeakRssi = true;
                settings.Report.Mode = ReportMode.Individual;

                // Asignar el evento solo una vez para evitar duplicados
                _reader.TagsReported -= OnTagsReported; // Eliminar si ya estaba asignado
                _reader.TagsReported += OnTagsReported;

                // Aplicar la configuración y comenzar la lectura
                _reader.ApplySettings(settings);
                _reader.Start();

                // Guardar el tiempo de inicio y reiniciar el contador
                _startTime = DateTime.Now;
                _totalTagsLeidos = 0;

                // Iniciar keep-alive
                StartKeepAlive();

                Console.WriteLine("Lectura de EPCs iniciada.");
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

        //metodo asincrono que recibe como parametro el lector y el reporte de las etiquetas
        private async void OnTagsReported(ImpinjReader reader, TagReport report)
        {
            //lock sirve para limitar los hilos que pueden acceder a un recurso, y asegurarse que sea uno a la vez
            lock (_etiquetasTemporales)
            {
                //por cada etiqueta en el reporte
                foreach (var tag in report.Tags)
                {
                    //pasa el epc a string
                    string epc = tag.Epc.ToString();
                    //pasa el puerto de la antena a ushort
                    ushort antennaPort = tag.AntennaPortNumber;

                    // Verificar si la etiqueta ya fue enviada
                    if (!_epcsEnviados.Contains(epc))
                    {
                        _totalTagsLeidos++; // Incrementar contador de etiquetas únicas
                                          
                         // Guardar la etiqueta en la lista temporal
                        _etiquetasTemporales.Add(tag);
                        _epcsEnviados.Add(epc); // Marcar EPC como enviado

                        // Guardar la última antena donde fue leída
                        _ultimaAntenaPorEpc[epc] = antennaPort;
                    } else {
                        Console.WriteLine($"EPC {epc} ya fue registrada.");
                    }
                }
            }

            // Inicia el temporizador si no está activo
            // si la variable _eventoTarimaTimer es nula significa que no hay un temporizador activo
            if (_eventoTarimaTimer == null)
            {
                // asi que crea un nuevo timer en este caso ejecutara el metodo EnviarEventoTarima cada 5 segundos
                _eventoTarimaTimer = new Timer(EnviarEventoTarima, null, 5000, Timeout.Infinite); // 5 segundos
            }
        }

        // Método para enviar las etiquetas agrupadas al cliente
        private async void EnviarEventoTarima(object state)
        {
            List<Tag> etiquetas;

            // Copia las etiquetas y limpia la lista temporal
            lock (_etiquetasTemporales)
            {
                etiquetas = new List<Tag>(_etiquetasTemporales);
                _etiquetasTemporales.Clear();
            }

            var grupo = "EntradaPT";

            // Genera la lista de EPCs con información adicional
            var listaEPCs = etiquetas.Select(tag =>
            {
                string epc = tag.Epc.ToString();
                ushort antennaPort = tag.AntennaPortNumber;

                // Determinar la dirección del movimiento
                string direccion = DeterminarDireccion(epc, antennaPort);

                return new
                {
                    EPC = epc,
                    AntennaPort = antennaPort,
                    RSSI = $"{tag.PeakRssiInDbm} dBm",
                    FirstSeenTime = tag.FirstSeenTime.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    LastSeenTime = tag.LastSeenTime.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    Direction = direccion,
                    ReaderIP = _readerIp
                };
            }).ToList();

            // Enviar los datos al grupo de clientes usando SignalR
            await _hubContext.Clients.Group(grupo).SendAsync("sendEpcs", new { Tags = listaEPCs });

            Console.WriteLine($"Evento enviado con {etiquetas.Count} etiquetas.");

            // Reiniciar el temporizador
            _eventoTarimaTimer.Dispose();
            _eventoTarimaTimer = null;
        }

        // Método para determinar la dirección según las antenas leídas
        private string DeterminarDireccion(string epc, ushort antennaPort)
        {
            if (_ultimaAntenaPorEpc.TryGetValue(epc, out ushort ultimaAntena))
            {
                // Definir zonas según las antenas (1-6: Entrada, 7-13: Salida)
                bool desdeZona2 = ultimaAntena >= 1 && ultimaAntena <= 6;
                bool haciaZona3 = antennaPort >= 7 && antennaPort <= 13;

                bool desdeZona3 = ultimaAntena >= 7 && ultimaAntena <= 13;
                bool haciaZona2 = antennaPort >= 1 && antennaPort <= 6;

                // Determinar la dirección con base en el cambio de zona
                if (desdeZona2 && haciaZona3)
                    return "Entrada";
                else if (desdeZona3 && haciaZona2)
                    return "Salida";
                else
                    return "Estático";
            }
            else
            {
                // Si no hay lectura previa, marcar como indeterminado
                return "Indeterminado";
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

                    Console.WriteLine("Lectura de EPCs detenida en la entrada de Producto Terminado");

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
                    Console.WriteLine("Lectura de EPCs terminada forzadamente en la entrada de Producto terminado");
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
                    _reader.Connect("172.16.100.198");
                }

                // Obtener la configuración actual del lector
                Settings settings = _reader.QuerySettings();

                var settingsList = new List<object>();

                for (ushort i = 1; i <= 13; i++)
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
