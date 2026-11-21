using System.Text.RegularExpressions;
using CodeReview.Core.Models;

namespace CodeReview.Core.Diff;

/// <summary>
/// Parses a git unified diff (what GitHub returns for application/vnd.github.diff) into structured
/// files/hunks/lines with correct OLD and NEW line numbers. Line numbers are the foundation of
/// inline comments, so they are computed here, never by the LLM.
/// </summary>
public static class UnifiedDiffParser
{
    private static readonly Regex GitHeader =
        new(@"^diff --git a/(?<a>.+?) b/(?<b>.+)$", RegexOptions.Compiled);

    private static readonly Regex HunkHeader =
        new(@"^@@ -(?<os>\d+)(?:,\d+)? \+(?<ns>\d+)(?:,\d+)? @@ ?(?<ctx>.*)$", RegexOptions.Compiled);

    public static IReadOnlyList<FileDiff> Parse(string diff)
    {
        var files = new List<FileDiff>();
        string? path = null;
        bool deleted = false, binary = false;
        var hunks = new List<DiffHunk>();
        List<DiffLine>? lines = null;
        int oldNo = 0, newNo = 0, oldStart = 0, newStart = 0;
        string header = "";

        void FlushHunk()
        {
            if (lines is not null) hunks.Add(new DiffHunk(oldStart, newStart, header, lines));
            lines = null;
        }

        void FlushFile()
        {
            FlushHunk();
            if (path is not null) files.Add(new FileDiff(path, deleted, binary, hunks));
            hunks = new List<DiffHunk>();
            path = null; deleted = false; binary = false;
        }

        foreach (var raw in diff.Split('\n'))
        {
            var line = raw.TrimEnd('\r');

            // A hunk body line always starts with ' ', '+', '-' or '\', so this can only be a new file.
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                FlushFile();
                var g = GitHeader.Match(line);
                path = g.Success ? g.Groups["b"].Value : "unknown";
                continue;
            }

            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                var m = HunkHeader.Match(line);
                if (!m.Success) continue;
                FlushHunk();
                oldStart = int.Parse(m.Groups["os"].Value);
                newStart = int.Parse(m.Groups["ns"].Value);
                oldNo = oldStart;
                newNo = newStart;
                header = line;
                lines = new List<DiffLine>();
                continue;
            }

            if (lines is null)
            {
                // File header region: metadata between "diff --git" and the first hunk.
                if (line.StartsWith("deleted file mode", StringComparison.Ordinal)) deleted = true;
                else if (line.StartsWith("Binary files", StringComparison.Ordinal) ||
                         line.StartsWith("GIT binary patch", StringComparison.Ordinal)) binary = true;
                else if (line.StartsWith("+++ ", StringComparison.Ordinal))
                {
                    var p = line[4..].Trim();
                    if (p == "/dev/null") deleted = true;
                    else path = p.StartsWith("b/", StringComparison.Ordinal) ? p[2..] : p;
                }
                continue;
            }

            if (line.Length == 0) continue; // trailing newline / stripped blank context line

            switch (line[0])
            {
                case '+': lines.Add(new DiffLine(DiffLineKind.Added, null, newNo++, line[1..])); break;
                case '-': lines.Add(new DiffLine(DiffLineKind.Removed, oldNo++, null, line[1..])); break;
                case ' ': lines.Add(new DiffLine(DiffLineKind.Context, oldNo++, newNo++, line[1..])); break;
                // '\' = "\ No newline at end of file": ignored, carries no line number.
            }
        }

        FlushFile();
        return files;
    }
}
