using System.Text.Json;
using TaskbarBanner.Core.Reporting;
using Xunit;

namespace TaskbarBanner.Core.Tests;

public class ReportPayloadJsonTests
{
    [Fact]
    public void Payload_UsesCamelCaseContract_WithAllExpectedFields()
    {
        DateTimeOffset start = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        VerifiedMinute[] minutes =
        {
            MinuteFactory.Make("key-1", start),
            MinuteFactory.Make("key-2", start.AddMinutes(1)),
        };

        ReportPayload payload = ReportPayloadFactory.Build(minutes);
        string json = ReportPayloadJson.Serialize(payload);

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);

        JsonElement array = root.GetProperty("minutes");
        Assert.Equal(2, array.GetArrayLength());

        JsonElement first = array[0];
        Assert.Equal("key-1", first.GetProperty("idempotencyKey").GetString());
        Assert.Equal("session-1", first.GetProperty("sessionId").GetString());
        Assert.Equal("device-1", first.GetProperty("deviceId").GetString());
        Assert.Equal(start, DateTimeOffset.Parse(first.GetProperty("minuteStartUtc").GetString()!));
        Assert.Equal(start.AddMinutes(1), DateTimeOffset.Parse(first.GetProperty("minuteEndUtc").GetString()!));
    }
}
