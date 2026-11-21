using CodeReview.Core.Models;

namespace CodeReview.Core.Review;

public sealed class ReviewOptions
{
    public int MaxCharsPerChunk { get; set; } = 12000;   // ~3k tokens of diff per LLM call
    public int MaxConcurrency { get; set; } = 4;          // parallel files (respect provider rate limits)
    public int MaxFiles { get; set; } = 50;               // safety valve for huge PRs
    public double MinConfidence { get; set; } = 0.6;      // drop speculative LLM findings
    public int MaxFindingsPerFile { get; set; } = 10;     // avoid comment floods
    public Severity MinSeverity { get; set; } = Severity.Low;
    public Severity BlockOnSeverity { get; set; } = Severity.High;
    public string[] IgnorePatterns { get; set; } =
    {
        "*.lock", "package-lock.json", "*.min.js", "*.designer.cs", "*.g.cs", "*.generated.cs",
        "*.png", "*.jpg", "*.svg", "*.pdf", "*.dll", "*.snap"
    };
}
