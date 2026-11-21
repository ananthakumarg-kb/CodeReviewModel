using System.Text.Json;
using System.Text.Json.Serialization;
using CodeReview.Core.Models;

namespace CodeReview.Core.Standards;

public sealed record StandardRule(
    string Id,
    string Title,
    Severity Severity,
    string Description,
    string[]? Languages = null);

public sealed class StandardsFile
{
    public string Name { get; set; } = "";
    public List<StandardRule> Rules { get; set; } = new();
}

/// <summary>
/// Loads the team's coding standards from *.json files so standards are DATA (versioned in git,
/// changed by PR) and not hard-coded in prompts. Rules without a 'languages' list apply to every language.
/// </summary>
public sealed class StandardsProvider
{
    private readonly List<StandardRule> _rules = new();

    public StandardsProvider(string directory)
    {
        if (!Directory.Exists(directory)) return;

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        foreach (var file in Directory.EnumerateFiles(directory, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            var parsed = JsonSerializer.Deserialize<StandardsFile>(File.ReadAllText(file), options);
            if (parsed?.Rules is not null) _rules.AddRange(parsed.Rules);
        }
    }

    public int Count => _rules.Count;

    public IReadOnlyList<StandardRule> ForLanguage(string language) =>
        _rules.Where(r => r.Languages is null || r.Languages.Length == 0 ||
                          r.Languages.Contains(language, StringComparer.OrdinalIgnoreCase))
              .ToList();

    /// <summary>Compact text form injected into the prompt: one line per rule.</summary>
    public string Format(string language)
    {
        var rules = ForLanguage(language);
        if (rules.Count == 0) return "(no specific standards; apply general engineering best practice)";
        return string.Join("\n", rules.Select(r => $"- [{r.Id}] ({r.Severity}) {r.Title}: {r.Description}"));
    }
}
