# CodeReviewer: LLM pull-request reviewer (C# + Semantic Kernel)

Automates first-pass PR review and enforces coding standards. Works with Claude (Anthropic) or OpenAI models.

## Quick start
```bash
dotnet new sln -n CodeReviewer
dotnet sln add src/CodeReview.Core src/CodeReview.Cli tests/CodeReview.Tests
dotnet test

export ANTHROPIC_API_KEY=...          # or OPENAI_API_KEY with Llm:Provider=OpenAI
# Review a local patch (no GitHub needed):
git diff main...HEAD > changes.patch
dotnet run --project src/CodeReview.Cli -- --diff changes.patch

# Review and comment on a real PR:
export GITHUB_TOKEN=...
dotnet run --project src/CodeReview.Cli -- --repo owner/name --pr 123 --post true
```
Switch model/provider in `src/CodeReview.Cli/appsettings.json` (or `--Llm:Provider OpenAI --Llm:Model <id>`).
Edit `standards/*.json` to change the rules the model enforces; no code change needed.

## Layout
- `Diff/` unified-diff parser and chunker  - `Standards/` rules as data
- `Plugins/` SK native plugin + static analyzer  - `Filters/` SK prompt-render and invocation filters
- `Llm/` Claude connector + kernel factory  - `Review/` prompt functions, validation, orchestrator
- `GitHub/` PR client and comment formatting

See the design document (Word) for internals and interview Q&A.

## Notes
- This code was written without a compiler in the authoring environment. Run `dotnet build` and `dotnet test` first and fix any version drift (SK package version, Microsoft.Extensions.* alignment).
- OpenAI "codex" models are served on the Responses API, not Chat Completions; use a chat-capable model id for the OpenAI provider, or add an SK Responses connector.
