namespace TrafficControl.Tasas.Api.Services.Interfaces
{
    public interface ILogService
    {
        Task RegistrarAsync(string funcion, object? request, object? response, long milisegundos,
                     bool error, Exception? excepcion = null);
    }
}