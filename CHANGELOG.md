# Changelog

All notable changes to Agentic.NET are documented here.

## [0.2.1-preview] - 2026-03-06

### Fixed
- Removed obsolete `UseMiddleware()` from `AgentBuilder` — use `WithMiddleware()` instead
- Fixed duplicate `using Agentic.Core` warning in `MiddlewareTests`
- Replaced all `UseMiddleware` calls in tests with `WithMiddleware`

### Changed
- All samples updated to use the MEAI API:
  - Demo samples (`BasicChat`, `SafeguardMiddleware`, `MemoryAndMiddleware`) now use `DemoChatClient : IChatClient` instead of the removed `IModelProvider`/`IAgentModel` pattern
  - Real OpenAI samples now use `WithChatClient(new OpenAI.Chat.ChatClient(...).AsIChatClient())`
  - Embedding samples now use `IEmbeddingGenerator` via `EmbeddingClient.AsIEmbeddingGenerator()`
  - Added `Microsoft.Extensions.AI.OpenAI` package reference to all samples that use OpenAI

### Documentation
- Added **Roadmap** section to `README.md`
- Updated `README.pkg.md` description to highlight use cases (CLI coding agents, web chat bots, autonomous agents)
- Fixed Table of Contents in `docs/user-manual.md` (section 13 link)

---

## [0.2.0-preview] - 2026-03-05

### Breaking Changes
- **Migrated to Microsoft.Extensions.AI (MEAI) v10.3.0** — the library no longer ships its own LLM abstraction
- Removed `WithOpenAi()`, `WithModelProvider()`, `IModelProvider`, `OpenAiChatModelProvider`, `OpenAiModels`
- Removed `IEmbeddingProvider`, `OpenAiEmbeddingProvider`, `WithEmbeddingProvider()`, `WithSemanticMemory()`
- `IAgentModel` is now `internal` (was previously public)

### Added
- `WithChatClient(IChatClient)` — accepts any MEAI-compatible chat client (OpenAI, Azure, Ollama, Anthropic, custom)
- `WithEmbeddingGenerator(IEmbeddingGenerator<string, Embedding<float>>)` — accepts any MEAI embedding generator

### Changed
- Repository restructured: library moved to `src/Agentic.NET/`, tests to `src/Agentic.Tests/`
- All documentation updated to reflect MEAI API

---

## [0.1.x] - Earlier

- Initial releases with built-in OpenAI provider, memory, middleware, tools, skills, SOUL.md, heartbeat, and streaming support
