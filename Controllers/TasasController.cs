using Microsoft.AspNetCore.Mvc;
using TrafficControl.Tasas.Api.Dtos.Request;
using TrafficControl.Tasas.Api.Dtos.Response;
using TrafficControl.Tasas.Api.Services.Interfaces;

namespace TrafficControl.Tasas.Api.Controllers
{
    [ApiController]
    [Route("api/v1/tasas")]
    [Produces("application/json")]
    public class TasasController : ControllerBase
    {
        private readonly ITasaService _tasaService;

        public TasasController(ITasaService tasaService)
        {
            _tasaService = tasaService;
        }

        /// <summary>Registra una tasa enviada por el ERP Olympo.</summary>
        [HttpPost]
        [ProducesResponseType(typeof(ApiResponse<TasaResponseDto>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ApiResponse<TasaResponseDto>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<TasaResponseDto>), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ApiResponse<TasaResponseDto>), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> InsertarTasa([FromBody] TasaRequestDto request)
        {
            var respuesta = await _tasaService.InsertarTasaAsync(request);
            return StatusCode(respuesta.HttpStatus, respuesta);
        }


        /// <summary>Consulta el estado de una tasa (0 = no utilizada, 1 = utilizada).</summary>
        [HttpGet("{codigoTasa}/estado")]
        [ProducesResponseType(typeof(ApiResponse<TasaEstadoResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<TasaEstadoResponseDto>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<TasaEstadoResponseDto>), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ApiResponse<TasaEstadoResponseDto>), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ConsultarEstado([FromRoute] string codigoTasa)
        {
            var respuesta = await _tasaService.ConsultarEstadoAsync(codigoTasa);
            return StatusCode(respuesta.HttpStatus, respuesta);
        }
    }
}