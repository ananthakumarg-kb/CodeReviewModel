using CodeReview.Core.Plugins;
using Microsoft.SemanticKernel;

namespace CodeReview.Core.Filters;

/// <summary>
/// IPromptRenderFilter: runs after SK resolves a prompt function and BEFORE the template is rendered.
/// We redact the raw input variables here (not the rendered text) because the renderer HTML-encodes values,
/// which would stop the secret regexes from matching quotes (&amp;quot;).
/// </summary>
public sealed class SecretRedactionFilter : IPromptRenderFilter
{
    public async Task OnPromptRenderAsync(PromptRenderContext context, Func<PromptRenderContext, Task> next)
    {
        foreach (var pair in context.Arguments.ToList())
        {
            if (pair.Value is string text) context.Arguments[pair.Key] = SecretPatterns.Redact(text);
        }

        await next(context);
    }
}
