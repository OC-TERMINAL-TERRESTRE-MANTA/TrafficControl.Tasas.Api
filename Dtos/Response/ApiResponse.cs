using System.Text.Json.Serialization;

namespace TrafficControl.Tasas.Api.Dtos.Response
{
    public class ApiResponse<T>
    {
        public bool Exito { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Mensaje { get; set; } = string.Empty;
        public string IdTransaccion { get; set; } = string.Empty;
        public DateTime FechaRespuesta { get; set; } = DateTime.Now;
        public T? Datos { get; set; }
        public List<string> Errores { get; set; } = new();

        [JsonIgnore]
        public int HttpStatus { get; set; } = StatusCodes.Status200OK;
    }
}