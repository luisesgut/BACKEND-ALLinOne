using Serilog;
using Prometheus; // Agregar Prometheus
using LLRP_ANTENNAS.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Configurar Serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()  // Registrar eventos en la consola
    .WriteTo.File("logs/log.txt", rollingInterval: RollingInterval.Day)  // Registrar eventos en un archivo de texto diario
    .CreateLogger();

// Reemplazar el sistema de logging predeterminado por Serilog
builder.Host.UseSerilog();

// Habilitar CORS para el frontend (React u otro cliente)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .SetIsOriginAllowed(origin => true)  // Permitir todas las solicitudes de origen
              .AllowCredentials();                 // Permitir las credenciales necesarias para SignalR
    });
});

// Agregar servicios de SignalR, controladores y Prometheus
builder.Services.AddHttpClient();
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configurar el pipeline de la aplicaci�n
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "API v1");
        c.RoutePrefix = "swagger";
    });
}

// Usar CORS con la pol�tica configurada
app.UseCors("AllowAll");

// Habilitar Prometheus para recolectar m�tricas
app.UseHttpMetrics();  // Captura m�tricas HTTP autom�ticamente

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();
    endpoints.MapHub<MessageEPC>("/message");

    // Exponer las m�tricas en la ruta /metrics para que Prometheus las recolecte
    endpoints.MapMetrics();
});

try
{
    Log.Information("Iniciando la aplicaci�n...");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "La aplicaci�n fall� al iniciar.");
}
finally
{
    Log.CloseAndFlush();
}
