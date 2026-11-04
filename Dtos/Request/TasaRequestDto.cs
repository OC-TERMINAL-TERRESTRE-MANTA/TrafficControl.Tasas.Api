namespace TrafficControl.Tasas.Api.Dtos.Request
{
    public class TasaRequestDto
    {
        public string? CodigoTasa { get; set; }
        public decimal? ValorTasa { get; set; }
        public string? FechaDesde { get; set; }
        public string? FechaViaje { get; set; }
        public string? FechaHasta { get; set; }
        public string? IdViaje { get; set; }
        public string? IdCooperativa { get; set; }
        public string? IdSilo { get; set; }
        public string? Usuario { get; set; }
    }
}