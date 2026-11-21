using CodeReview.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace CodeReview.Core.Review;

/// <summary>Second, tiny LLM call: turns the validated findings into a short human summary. Verdict is NOT decided here.</summary>
public sealed class PrSummarizer
{
    private const string Template = """
<message role="system">
You write concise pull-request review summaries for engineers. Plain text, at most 5 sentences, no headings.
Mention the most important risks first. Do not invent issues that are not in the list.
</message>
<message role="user">
PR title: {{$title}}
Files reviewed: {{$fileCount}}
Findings:
{{$findings}}
</message>
""";

    private readonly Kernel _kernel;
    private readonly KernelFunction _summarize;
    private readonly ILogger<PrSummarizer> _log;

    public PrSummarizer(Kernel kernel, PromptExecutionSettings settings, ILogger<PrSummarizer> log)
    {
        _kernel = kernel;
        _log = log;
        _summarize = kernel.CreateFunctionFromPrompt(Template, settings, functionName: "SummarizePr");
    }

    public async Task<string> SummarizeAsync(string? title, int fileCount, IReadOnlyList<ReviewFinding> findings, CancellationToken ct)
    {
        if (findings.Count == 0) return "No issues found in the reviewed changes.";

        var list = string.Join("\n", findings.Take(40).Select(f =>
            $"- {f.Severity} {f.File}:{f.Line} [{f.RuleId}] {f.Message}"));

        try
        {
            var result = await _summarize.InvokeAsync(_kernel, new KernelArguments
            {
                ["title"] = title ?? "(untitled)",
                ["fileCount"] = fileCount,
                ["findings"] = list
            }, ct);
            return result.GetValue<string>()?.Trim() ?? Fallback(findings);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The summary is a nice-to-have: never fail the whole review because of it.
            _log.LogWarning(ex, "Summary generation failed; using deterministic fallback");
            return Fallback(findings);
        }
    }

    private static string Fallback(IReadOnlyList<ReviewFinding> findings) =>
        $"{findings.Count} finding(s): " + string.Join(", ",
            findings.GroupBy(f => f.Severity).OrderByDescending(g => g.Key).Select(g => $"{g.Count()} {g.Key}"));
}
