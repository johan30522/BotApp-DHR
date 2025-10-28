namespace BotApp.Services
{
    public class CodigoVerificacionService
    {
        private readonly SessionStateStore _state;
        private readonly ILogger<CodigoVerificacionService> _logger;
        private readonly TimeSpan _ttl;
        private const string Namespace = "codexpediente";

        public CodigoVerificacionService(SessionStateStore state, IConfiguration cfg, ILogger<CodigoVerificacionService> logger)
        {
            _state = state;
            _logger = logger;
            // TTL configurable (minutos)
            var ttlMinutes = cfg.GetValue<int>("Expedientes:CodigoTTL", 10);
            _ttl = TimeSpan.FromMinutes(ttlMinutes);
        }

        /// <summary>
        /// Genera y guarda un código de verificación aleatorio con TTL.
        /// </summary>
        public async Task<string> GenerarAsync(string numeroExpediente)
        {
            var codigo = new Random().Next(100000, 999999).ToString();
            await _state.CacheSetAsync(Namespace, numeroExpediente, codigo, _ttl);
            _logger.LogInformation("Código {Codigo} generado para expediente {NumeroExpediente}", codigo, numeroExpediente);
            return codigo;
        }

        /// <summary>
        /// Valida si el código es correcto y no ha expirado.
        /// </summary>
        public async Task<bool> ValidarAsync(string numeroExpediente, string codigoIngresado)
        {
            var guardado = await _state.CacheGetAsync(Namespace, numeroExpediente);
            if (guardado == null)
            {
                _logger.LogWarning("El código para expediente {NumeroExpediente} ha expirado o no existe.", numeroExpediente);
                return false;
            }

            var esValido = guardado == codigoIngresado;
            _logger.LogInformation("Validación de código para expediente {NumeroExpediente}: {Resultado}",
                numeroExpediente, esValido ? "válido" : "inválido");

            if (esValido)
            {
                // Eliminamos el código tras validación exitosa
                await _state.CacheDelAsync(Namespace, numeroExpediente);
            }

            return esValido;
        }
    }
}
