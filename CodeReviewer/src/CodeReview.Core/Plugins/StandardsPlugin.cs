using System.ComponentModel;
using CodeReview.Core.Standards;
using Microsoft.SemanticKernel;

namespace CodeReview.Core.Plugins;

/// <summary>
/// A Semantic Kernel NATIVE plugin. Its [KernelFunction] methods become KernelFunctions that can be called
/// (a) from code via kernel.InvokeAsync, (b) inline from a prompt template as
/// {{Standards.get_standards language=$language}}, or (c) by the LLM through function calling.
/// Here we use (b): deterministic, cheap, and works with every model/provider.
/// </summary>
public sealed class StandardsPlugin
{
    private readonly StandardsProvider _provider;

    public StandardsPlugin(StandardsProvider provider) => _provider = provider;

    [KernelFunction("get_standards")]
    [Description("Returns the team's coding standards that apply to the given language id (e.g. csharp, typescript).")]
    public string GetStandards([Description("Language id such as csharp, python, typescript")] string language)
        => _provider.Format(language);
}
