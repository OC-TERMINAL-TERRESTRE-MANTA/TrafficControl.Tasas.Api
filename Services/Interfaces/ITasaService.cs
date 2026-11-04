using TrafficControl.Tasas.Api.Dtos.Request;
using TrafficControl.Tasas.Api.Dtos.Response;

namespace TrafficControl.Tasas.Api.Services.Interfaces
{
    public interface ITasaService
    {
        Task<ApiResponse<TasaResponseDto>> InsertarTasaAsync(TasaRequestDto request);
        Task<ApiResponse<TasaEstadoResponseDto>> ConsultarEstadoAsync(string codigoTasa);
    }
}