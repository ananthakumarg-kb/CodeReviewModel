using CodeReview.Core.GitHub;
using CodeReview.Core.Llm;
using CodeReview.Core.Models;
using CodeReview.Core.Plugins;
using CodeReview.Core.Review;
using CodeReview.Core.Standards;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

// Usage:
//   dotnet run --project src/CodeReview.Cli -- --diff changes.patch
//   dotnet run --project src/CodeReview.Cli -- --repo owner/name --pr 123 [--post true] [--fail-on-high true]
// Secrets via environment: ANTHROPIC_API_KEY or OPENAI_API_KEY, and GITHUB_TOKEN for GitHub access.

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

using var loggerFactory = LoggerFactory.Create(b => b
    .AddSimpleConsole(o => o.SingleLine = true)
    .SetMinimumLevel(LogLevel.Information));

var llm = config.GetSection("Llm").Get<LlmOptions>() ?? new LlmOptions();
llm.ApiKey = (llm.Provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)
    ? config["OPENAI_API_KEY"] : config["ANTHROPIC_API_KEY"]) ?? "";
if (llm.ApiKey.Length == 0)
{
    Console.Error.WriteLine($"Missing API key for provider {llm.Provider} (set ANTHROPIC_API_KEY or OPENAI_API_KEY).");
    return 2;
}

var reviewOptions = config.GetSection("Review").Get<ReviewOptions>() ?? new ReviewOptions();
var standardsPath = config["StandardsPath"] is { Length: > 0 } p ? p : Path.Combine(AppContext.BaseDirectory, "standards");
var standards = new StandardsProvider(standardsPath);
loggerFactory.CreateLogger("Startup").LogInformation("Loaded {Count} coding rules from {Path}", standards.Count, standardsPath);

// ---- Input: local patch file or a GitHub pull request ----
string diffText;
string? title = null;
PullRequestRef? pr = null;
GitHubPullRequestClient? github = null;
string headSha = "";
using var ghHttp = new HttpClient();

if (config["diff"] is { Length: > 0 } diffPath)
{
    diffText = await File.ReadAllTextAsync(diffPath);
}
else if (config["repo"] is { Length: > 0 } repo && int.TryParse(config["pr"], out var number) && repo.Contains('/'))
{
    var parts = repo.Split('/', 2);
    pr = new PullRequestRef(parts[0], parts[1], number);
    github = new GitHubPullRequestClient(ghHttp, config["GITHUB_TOKEN"] ?? "");
    diffText = await github.GetDiffAsync(pr, CancellationToken.None);
    (title, headSha) = await github.GetMetaAsync(pr, CancellationToken.None);
}
else
{
    Console.Error.WriteLine("Provide --diff <file> or --repo owner/name --pr <number>.");
    return 1;
}

// ---- Wire the pipeline ----
var kernel = ReviewKernelFactory.Create(llm, standards, loggerFactory);
var reviewer = new LlmFileReviewer(kernel, ReviewKernelFactory.Settings(llm, expectJson: true), loggerFactory.CreateLogger<LlmFileReviewer>());
var summarizer = new PrSummarizer(kernel, ReviewKernelFactory.Settings(llm, expectJson: false), loggerFactory.CreateLogger<PrSummarizer>());
var orchestrator = new ReviewOrchestrator(reviewer, summarizer, new StaticAnalyzer(), reviewOptions,
    loggerFactory.CreateLogger<ReviewOrchestrator>());

var report = await orchestrator.ReviewAsync(diffText, title);
Console.WriteLine(ReportFormatter.ToMarkdown(report));

if (config["post"] == "true" && github is not null && pr is not null)
{
    await github.PostReviewAsync(pr, headSha, report, allowRequestChanges: config["request-changes"] == "true", CancellationToken.None);
    Console.WriteLine("Review posted to GitHub.");
}

// CI gate: fail the job only when explicitly asked and a blocking finding exists.
return config["fail-on-high"] == "true" && report.Verdict == ReviewVerdict.RequestChanges ? 3 : 0;
