using System.Text.Json.Serialization;

namespace TrafficControl.Tasas.Api.Dtos.Response
{
    public class TasaEstadoResponseDto : TasaResponseDto
    {
        [JsonPropertyOrder(1)]             // al final del JSON
        public DateTime? FechaUso { get; set; }
    }
}