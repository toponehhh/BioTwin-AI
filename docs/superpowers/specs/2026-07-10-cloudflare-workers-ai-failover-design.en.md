# Cloudflare Workers AI Primary Service with Multi-level Failover Design

Date: 2026-07-10
Status: Approved

## 1. Goal and Scope

`BioTwin_AI.AspNetCoreApi` will use Cloudflare Workers AI as the default service for large language models, embeddings, and reranking, while retaining local models or OpenRouter as automatic fallback providers. The Blazor client will continue to call only the BioTwin API and will never receive third-party endpoints, model details, or provider credentials.

This change applies only to `src/BioTwin_AI.AspNetCoreApi`. The legacy backend at `src/BioTwin_AI` will not be modified or used as an implementation dependency on this branch.

The business scope includes:

- chat, resume refinement, resume merging, and structured resume extraction;
- resume-section and query embedding generation;
- semantic reranking of coarse RAG retrieval results.

## 2. Providers and Models

Cloudflare large language models and embeddings use the OpenAI-compatible endpoint:

`https://api.cloudflare.com/client/v4/accounts/{accountId}/ai/v1`

Reranking uses the native Workers AI REST endpoint:

`https://api.cloudflare.com/client/v4/accounts/{accountId}/ai/run/{model}`

The default models and fallback order are:

| Capability | Cloudflare primary model | Fallback |
| --- | --- | --- |
| General chat, refinement, and merging | `@cf/meta/llama-3.1-8b-instruct-fast` | OpenRouter |
| Structured resume extraction | `@cf/meta/llama-3.3-70b-instruct-fp8-fast` | OpenRouter |
| Embedding | `@cf/baai/bge-m3` | Local BGE-M3 ONNX |
| Reranking | `@cf/baai/bge-reranker-base` | Local BGE reranker ONNX |

OpenRouter retains its current endpoint, credentials, and model configuration but is no longer the default path. Administrators may disable LLM failover or explicitly change the preferred provider. Embedding and reranking never use OpenRouter when their Cloudflare primary path fails.

## 3. Configuration Shape

Non-secret configuration is stored in `appsettings.json`:

```json
{
  "LLM": {
    "PrimaryProvider": "CloudflareWorkersAI",
    "FallbackProvider": "OpenRouter",
    "FallbackEnabled": true,
    "FallbackCooldownSeconds": 60,
    "RequestTimeoutSeconds": 180,
    "ExtractionTimeoutSeconds": 600
  },
  "CloudflareAI": {
    "AccountId": "",
    "ApiToken": "",
    "ChatModel": "@cf/meta/llama-3.1-8b-instruct-fast",
    "ExtractionModel": "@cf/meta/llama-3.3-70b-instruct-fp8-fast",
    "EmbeddingModel": "@cf/baai/bge-m3",
    "RerankModel": "@cf/baai/bge-reranker-base"
  },
  "OpenRouter": {
    "BaseUrl": "https://openrouter.ai/api/v1",
    "ApiKey": "",
    "ChatModel": "openrouter/free",
    "ExtractionModel": "openai/gpt-oss-20b:free"
  },
  "Embedding": {
    "PrimaryProvider": "CloudflareWorkersAI",
    "FallbackProvider": "BgeM3Onnx",
    "FallbackEnabled": true,
    "FallbackCooldownSeconds": 60,
    "RequestTimeoutSeconds": 60,
    "ExpectedDimensions": 1024,
    "ModelDirectory": "../../../LLM/bge_m3"
  },
  "Rerank": {
    "Enabled": true,
    "PrimaryProvider": "CloudflareWorkersAI",
    "FallbackProvider": "BgeRerankerOnnx",
    "FallbackEnabled": true,
    "FallbackCooldownSeconds": 60,
    "RequestTimeoutSeconds": 60,
    "CandidateCount": 20,
    "ModelDirectory": "../../../LLM/bge_rerank_v2"
  }
}
```

`CloudflareAI:ApiToken`, `OpenRouter:ApiKey`, and the production Account ID must be supplied through User Secrets, environment variables, or deployment-platform secrets. They must never enter client publish artifacts or logs.

## 4. API Components

### 4.1 Large Language Models

The API implements its own `CloudflareChatClient : IChatClient` and calls the Cloudflare OpenAI-compatible HTTP endpoint directly. This client converts normal non-streaming responses, JSON Schema object-valued `message.content`, SSE streaming events, finish reasons, and basic usage metadata. It therefore preserves Cloudflare JSON Mode responses that a generic OpenAI .NET adapter can otherwise discard as empty text.

OpenRouter continues to use the existing OpenAI SDK client. `ILlmChatService` remains the business-layer boundary and owns model selection, response validation, automatic failover, and safe telemetry; business services do not distinguish the providers' transport implementations.

Configuration binding and client construction move from scattered `Program.cs` static methods into a focused registration extension, making the behavior testable and easier to extend with another provider.

### 4.2 Embedding

`IEmbeddingService` remains the shared boundary for resume saves, vector rebuilds, and queries. A new composite implementation calls Cloudflare BGE-M3 first and uses the existing local BGE-M3 ONNX implementation after a retryable failure.

Every Cloudflare response must be validated to ensure that:

- it contains exactly one vector for each requested text;
- every vector has `ExpectedDimensions` elements;
- every value is finite;
- vectors are not all zero and are normalized when needed to match the local implementation.

If both Cloudflare and local models fail, save and rebuild operations fail explicitly. Production vectors must not be populated with hashing embeddings because mixing semantic spaces would corrupt retrieval behavior.

### 4.3 Reranking

The new API receives its own `IRerankService` and does not reference the legacy backend implementation. `RagSearchService` first selects `CandidateCount` candidates using vector similarity and the existing lexical boost, calls the Cloudflare reranker, and finally truncates the list to the requested result count.

When Cloudflare reranking fails, the API calls the local ONNX reranker. If both rerankers fail, it records a warning and preserves the coarse retrieval order so retrieval still returns a degraded result. Reranking scores exist only for the current request, so the different primary and fallback models cannot contaminate persisted vectors.

## 5. Automatic Failover and Cooldown

The following conditions trigger automatic failover from Cloudflare to the corresponding fallback provider:

- Cloudflare is unconfigured while the fallback provider is configured correctly;
- network connection failure or provider timeout;
- HTTP `408`, `429`, or `5xx`;
- an empty text, embedding, or ranking result;
- malformed JSON, embedding, or reranking response structures;
- invalid embedding dimensions, values, or vector counts;
- a Cloudflare error stating that the selected model or JSON Mode cannot satisfy the request.

Failover must not occur for:

- explicit user cancellation;
- BioTwin input-validation failures;
- both primary and fallback providers being unconfigured;
- an LLM streaming failure after response text has already been sent to the client.

LLM, embedding, and reranking maintain independent in-memory health states and use a default 60-second cooldown. A failure in one model endpoint does not switch the other capabilities. Health state is not persisted and is cleared when the API restarts.

## 6. JSON and Resume Extraction

Resume extraction continues to send a JSON Schema and uses a Cloudflare model that officially supports JSON Mode. The business layer continues to request a low reasoning budget and reserves sufficient output tokens for the response body. The current Cloudflare primary model, `llama-3.3-70b-instruct-fp8-fast`, does not expose a reasoning input parameter, so the native Cloudflare client does not send one. An OpenRouter fallback model that supports reasoning still receives the setting through its SDK client.

For JSON requests, `LlmChatService` performs syntax-level JSON validation before returning. If Cloudflare returns empty content, explanatory prose, or truncated JSON, failover occurs before business-layer deserialization. `ResumeWizardExtractionService` still performs DTO deserialization, normalization, and up to two business attempts.

If both providers fail, the import job returns the existing actionable error. It does not save a partial resume or overwrite an existing resume.

## 7. Vector Migration and Consistency

Both Cloudflare and local paths use BGE-M3 and are required to produce 1024-dimensional vectors. Implementation and tests must still compare dimensions, normalization, and similarity ordering for the same sample texts instead of assuming compatibility from the model name alone.

The API does not rebuild existing vectors automatically during startup and does not require a database migration. After deployment, an administrator explicitly invokes the existing vector-rebuild API once so existing resumes are regenerated through the current Cloudflare primary path. The UI or API response must clearly identify this as a data-modifying operation.

A rebuild failure preserves the existing operation's error boundary and does not silently modify business data during startup. Persisted model versions and incremental migration may be designed separately if they become necessary later.

## 8. Streaming

`CloudflareChatClient` parses SSE `data:` events from the OpenAI-compatible endpoint directly, ignores blank lines and `[DONE]`, and converts `choices[].delta.content` into `ChatResponseUpdate` items. Streaming chat may fail over only before any response text has been emitted. If Cloudflare fails before the first answer chunk, the API restarts the request through OpenRouter. Once response text has started, the API records the provider failure and terminates the stream instead of producing duplicated or conflicting content. JSON Mode is not streamed.

## 9. Logging and Security

Provider logs may contain only the capability, provider name, model, elapsed time, HTTP status, finish reason, token counts, vector count and dimensions, candidate count, and whether failover occurred.

Logs must not contain resume Markdown, embedding input text, reranking queries or contexts, prompts, model response bodies, authorization headers, API tokens, Account IDs, cookies, profile hashes, or URL query strings.

Client remote logs continue to enter the BioTwin API through `POST /api/client-logs` and remain separate from provider telemetry.

## 10. Tests and Acceptance

Unit tests cover:

- a successful Cloudflare LLM response does not call OpenRouter;
- Cloudflare empty content, invalid JSON, timeout, `429`, and `5xx` trigger OpenRouter;
- successful Cloudflare embedding does not evaluate local inference results;
- Cloudflare embedding timeout, invalid dimensions, non-finite values, or empty results trigger local ONNX;
- failure of both embedding providers never writes hashing vectors;
- RAG performs coarse retrieval before returning Cloudflare-reranked results;
- a Cloudflare reranking failure uses the local reranker;
- failure of both rerankers preserves coarse retrieval order;
- user cancellation does not trigger failover for any capability;
- the three cooldown states remain independent and probe again after expiry;
- logs contain no prompts, resume content, embedding inputs, or credentials;
- configuration binding produces the expected Cloudflare endpoints, model names, and timeouts.

Integration verification uses completely synthetic resumes and retrieval samples and never sends real candidate information in diagnostic requests. It verifies that Cloudflare and local BGE-M3 produce equal dimensions and consistent similarity ordering for the samples. Final verification runs API, client, and solution tests/builds, then shuts down BioTwin, All2MD, and .NET build services.

## 11. Non-goals

- This change does not modify the legacy backend at `src/BioTwin_AI`.
- It does not move the ASP.NET Core API to Cloudflare Workers or Containers.
- It does not migrate SQLite, R2, or Vectorize.
- It does not remove local ONNX models or their download scripts.
- It does not allow the Blazor client to call Cloudflare directly.
- It does not persist provider health, model versions, or introduce Redis.
- It does not rebuild existing vectors automatically during API startup.
