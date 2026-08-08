using System.Text.Json;
using System.Text.Json.Serialization;

namespace CRM.Api.Serialization;

/// <summary>
/// Serializes every DateTime the API emits as UTC ISO-8601 with a trailing 'Z'. SQLite returns
/// DateTimeKind.Unspecified on read (the stored instant IS UTC), which System.Text.Json would
/// otherwise write WITHOUT a zone marker — the browser then parses it as LOCAL time and shows a
/// skewed "x hours ago" (e.g. a just-posted item appearing 5h old in a UTC+5 timezone). This treats
/// Unspecified as UTC and converts Local to UTC, so timestamps are unambiguous everywhere.
/// </summary>
public class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => DateTime.SpecifyKind(reader.GetDateTime(), DateTimeKind.Utc);

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),   // Unspecified (from SQLite) == already UTC
        };
        writer.WriteStringValue(utc.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ"));
    }
}
