# Cloudflare AI Failover Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Cloudflare Workers AI the primary LLM, embedding, and reranking provider in `BioTwin_AI.AspNetCoreApi`, with OpenRouter fallback for LLM requests and local ONNX fallback for embeddings and reranking.

**Architecture:** Provider-specific clients remain behind the existing application boundaries. Three independent failover services own their own in-memory cooldown state so a failure in one Cloudflare model does not disable the others. RAG performs vector and lexical coarse retrieval first, then reranks the configured candidate set without persisting reranker scores.

**Tech Stack:** .NET 10, ASP.NET Core, `Microsoft.Extensions.AI`, OpenAI-compatible HTTP APIs, Cloudflare Workers AI REST API, ONNX Runtime, Microsoft.ML.Tokenizers, xUnit, EF Core SQLite.

## Global Constraints

- Modify only `src/BioTwin_AI.AspNetCoreApi`, its tests, configuration, and related documentation; do not modify `src/BioTwin_AI`.
- The Blazor client calls only the BioTwin API; it never calls Cloudflare or OpenRouter directly.
- `CloudflareAI:ApiToken`, `OpenRouter:ApiKey`, and Account ID must not be logged or published to the client.
- LLM fallback order is Cloudflare then OpenRouter.
- Embedding fallback order is Cloudflare `@cf/baai/bge-m3` then local BGE-M3 ONNX.
- Rerank fallback order is Cloudflare `@cf/baai/bge-reranker-base` then local BGE reranker ONNX, then the original coarse ranking.
- Do not use hashing embeddings when both production embedding providers fail.
- Do not rebuild stored vectors during API startup and do not add a database migration.
- Do not commit or push changes unless the user explicitly requests it.
- Use `C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe` for build and test verification.
- Shut down every related BioTwin, All2MD, and .NET build service after verification.

---

### Task 1: Shared Provider Configuration, HTTP Clients, and Cooldown

**Files:**
- Create: `src/BioTwin_AI.AspNetCoreApi/Infrastructure/Ai/AiProviderNames.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Infrastructure/Ai/CloudflareAiOptions.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Infrastructure/Ai/LlmFailoverOptions.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Infrastructure/Ai/ProviderCooldownState.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Infrastructure/Ai/AiServiceCollectionExtensions.cs`
- Create: `tests/BioTwin_AI.AspNetCoreApi.Tests/Infrastructure/AiProviderConfigurationTests.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Program.cs`

**Interfaces:**
- Produces: `CloudflareAiOptions` with account, token, chat, extraction, embedding, and rerank model names.
- Produces: `ProviderCooldownState.IsCoolingDown`, `MarkRetryableFailure()`, and `MarkHealthy()` using injected `TimeProvider`.
- Produces: named HTTP client `cloudflare-ai` and provider client set for Cloudflare and OpenRouter chat.

- [ ] **Step 1: Write failing configuration and cooldown tests**

Test that the Cloudflare base URI is exactly `https://api.cloudflare.com/client/v4/accounts/{accountId}/ai/v1/`, the native model URI safely escapes the model path, an absent token produces an unconfigured provider rather than startup failure, and a 60-second cooldown expires under a manual `TimeProvider`.

```csharp
[Fact]
public void ProviderCooldownState_expires_using_TimeProvider()
{
    var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-07-10T00:00:00Z"));
    var state = new ProviderCooldownState(clock, TimeSpan.FromSeconds(60));
    state.MarkRetryableFailure();
    Assert.True(state.IsCoolingDown);
    clock.Advance(TimeSpan.FromSeconds(61));
    Assert.False(state.IsCoolingDown);
}
```

- [ ] **Step 2: Run the focused test and confirm it fails**

Run:

```powershell
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests/BioTwin_AI.AspNetCoreApi.Tests/BioTwin_AI.AspNetCoreApi.Tests.csproj --filter AiProviderConfigurationTests -p:UseAppHost=false
```

Expected: compilation fails because the options, registration extension, and cooldown state do not exist.

- [ ] **Step 3: Implement options, URI construction, named clients, and cooldown**

Use options validation that checks URI shape and positive timeout values without requiring secrets at application startup. Configure `cloudflare-ai` with a bearer header only when the token is present. Keep separate cooldown instances inside the LLM, embedding, and rerank services rather than a single global Cloudflare switch.

```csharp
public sealed class ProviderCooldownState(TimeProvider clock, TimeSpan duration)
{
    private long _retryAfterUtcTicks;
    public bool IsCoolingDown => clock.GetUtcNow().UtcTicks < Interlocked.Read(ref _retryAfterUtcTicks);
    public void MarkRetryableFailure() =>
        Interlocked.Exchange(ref _retryAfterUtcTicks, clock.GetUtcNow().Add(duration).UtcTicks);
    public void MarkHealthy() => Interlocked.Exchange(ref _retryAfterUtcTicks, 0);
}
```

- [ ] **Step 4: Re-run the focused test**

Expected: all `AiProviderConfigurationTests` pass without reading real credentials.

---

### Task 2: Cloudflare-first LLM with OpenRouter Failover

**Files:**
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Llm/LlmRequestKind.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Llm/LlmProviderClients.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Application/Llm/ILlmChatService.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Application/Llm/LlmChatService.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Application/Chat/ChatService.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Application/Refinement/ResumeRefinementService.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Application/Resumes/ResumeService.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Application/Resumes/ResumeWizardExtractionService.cs`
- Modify: affected fakes in `tests/BioTwin_AI.AspNetCoreApi.Tests/Application`
- Modify: `tests/BioTwin_AI.AspNetCoreApi.Tests/Application/LlmChatServiceTests.cs`
- Modify: `tests/BioTwin_AI.AspNetCoreApi.Tests/Application/ResumeWizardExtractionServiceTests.cs`

**Interfaces:**
- Produces: `LlmRequestKind.General` and `LlmRequestKind.StructuredExtraction` so provider-specific model IDs do not leak into business services.
- Produces: `ILlmChatService.CompleteAsync(..., LlmRequestKind requestKind, CancellationToken)` and equivalent streaming method.

- [ ] **Step 1: Write failing failover tests**

Cover Cloudflare success, missing Cloudflare configuration, empty output, invalid JSON for structured extraction, timeout, `408`, `429`, `5xx`, cancellation, cooldown, both providers failing, stream failure before the first chunk, and stream failure after the first chunk.

```csharp
[Fact]
public async Task CompleteAsync_falls_back_when_primary_JSON_is_invalid()
{
    var primary = new QueueChatClient("not-json");
    var fallback = new QueueChatClient("{\"title\":\"Resume\"}");
    var service = CreateService(primary, fallback);
    var text = await service.CompleteAsync(
        [new ChatMessage(ChatRole.User, "private resume")],
        new ChatOptions { ResponseFormat = ChatResponseFormat.Json },
        LlmRequestKind.StructuredExtraction,
        CancellationToken.None);
    Assert.Equal("{\"title\":\"Resume\"}", text);
    Assert.Equal(1, primary.CompleteCalls);
    Assert.Equal(1, fallback.CompleteCalls);
}
```

- [ ] **Step 2: Run LLM tests and confirm failure**

Run the API test project filtered to `LlmChatServiceTests|ResumeWizardExtractionServiceTests` and expect compilation failures from the new request-kind parameter.

- [ ] **Step 3: Implement provider-specific model selection and failover**

Clone request options before setting model IDs. Use `CloudflareAI:ChatModel` for general calls, `CloudflareAI:ExtractionModel` for structured extraction, and the corresponding OpenRouter model on fallback. Validate JSON with `JsonDocument.Parse` only for structured extraction. Never log messages or response bodies.

```csharp
private static bool IsUsable(string text, LlmRequestKind kind)
{
    if (string.IsNullOrWhiteSpace(text)) return false;
    if (kind != LlmRequestKind.StructuredExtraction) return true;
    try { using var _ = JsonDocument.Parse(text); return true; }
    catch (JsonException) { return false; }
}
```

Classify only missing provider configuration, network failure, timeout, `408`, `429`, and `5xx` as retryable. Re-throw `OperationCanceledException` when the caller token is canceled. For streaming, fallback only while no text has been yielded.

- [ ] **Step 4: Remove provider-specific model IDs from business services**

Pass `General` from chat, refinement, and merge paths, and `StructuredExtraction` from resume extraction. Keep temperature, token budget, response format, and reasoning settings in business-owned `ChatOptions`.

- [ ] **Step 5: Re-run LLM and extraction tests**

Expected: all focused tests pass, including safe-log assertions and cancellation behavior.

---

### Task 2A: Replace the Cloudflare SDK Adapter with a Native `IChatClient`

**Files:**
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Llm/CloudflareChatClient.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Infrastructure/Ai/AiServiceCollectionExtensions.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Application/Llm/LlmProviderClients.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Application/Llm/LlmChatService.cs`
- Create: `tests/BioTwin_AI.AspNetCoreApi.Tests/Application/CloudflareChatClientTests.cs`

**Interfaces:**
- Produces: a complete `CloudflareChatClient : IChatClient` for non-streaming, JSON Schema, and SSE streaming requests.
- Preserves: the existing `ILlmChatService` failover boundary and the SDK-backed OpenRouter client.

- [ ] **Step 1: Write failing protocol tests**

Cover string and object-valued `message.content`, JSON Schema request serialization, malformed responses, retryable HTTP status propagation, cancellation, SSE text chunks, `[DONE]`, and an SSE failure after output has started.

- [ ] **Step 2: Implement Cloudflare request and response mapping**

Serialize `ChatMessage`, model, temperature, token limit, and `ChatResponseFormatJson` directly. Convert object-valued content with `JsonElement.GetRawText()` and string-valued content without modification. Never include response bodies or prompts in exceptions.

- [ ] **Step 3: Implement SSE streaming**

Request `stream: true` with `HttpCompletionOption.ResponseHeadersRead`, parse only `data:` lines, stop on `[DONE]`, and emit text from `choices[].delta.content`. Propagate caller cancellation and protocol/transport failures so `LlmChatService` can apply its existing pre-first-chunk failover rule.

- [ ] **Step 4: Replace only the Cloudflare registration**

Register `CloudflareChatClient` as the Cloudflare `IChatClient`. Keep the OpenRouter SDK client unchanged and remove the temporary structured-only adapter.

- [ ] **Step 5: Run focused unit tests and a synthetic live test**

Verify normal, structured, and streaming requests through application DI using only synthetic prompts and User Secrets. Remove the live diagnostic test afterward.

---

### Task 3: Cloudflare BGE-M3 Embedding with Local ONNX Fallback

**Files:**
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Embeddings/CloudflareEmbeddingService.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Embeddings/FailoverEmbeddingService.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Embeddings/EmbeddingVectorValidator.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Embeddings/ILocalEmbeddingServiceFactory.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Embeddings/LocalEmbeddingServiceFactory.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Application/Embeddings/BgeM3OnnxEmbeddingService.cs`
- Create: `tests/BioTwin_AI.AspNetCoreApi.Tests/Application/CloudflareEmbeddingServiceTests.cs`
- Create: `tests/BioTwin_AI.AspNetCoreApi.Tests/Application/FailoverEmbeddingServiceTests.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Program.cs`

**Interfaces:**
- Consumes: named `cloudflare-ai` client and independent `ProviderCooldownState`.
- Produces: the existing `IEmbeddingService.EmbedAsync` contract with normalized, finite, 1024-dimensional vectors.

- [ ] **Step 1: Write failing Cloudflare response and failover tests**

Use a fake `HttpMessageHandler` to verify POST `/embeddings` with model `@cf/baai/bge-m3` and input text. Cover valid OpenAI-compatible response, empty data, wrong dimensions, `NaN`/infinite values, zero vectors, timeout, caller cancellation, local success, and both providers failing.

```json
{
  "object": "list",
  "data": [{ "object": "embedding", "index": 0, "embedding": [0.6, 0.8] }],
  "model": "@cf/baai/bge-m3"
}
```

- [ ] **Step 2: Run embedding tests and confirm failure**

Expected: compilation fails because Cloudflare and failover embedding services do not exist.

- [ ] **Step 3: Implement Cloudflare request parsing and vector validation**

Send only one text per current `IEmbeddingService` call. Require exactly one returned vector, the configured dimension, finite values, and non-zero norm. Normalize with the same L2 behavior used by `BgeM3OnnxEmbeddingService`.

- [ ] **Step 4: Implement lazy local fallback and remove hashing from production DI**

The local factory first calls `BgeM3OnnxEmbeddingService.CanLoad`; it creates and caches the ONNX service only when fallback is needed. If local files are missing, return a clear provider-unavailable error. Register `FailoverEmbeddingService` as the sole production `IEmbeddingService`; keep `HashingEmbeddingService` only as an explicitly constructed test utility.

- [ ] **Step 5: Re-run embedding tests**

Expected: Cloudflare success never creates the local model; retryable Cloudflare failures use local ONNX; cancellation and validation errors follow the documented rules.

---

### Task 4: Cloudflare and Local Reranker Services

**Files:**
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Reranking/IRerankService.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Reranking/CloudflareRerankService.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Reranking/LocalBgeRerankService.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Reranking/ILocalRerankServiceFactory.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Reranking/LocalRerankServiceFactory.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Reranking/FailoverRerankService.cs`
- Create: `tests/BioTwin_AI.AspNetCoreApi.Tests/Application/CloudflareRerankServiceTests.cs`
- Create: `tests/BioTwin_AI.AspNetCoreApi.Tests/Application/FailoverRerankServiceTests.cs`
- Create: `tests/BioTwin_AI.AspNetCoreApi.Tests/Application/LocalBgeRerankServiceTests.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/BioTwin_AI.AspNetCoreApi.csproj`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Program.cs`

**Interfaces:**
- Produces: `IRerankService.RerankAsync(string query, IReadOnlyList<string> documents, int limit, CancellationToken)` returning `RerankResult(Index, Score)`.

- [ ] **Step 1: Add `Microsoft.ML.Tokenizers` 2.0.0 and write failing tests**

Test Cloudflare POST to `/ai/run/@cf/baai/bge-reranker-base` with `query`, indexed `contexts`, and `top_k`. Parse `result.response` entries with `id` and `score`, rejecting duplicate or out-of-range IDs and non-finite scores.

```json
{
  "success": true,
  "result": {
    "response": [
      { "id": 1, "score": 0.91 },
      { "id": 0, "score": 0.32 }
    ]
  }
}
```

- [ ] **Step 2: Run reranker tests and confirm failure**

Expected: compilation fails because the reranking namespace and services do not exist.

- [ ] **Step 3: Implement the new API's local ONNX reranker**

Port the tokenizer/model algorithm into the new namespace without editing or referencing `src/BioTwin_AI`. Resolve `LLM/bge_rerank_v2/model.onnx` and `tokenizer.json` using the same solution-root and publish fallback rules as the embedding service. Preserve cancellation between documents and sigmoid scoring.

- [ ] **Step 4: Implement Cloudflare parsing and two-level fallback**

Cloudflare retryable failures invoke the lazy local factory. If local inference also fails, log provider/model/status metadata and return an empty ranking list. Caller cancellation must still propagate.

- [ ] **Step 5: Run reranker tests**

Expected: Cloudflare success, local fallback, cooldown, invalid response rejection, and final empty-result degradation tests all pass.

---

### Task 5: Integrate Reranking into RAG Retrieval

**Files:**
- Modify: `src/BioTwin_AI.AspNetCoreApi/Application/Rag/RagSearchService.cs`
- Create: `tests/BioTwin_AI.AspNetCoreApi.Tests/Application/RagSearchServiceTests.cs`

**Interfaces:**
- Consumes: `IRerankService` and `Rerank:CandidateCount`.
- Produces: unchanged public `IRagSearchService` and `RagSearchResponse` contracts.

- [ ] **Step 1: Write failing SQLite-backed RAG tests**

Seed more candidates than the requested result limit. Assert that the service sends the top coarse candidates to the reranker, maps reranker indices back to citations, uses reranker order and scores, and preserves coarse order when the reranker returns an empty list.

```csharp
Assert.Equal(new[] { expectedSecondSectionId, expectedFirstSectionId },
    response.Citations.Select(item => item.SectionId));
Assert.Equal(3, reranker.Documents.Count);
```

- [ ] **Step 2: Run `RagSearchServiceTests` and confirm failure**

Expected: constructor or result-order assertions fail because reranking is not integrated.

- [ ] **Step 3: Implement coarse candidate selection and reranking**

Clamp the requested limit to 1-20. Clamp `CandidateCount` to at least the requested limit and no more than 100. Materialize candidate DTOs plus full rerank text, call reranking once, map valid indices, and use the original list if no valid rerank results are returned.

- [ ] **Step 4: Run RAG tests**

Expected: reranked and degraded result paths both pass without changing public DTOs or database schema.

---

### Task 6: Configuration, Regression Tests, and Operational Verification

**Files:**
- Modify: `src/BioTwin_AI.AspNetCoreApi/appsettings.json`
- Modify: `src/BioTwin_AI.AspNetCoreApi/appsettings.Development.json` only for non-secret development overrides
- Modify: `src/BioTwin_AI.AspNetCoreApi/Program.cs`
- Modify: architecture/configuration tests under `tests/BioTwin_AI.AspNetCoreApi.Tests`
- Verify: `docs/superpowers/specs/2026-07-10-cloudflare-workers-ai-failover-design.zh-CN.md`
- Verify: `docs/superpowers/specs/2026-07-10-cloudflare-workers-ai-failover-design.en.md`

**Interfaces:**
- Produces: Cloudflare-first defaults with secrets left empty and externally injectable.
- Preserves: existing authenticated vector-rebuild endpoint; no startup rebuild and no migration.

- [ ] **Step 1: Add safe default configuration and configuration assertions**

Set the documented model names, provider order, independent timeouts, cooldowns, expected 1024 dimensions, and RAG candidate count. Keep API tokens and Account ID empty in committed configuration.

- [ ] **Step 2: Run the complete API test suite**

```powershell
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests/BioTwin_AI.AspNetCoreApi.Tests/BioTwin_AI.AspNetCoreApi.Tests.csproj -p:UseAppHost=false
```

Expected: all tests pass with no warnings.

- [ ] **Step 3: Build API, client, and solution without starting services**

```powershell
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' build src/BioTwin_AI.AspNetCoreApi/BioTwin_AI.AspNetCoreApi.csproj -p:UseAppHost=false
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' build src/BioTwin_AI.BlazorClient/BioTwin_AI.BlazorClient.csproj -p:UseAppHost=false
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' build BioTwin_AI.slnx -p:UseAppHost=false
```

Expected: all builds succeed with zero errors and zero new warnings.

- [ ] **Step 4: Run credential-free and synthetic provider verification**

Without credentials, confirm API startup does not fail solely because Cloudflare is unconfigured and that local embedding/rerank fallback can load. When development secrets are available, send only synthetic English and Simplified Chinese samples to verify Cloudflare/local vector dimensions and similarity ordering; never send a real resume in diagnostics.

- [ ] **Step 5: Confirm migration behavior and scope**

Verify there are no database migration changes, no startup call to rebuild vectors, and no diff under `src/BioTwin_AI`. Record that the administrator must invoke the existing rebuild endpoint after deployment.

- [ ] **Step 6: Shut down related services and inspect final diff**

Stop BioTwin API/client, All2MD, and build-server processes used during verification. Run `git status --short` and `git diff --check`; do not commit.
