namespace CodeReview.Core.Models;

public enum Severity { Info = 0, Low = 1, Medium = 2, High = 3, Critical = 4 }

public enum ReviewVerdict { Approve, Comment, RequestChanges }

public enum DiffLineKind { Context, Added, Removed }

/// <summary>One line of a unified diff. NewLine is null for removed lines, OldLine is null for added lines.</summary>
public sealed record DiffLine(DiffLineKind Kind, int? OldLine, int? NewLine, string Text);

public sealed record DiffHunk(int OldStart, int NewStart, string Header, IReadOnlyList<DiffLine> Lines);

/// <summary>All changes to a single file in the pull request.</summary>
public sealed record FileDiff(string Path, bool IsDeleted, bool IsBinary, IReadOnlyList<DiffHunk> Hunks)
{
    public string Language => LanguageDetector.FromPath(Path);

    /// <summary>New-file line numbers of every added line. Only these lines may receive a comment.</summary>
    public int[] AddedLines => Hunks.SelectMany(h => h.Lines)
        .Where(l => l.Kind == DiffLineKind.Added)
        .Select(l => l.NewLine!.Value)
        .ToArray();

    public int AddedCount => Hunks.Sum(h => h.Lines.Count(l => l.Kind == DiffLineKind.Added));
}

public sealed record ReviewFinding
{
    public required string File { get; init; }
    public int Line { get; init; }
    public Severity Severity { get; init; } = Severity.Medium;
    public string RuleId { get; init; } = "general";
    public required string Message { get; init; }
    public string? Suggestion { get; init; }
    public double Confidence { get; init; } = 0.7;
    /// <summary>"static" (deterministic) or "llm".</summary>
    public string Source { get; init; } = "llm";
}

public sealed record ReviewReport(
    IReadOnlyList<ReviewFinding> Findings,
    string Summary,
    ReviewVerdict Verdict,
    int FilesReviewed,
    int FilesSkipped);

public sealed record PullRequestRef(string Owner, string Repo, int Number);

public static class LanguageDetector
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "csharp", [".ts"] = "typescript", [".tsx"] = "typescript", [".js"] = "javascript",
        [".jsx"] = "javascript", [".py"] = "python", [".java"] = "java", [".cpp"] = "cpp", [".cc"] = "cpp",
        [".h"] = "cpp", [".hpp"] = "cpp", [".c"] = "c", [".go"] = "go", [".sql"] = "sql",
        [".json"] = "json", [".yml"] = "yaml", [".yaml"] = "yaml", [".xaml"] = "xaml"
    };

    public static string FromPath(string path) =>
        Map.TryGetValue(System.IO.Path.GetExtension(path), out var lang) ? lang : "unknown";
}
