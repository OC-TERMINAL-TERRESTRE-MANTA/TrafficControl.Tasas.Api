namespace TrafficControl.Tasas.Api.Utils
{
    public static class Constantes
    {
        public const string FormatoFecha = "yyyy-MM-ddTHH:mm:ss";

        // Estados de TS_ESTADO
        public const string EstadoTasaRegistrada = "0";   // CREADA, no utilizada
        public const string EstadoTasaUtilizada = "1";   // USADA
        public const string EstadoTasaCaducada = "2";   // CADUCADA

        // Funciones para FC_LOGSERVICE
        public const string FuncionInsertarTasa = "InsertarTasa";
        public const string FuncionConsultarEstadoTasa = "ConsultarEstadoTasa";
        public const string FuncionSeguridad = "SEGURIDAD";
        public const string IdTransaccionFallida = "0";

        public static class Codigos
        {
            public const string TasaRegistrada = "TASA_REGISTRADA";
            public const string TasaDuplicada = "TASA_DUPLICADA";
            public const string TasaEncontrada = "TASA_ENCONTRADA";
            public const string TasaNoExiste = "TASA_NO_EXISTE";
            public const string ValidacionError = "VALIDACION_ERROR";
            public const string ErrorInterno = "ERROR_INTERNO";
            public const string ApiOk = "API_OK";
            public const string BdOk = "BD_OK";
            public const string BdError = "BD_ERROR";
            public const string ApiKeyRequerida = "API_KEY_REQUERIDA";
            public const string ApiKeyInvalida = "API_KEY_INVALIDA";
            public const string IpNoPermitida = "IP_NO_PERMITIDA";
            public const string IpBloqueada = "IP_BLOQUEADA";
            public const string LimitePeticiones = "LIMITE_PETICIONES";
            public const string SolicitudMuyGrande = "SOLICITUD_MUY_GRANDE";
            public const string SolicitudInvalida = "SOLICITUD_INVALIDA";
        }
    }
}