using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrafficControl.Tasas.Api.Utils
{
    public class DateTimeIsoConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => DateTime.ParseExact(reader.GetString()!, Constantes.FormatoFecha, CultureInfo.InvariantCulture);

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString(Constantes.FormatoFecha, CultureInfo.InvariantCulture));
    }
}