namespace TrafficControl.Tasas.Api.Utils
{
    public class SeguridadOptions
    {
        public string ApiKeyHeader { get; set; } = "X-API-KEY";
        public List<string> ApiKeys { get; set; } = new();
        public bool MostrarDetalleErrores { get; set; } = false;
        /// <summary>Vacía = se permiten todas las IPs.</summary>
        public List<string> IpsPermitidas { get; set; } = new();
        public int PeticionesPorSegundoPorIp { get; set; } = 300;
        public int MaxIntentosFallidos { get; set; } = 10;
        public int MinutosBloqueo { get; set; } = 15;
        public int MaxTamanoBodyKb { get; set; } = 16;
    }
}