using System.Text;
using CodeReview.Core.Models;

namespace CodeReview.Core.GitHub;

public static class ReportFormatter
{
    public static string InlineComment(ReviewFinding f)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"**{f.Severity}** · `{f.RuleId}` · _{f.Source}_");
        sb.AppendLine();
        sb.AppendLine(f.Message);
        if (!string.IsNullOrWhiteSpace(f.Suggestion))
        {
            sb.AppendLine();
            sb.AppendLine($"**Suggestion:** {f.Suggestion}");
        }
        return sb.ToString();
    }

    public static string ReviewBody(ReviewReport r) =>
        $"### Automated code review\n\n{r.Summary}\n\n" +
        $"Files reviewed: {r.FilesReviewed} · skipped: {r.FilesSkipped} · findings: {r.Findings.Count} · verdict: **{r.Verdict}**\n\n" +
        "_AI-assisted review. A human reviewer still approves the merge._";

    public static string ToMarkdown(ReviewReport r)
    {
        var sb = new StringBuilder(ReviewBody(r)).AppendLine().AppendLine();
        foreach (var f in r.Findings)
            sb.AppendLine($"#### {f.File}:{f.Line}").AppendLine(InlineComment(f));
        return sb.ToString();
    }
}
