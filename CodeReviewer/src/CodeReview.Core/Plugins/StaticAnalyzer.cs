using System.Text.RegularExpressions;
using CodeReview.Core.Models;

namespace CodeReview.Core.Plugins;

/// <summary>
/// Deterministic first pass: cheap regex rules over ADDED lines. Findings have confidence 1.0, never hallucinate,
/// and are also given to the LLM as "already reported" so it does not repeat them.
/// </summary>
public sealed class StaticAnalyzer
{
    private sealed record Rule(string Id, Severity Severity, Regex Pattern, string Message, string? Language);

    private static readonly Rule[] Rules =
    {
        new("CS-001", Severity.High, new Regex(@"\basync\s+void\b(?!.*EventArgs)", RegexOptions.Compiled),
            "async void cannot be awaited and crashes the process on exceptions. Return Task instead.", "csharp"),
        new("CS-002", Severity.High, new Regex(@"\.GetAwaiter\(\)\.GetResult\(\)|\.Wait\(\)|\bTask\.WaitAll\(", RegexOptions.Compiled),
            "Blocking on a Task can deadlock and starves the thread pool. Use await.", "csharp"),
        new("CS-004", Severity.Medium, new Regex(@"catch\s*(\(\s*Exception[^)]*\))?\s*\{\s*\}", RegexOptions.Compiled),
            "Empty catch block swallows the exception. Handle, log or rethrow.", "csharp"),
        new("GEN-002", Severity.Info, new Regex(@"\b(?:TODO|FIXME|HACK)\b(?!.*[A-Z]{2,}-\d+)", RegexOptions.Compiled),
            "TODO/FIXME without a ticket reference.", null)
    };

    public IReadOnlyList<ReviewFinding> Analyze(FileDiff file)
    {
        var results = new List<ReviewFinding>();
        var added = file.Hunks.SelectMany(h => h.Lines).Where(l => l.Kind == DiffLineKind.Added);

        foreach (var line in added)
        {
            foreach (var (name, pattern) in SecretPatterns.All)
            {
                if (!pattern.IsMatch(line.Text)) continue;
                results.Add(new ReviewFinding
                {
                    File = file.Path, Line = line.NewLine!.Value, Severity = Severity.Critical, RuleId = "SEC-001",
                    Message = $"Possible {name} committed to source control. Rotate it and load it from a secret store.",
                    Confidence = 1.0, Source = "static"
                });
            }

            foreach (var rule in Rules)
            {
                if (rule.Language is not null && rule.Language != file.Language) continue;
                if (!rule.Pattern.IsMatch(line.Text)) continue;
                results.Add(new ReviewFinding
                {
                    File = file.Path, Line = line.NewLine!.Value, Severity = rule.Severity, RuleId = rule.Id,
                    Message = rule.Message, Confidence = 1.0, Source = "static"
                });
            }
        }

        return results;
    }
}
