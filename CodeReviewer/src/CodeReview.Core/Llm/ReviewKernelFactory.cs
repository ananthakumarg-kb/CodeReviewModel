using CodeReview.Core.Filters;
using CodeReview.Core.Plugins;
using CodeReview.Core.Standards;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace CodeReview.Core.Llm;

public sealed class LlmOptions
{
    /// <summary>"Anthropic" (Claude) or "OpenAI" (GPT / Codex-family chat models).</summary>
    public string Provider { get; set; } = "Anthropic";
    public string Model { get; set; } = "claude-sonnet-5-5";
    public string ApiKey { get; set; } = "";
    public int MaxOutputTokens { get; set; } = 4096;
    public double Temperature { get; set; } = 0.1;
}

/// <summary>Composition root for Semantic Kernel: one place where model, plugins and filters are wired.</summary>
public static class ReviewKernelFactory
{
    public static Kernel Create(LlmOptions llm, StandardsProvider standards, ILoggerFactory loggerFactory)
    {
        var builder = Kernel.CreateBuilder();
        builder.Services.AddSingleton<ILoggerFactory>(loggerFactory);

        switch (llm.Provider.ToLowerInvariant())
        {
            case "openai":
                builder.AddOpenAIChatCompletion(llm.Model, llm.ApiKey);
                break;
            case "anthropic":
                builder.Services.AddSingleton<IChatCompletionService>(
                    new AnthropicChatCompletionService(
                        new HttpClient { Timeout = TimeSpan.FromMinutes(2) }, llm.ApiKey, llm.Model));
                break;
            default:
                throw new NotSupportedException($"Unknown LLM provider '{llm.Provider}'. Use Anthropic or OpenAI.");
        }

        // Filters registered in DI are picked up automatically by the Kernel.
        builder.Services.AddSingleton<IPromptRenderFilter>(new SecretRedactionFilter());
        builder.Services.AddSingleton<IFunctionInvocationFilter>(
            new TelemetryFilter(loggerFactory.CreateLogger<TelemetryFilter>()));

        var kernel = builder.Build();
        kernel.Plugins.AddFromObject(new StandardsPlugin(standards), "Standards");
        return kernel;
    }

    /// <summary>Execution settings are provider specific; this hides the difference from the rest of the code.</summary>
    public static PromptExecutionSettings Settings(LlmOptions llm, bool expectJson)
    {
        if (llm.Provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            return new OpenAIPromptExecutionSettings
            {
                Temperature = llm.Temperature,
                MaxTokens = llm.MaxOutputTokens,
                ResponseFormat = expectJson ? "json_object" : null
            };
        }

        return new PromptExecutionSettings
        {
            ExtensionData = new Dictionary<string, object>
            {
                ["temperature"] = llm.Temperature,
                ["max_tokens"] = llm.MaxOutputTokens
            }
        };
    }
}
