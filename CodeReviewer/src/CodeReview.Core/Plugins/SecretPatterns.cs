using System.Text.RegularExpressions;

namespace CodeReview.Core.Plugins;

/// <summary>Shared secret signatures: used by the static analyzer (to report) and the redaction filter (to hide from the LLM).</summary>
public static class SecretPatterns
{
    public static readonly IReadOnlyList<(string Name, Regex Pattern)> All = new (string, Regex)[]
    {
        ("AWS access key id", new Regex(@"\bAKIA[0-9A-Z]{16}\b", RegexOptions.Compiled)),
        ("private key block", new Regex(@"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----", RegexOptions.Compiled)),
        ("GitHub token", new Regex(@"\bgh[pos]_[A-Za-z0-9]{30,}\b", RegexOptions.Compiled)),
        ("hard-coded credential", new Regex(
            @"(?i)\b(?:password|passwd|pwd|secret|api[_-]?key|token)\b\s*[:=]\s*[""'][^""']{6,}[""']",
            RegexOptions.Compiled))
    };

    public static string Redact(string text)
    {
        foreach (var (_, pattern) in All) text = pattern.Replace(text, "[REDACTED]");
        return text;
    }
}
