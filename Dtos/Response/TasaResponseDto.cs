using System.Text.Json.Serialization;

namespace TrafficControl.Tasas.Api.Dtos.Response
{
    public class TasaResponseDto
    {
        [JsonIgnore]
        public long IdTasa { get; set; }
        public string CodigoTasa { get; set; } = string.Empty;
        public decimal? ValorTasa { get; set; }
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
        public int? IdCooperativa { get; set; }
        public string? Estado { get; set; }
        public string? DescripcionEstado { get; set; }
        public DateTime? FechaRegistro { get; set; }
       
    }
}