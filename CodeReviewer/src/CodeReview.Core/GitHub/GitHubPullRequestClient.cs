using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CodeReview.Core.Models;

namespace CodeReview.Core.GitHub;

/// <summary>Thin GitHub REST client: fetch the PR diff and post ONE review containing all inline comments.</summary>
public sealed class GitHubPullRequestClient
{
    private readonly HttpClient _http;

    public GitHubPullRequestClient(HttpClient http, string token)
    {
        _http = http;
        _http.BaseAddress ??= new Uri("https://api.github.com/");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("codereview-bot");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public async Task<string> GetDiffAsync(PullRequestRef pr, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"repos/{pr.Owner}/{pr.Repo}/pulls/{pr.Number}");
        req.Headers.Accept.ParseAdd("application/vnd.github.diff");
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsStringAsync(ct);
    }

    public async Task<(string Title, string HeadSha)> GetMetaAsync(PullRequestRef pr, CancellationToken ct)
    {
        var json = await _http.GetFromJsonAsync<JsonElement>($"repos/{pr.Owner}/{pr.Repo}/pulls/{pr.Number}", ct);
        return (json.GetProperty("title").GetString() ?? "",
                json.GetProperty("head").GetProperty("sha").GetString() ?? "");
    }

    /// <param name="allowRequestChanges">If false the bot only ever COMMENTs. It never approves: a human owns the merge decision.</param>
    public async Task PostReviewAsync(PullRequestRef pr, string headSha, ReviewReport report,
        bool allowRequestChanges, CancellationToken ct)
    {
        var url = $"repos/{pr.Owner}/{pr.Repo}/pulls/{pr.Number}/reviews";
        var ev = report.Verdict == ReviewVerdict.RequestChanges && allowRequestChanges ? "REQUEST_CHANGES" : "COMMENT";

        var comments = report.Findings.Select(f => new
        {
            path = f.File, line = f.Line, side = "RIGHT", body = ReportFormatter.InlineComment(f)
        }).ToList();

        using var res = await _http.PostAsJsonAsync(url,
            new { commit_id = headSha, body = ReportFormatter.ReviewBody(report), @event = ev, comments }, ct);

        if (res.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // GitHub rejects the whole review if a single comment position is invalid (e.g. the PR was force-pushed).
            // Fallback: post everything as one plain review body so the feedback is never lost.
            using var fallback = await _http.PostAsJsonAsync(url,
                new { commit_id = headSha, body = ReportFormatter.ToMarkdown(report), @event = "COMMENT" }, ct);
            fallback.EnsureSuccessStatusCode();
            return;
        }

        res.EnsureSuccessStatusCode();
    }
}
