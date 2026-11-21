using System.Text;
using CodeReview.Core.Models;

namespace CodeReview.Core.Diff;

/// <summary>
/// Renders a file diff as numbered text for the prompt and splits it into chunks that fit the token budget.
/// Format:  "   42 + added code"  /  "   41   context"  /  "      - removed".
/// The number in the left column is the NEW-file line number the model must cite.
/// </summary>
public static class DiffChunker
{
    public static string Render(DiffLine l) => l.Kind switch
    {
        DiffLineKind.Added => $"{l.NewLine,5} + {l.Text}",
        DiffLineKind.Context => $"{l.NewLine,5}   {l.Text}",
        _ => $"      - {l.Text}"
    };

    public static IReadOnlyList<string> Chunk(FileDiff file, int maxChars)
    {
        var chunks = new List<string>();
        var sb = new StringBuilder();

        void Add(string text)
        {
            if (sb.Length > 0 && sb.Length + text.Length + 1 > maxChars)
            {
                chunks.Add(sb.ToString());
                sb.Clear();
            }
            sb.AppendLine(text);
        }

        foreach (var hunk in file.Hunks)
        {
            Add(hunk.Header);
            foreach (var line in hunk.Lines) Add(Render(line));
        }

        if (sb.Length > 0) chunks.Add(sb.ToString());
        return chunks;
    }
}
