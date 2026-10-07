using System.Text.Json;

namespace ControllerOS.Core.Controls;

public static class StateJson
{
    public static string Serialize(ControllerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("timestampTicks", state.Timestamp.Ticks);
            writer.WriteStartArray("controls");
            foreach (ControlValue value in state.Values.Values.OrderBy(value => value.Id))
            {
                writer.WriteStartObject();
                writer.WriteString("id", value.Id.ToString());
                writer.WriteString("kind", value.Kind.ToString());
                writer.WriteNumber("value", value.Value);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
