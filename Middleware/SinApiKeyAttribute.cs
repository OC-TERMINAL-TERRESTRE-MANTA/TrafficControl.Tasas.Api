namespace TrafficControl.Tasas.Api.Middleware
{
    /// <summary>Marca un endpoint como público (no exige API Key).</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class SinApiKeyAttribute : Attribute { }
}