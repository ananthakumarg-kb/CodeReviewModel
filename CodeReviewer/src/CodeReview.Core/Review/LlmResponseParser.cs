using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeReview.Core.Review;

public sealed class LlmFinding
{
    [JsonPropertyName("line")] public int Line { get; set; }
    [JsonPropertyName("severity")] public string? Severity { get; set; }
    [JsonPropertyName("rule_id")] public string? RuleId { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("suggestion")] public string? Suggestion { get; set; }
    [JsonPropertyName("confidence")] public double Confidence { get; set; } = 0.7;
}

public sealed class LlmResponse
{
    [JsonPropertyName("findings")] public List<LlmFinding> Findings { get; set; } = new();
}

/// <summary>Tolerant parser: models sometimes wrap JSON in prose or ```json fences. Never trust the format blindly.</summary>
public static class LlmResponseParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static bool TryParse(string text, out List<LlmFinding> findings)
    {
        findings = new List<LlmFinding>();
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return false;

        try
        {
            var parsed = JsonSerializer.Deserialize<LlmResponse>(text[start..(end + 1)], Options);
            if (parsed is null) return false;
            findings = parsed.Findings ?? new List<LlmFinding>();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
