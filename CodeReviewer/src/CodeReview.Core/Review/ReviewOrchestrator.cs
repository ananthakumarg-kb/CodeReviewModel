using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using CodeReview.Core.Diff;
using CodeReview.Core.Models;
using CodeReview.Core.Plugins;
using Microsoft.Extensions.Logging;

namespace CodeReview.Core.Review;

/// <summary>
/// The pipeline: parse diff -> select files -> (static analysis + LLM review per file, in parallel)
/// -> post-process (confidence filter, dedupe, cap, sort) -> verdict (deterministic) -> summary (LLM).
/// Principle: the LLM PROPOSES findings; deterministic code VALIDATES, FILTERS and DECIDES.
/// </summary>
public sealed class ReviewOrchestrator
{
    private readonly LlmFileReviewer _reviewer;
    private readonly PrSummarizer _summarizer;
    private readonly StaticAnalyzer _analyzer;
    private readonly ReviewOptions _options;
    private readonly ILogger<ReviewOrchestrator> _log;
    private readonly Regex[] _ignore;

    public ReviewOrchestrator(LlmFileReviewer reviewer, PrSummarizer summarizer, StaticAnalyzer analyzer,
        ReviewOptions options, ILogger<ReviewOrchestrator> log)
    {
        _reviewer = reviewer;
        _summarizer = summarizer;
        _analyzer = analyzer;
        _options = options;
        _log = log;
        _ignore = options.IgnorePatterns
            .Select(p => new Regex("(^|.*/)" + Regex.Escape(p).Replace("\\*", "[^/]*") + "$", RegexOptions.IgnoreCase))
            .ToArray();
    }

    public async Task<ReviewReport> ReviewAsync(string unifiedDiff, string? prTitle, CancellationToken ct = default)
    {
        var allFiles = UnifiedDiffParser.Parse(unifiedDiff);
        var toReview = allFiles
            .Where(f => !f.IsDeleted && !f.IsBinary && f.AddedCount > 0 && !_ignore.Any(r => r.IsMatch(f.Path)))
            .Take(_options.MaxFiles)
            .ToList();
        _log.LogInformation("PR has {Total} files; reviewing {Count}", allFiles.Count, toReview.Count);

        var collected = new ConcurrentBag<ReviewFinding>();

        await Parallel.ForEachAsync(toReview,
            new ParallelOptions { MaxDegreeOfParallelism = _options.MaxConcurrency, CancellationToken = ct },
            async (file, token) =>
            {
                var staticFindings = _analyzer.Analyze(file);
                IReadOnlyList<ReviewFinding> llmFindings = Array.Empty<ReviewFinding>();
                try
                {
                    llmFindings = await _reviewer.ReviewAsync(file, staticFindings, _options.MaxCharsPerChunk, token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Degrade gracefully: one failed file must not lose the static findings or the other files.
                    _log.LogError(ex, "LLM review failed for {File}; keeping static findings only", file.Path);
                }

                foreach (var f in PostProcess(staticFindings.Concat(llmFindings)))
                    collected.Add(f);
            });

        var findings = collected
            .Where(f => f.Severity >= _options.MinSeverity)
            .OrderByDescending(f => f.Severity).ThenBy(f => f.File, StringComparer.Ordinal).ThenBy(f => f.Line)
            .ToList();

        var verdict =
            findings.Any(f => f.Severity >= _options.BlockOnSeverity) ? ReviewVerdict.RequestChanges :
            findings.Count > 0 ? ReviewVerdict.Comment : ReviewVerdict.Approve;

        var summary = await _summarizer.SummarizeAsync(prTitle, toReview.Count, findings, ct);
        return new ReviewReport(findings, summary, verdict, toReview.Count, allFiles.Count - toReview.Count);
    }

    /// <summary>Per-file: confidence gate, de-duplication (same file+line+rule), comment cap.</summary>
    private IEnumerable<ReviewFinding> PostProcess(IEnumerable<ReviewFinding> findings) =>
        findings
            .Where(f => f.Confidence >= _options.MinConfidence)
            .GroupBy(f => (f.File, f.Line, f.RuleId))
            .Select(g => g.OrderByDescending(f => f.Source == "static").ThenByDescending(f => f.Confidence).First())
            .OrderByDescending(f => f.Severity).ThenByDescending(f => f.Confidence)
            .Take(_options.MaxFindingsPerFile);
}
