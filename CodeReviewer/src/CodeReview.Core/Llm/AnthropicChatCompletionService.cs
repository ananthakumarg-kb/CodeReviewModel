using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace CodeReview.Core.Llm;

/// <summary>
/// A custom Semantic Kernel connector for Claude (Anthropic Messages API).
/// SK only needs IChatCompletionService, so ANY model can be plugged in by implementing it.
/// Everything above this class (prompt functions, filters, plugins) is provider-agnostic.
/// Text-only: no streaming and no tool calling, which this reviewer does not need.
/// </summary>
public sealed class AnthropicChatCompletionService : IChatCompletionService
{
    private readonly HttpClient _http;
    private readonly string _model;

    public AnthropicChatCompletionService(HttpClient http, string apiKey, string modelId)
    {
        _http = http;
        _model = modelId;
        _http.BaseAddress ??= new Uri("https://api.anthropic.com/");
        _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        Attributes = new Dictionary<string, object?> { ["ModelId"] = modelId };
    }

    public IReadOnlyDictionary<string, object?> Attributes { get; }

    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        // Anthropic takes the system prompt as a top-level field, not as a message.
        var system = string.Join("\n\n", chatHistory
            .Where(m => m.Role == AuthorRole.System)
            .Select(m => m.Content));

        var messages = chatHistory
            .Where(m => m.Role != AuthorRole.System)
            .Select(m => new Dictionary<string, object?>
            {
                ["role"] = m.Role == AuthorRole.Assistant ? "assistant" : "user",
                ["content"] = m.Content ?? ""
            })
            .ToList();

        var payload = new Dictionary<string, object?>
        {
            ["model"] = _model,
            ["max_tokens"] = ReadInt(executionSettings, "max_tokens", 4096),
            ["temperature"] = ReadDouble(executionSettings, "temperature", 0.1),
            ["messages"] = messages
        };
        if (system.Length > 0) payload["system"] = system;

        using var response = await _http.PostAsJsonAsync("v1/messages", payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Anthropic API error {(int)response.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var text = string.Concat(root.GetProperty("content").EnumerateArray()
            .Where(b => b.GetProperty("type").GetString() == "text")
            .Select(b => b.GetProperty("text").GetString()));

        var metadata = new Dictionary<string, object?>();
        if (root.TryGetProperty("usage", out var usage))
        {
            var input = usage.TryGetProperty("input_tokens", out var i) ? i.GetInt32() : 0;
            var output = usage.TryGetProperty("output_tokens", out var o) ? o.GetInt32() : 0;
            metadata["Usage"] = $"in={input} out={output}";
        }

        return new List<ChatMessageContent>
        {
            new(AuthorRole.Assistant, text, modelId: _model, metadata: metadata)
        };
    }

    public IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Streaming is not needed for batch code review.");

    private static int ReadInt(PromptExecutionSettings? s, string key, int fallback) =>
        s?.ExtensionData is not null && s.ExtensionData.TryGetValue(key, out var v) ? Convert.ToInt32(v) : fallback;

    private static double ReadDouble(PromptExecutionSettings? s, string key, double fallback) =>
        s?.ExtensionData is not null && s.ExtensionData.TryGetValue(key, out var v) ? Convert.ToDouble(v) : fallback;
}
