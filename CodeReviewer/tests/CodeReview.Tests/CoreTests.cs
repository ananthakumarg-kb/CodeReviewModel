using CodeReview.Core.Diff;
using CodeReview.Core.Models;
using CodeReview.Core.Plugins;
using CodeReview.Core.Review;
using Xunit;

namespace CodeReview.Tests;

public class CoreTests
{
    private const string SampleDiff = """
diff --git a/src/Foo.cs b/src/Foo.cs
index 111..222 100644
--- a/src/Foo.cs
+++ b/src/Foo.cs
@@ -10,3 +10,4 @@ class Foo
 context
-old
+new1
+var password = "supersecret123";
 tail
""";

    [Fact]
    public void Parser_computes_new_line_numbers_for_added_lines()
    {
        var file = Assert.Single(UnifiedDiffParser.Parse(SampleDiff));
        Assert.Equal("src/Foo.cs", file.Path);
        Assert.Equal("csharp", file.Language);
        Assert.Equal(new[] { 11, 12 }, file.AddedLines);
    }

    [Fact]
    public void Chunker_splits_when_over_budget()
    {
        var file = UnifiedDiffParser.Parse(SampleDiff)[0];
        Assert.True(DiffChunker.Chunk(file, maxChars: 40).Count > 1);
        Assert.Single(DiffChunker.Chunk(file, maxChars: 10_000));
    }

    [Fact]
    public void StaticAnalyzer_flags_hardcoded_secret_on_correct_line()
    {
        var file = UnifiedDiffParser.Parse(SampleDiff)[0];
        var finding = Assert.Single(new StaticAnalyzer().Analyze(file), f => f.RuleId == "SEC-001");
        Assert.Equal(12, finding.Line);
        Assert.Equal(Severity.Critical, finding.Severity);
    }

    [Fact]
    public void ResponseParser_accepts_json_inside_code_fence()
    {
        var text = "Here you go:\n```json\n{\"findings\":[{\"line\":5,\"severity\":\"High\",\"message\":\"x\",\"confidence\":\"0.9\"}]}\n```";
        Assert.True(LlmResponseParser.TryParse(text, out var findings));
        Assert.Equal(5, Assert.Single(findings).Line);
    }

    [Fact]
    public void ResponseParser_rejects_garbage()
    {
        Assert.False(LlmResponseParser.TryParse("sorry, I cannot help", out _));
    }

    [Fact]
    public void Redactor_hides_secrets_before_they_reach_the_model()
    {
        Assert.DoesNotContain("supersecret123", SecretPatterns.Redact("var password = \"supersecret123\";"));
    }
}
