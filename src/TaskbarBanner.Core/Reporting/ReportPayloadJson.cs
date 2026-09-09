using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskbarBanner.Core.Reporting;

public static class ReportPayloadJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(ReportPayload payload)
        => JsonSerializer.Serialize(payload, Options);
}
