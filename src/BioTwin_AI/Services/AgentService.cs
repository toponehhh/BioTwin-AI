using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using System.Text;

namespace BioTwin_AI.Services
{
    public sealed record AgentStreamChunk(string Kind, string Content)
    {
        public const string Answer = "answer";
        public const string Thinking = "thinking";
    }

    /// <summary>
    /// Service for AI agent interaction with LLM using RAG context.
    /// </summary>
    public class AgentService
    {
        private readonly IRagService _ragService;
        private readonly ILogger<AgentService> _logger;
        private readonly IChatClient _chatClient;
        private readonly CurrentUserSession _session;
        private readonly string _model;
        private readonly double _temperature;
        private readonly int _maxTokens;
        private readonly int _maxContextChars;
        private readonly int _maxSnippetChars;

        public AgentService(
            IRagService ragService,
            ILogger<AgentService> logger,
            IConfiguration config,
            IChatClient chatClient,
            CurrentUserSession session)
        {
            _ragService = ragService;
            _logger = logger;
            _chatClient = chatClient;
            _session = session;
            _model = config["LLM:Model"] ?? "openrouter/free";
            _temperature = config.GetValue("LLM:Temperature", 0.2);
            _maxTokens = config.GetValue("LLM:MaxTokens", 800);
            _maxContextChars = config.GetValue("LLM:MaxContextChars", 6000);
            _maxSnippetChars = config.GetValue("LLM:MaxSnippetChars", 2000);
        }

        /// <summary>
        /// Process interview question and generate response based on resume RAG.
        /// </summary>
        public async Task<string> AnswerQuestionAsync(string question)
        {
            try
            {
                var builder = new StringBuilder();

                await foreach (var chunk in StreamAnswerQuestionAsync(question))
                {
                    builder.Append(chunk);
                }

                return builder.Length == 0
                    ? "The model returned an empty response."
                    : builder.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to answer question with LLM");
                return "I apologize, but I encountered an error when calling the language model. Please verify LLM settings and try again.";
            }
        }

        /// <summary>
        /// Stream answer chunks for the provided question.
        /// </summary>
        public async IAsyncEnumerable<string> StreamAnswerQuestionAsync(
            string question,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var chunk in StreamAnswerQuestionChunksAsync(question, cancellationToken))
            {
                if (chunk.Kind == AgentStreamChunk.Answer)
                {
                    yield return chunk.Content;
                }
            }
        }

        /// <summary>
        /// Stream answer and thinking chunks for the provided question.
        /// </summary>
        public async IAsyncEnumerable<AgentStreamChunk> StreamAnswerQuestionChunksAsync(
            string question,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Processing question: {Question}", question);

            var relevantContent = await _ragService.SearchAsync(question, limit: 3);
            var context = BuildContext(relevantContent);
            var (systemPrompt, userPrompt) = BuildPrompts(question, context);
            var messages = BuildChatMessages(systemPrompt, userPrompt);

            var options = CreateChatOptions(_maxTokens);

            await foreach (var chunk in StreamChatAsync(messages, options, cancellationToken))
            {
                yield return chunk;
            }
        }

        private string BuildContext(IEnumerable<(string content, double score)> relevantContent)
        {
            var contextBuilder = new StringBuilder();
            contextBuilder.AppendLine("Top matched resume snippets:");
            var remaining = _maxContextChars;

            foreach (var (content, score) in relevantContent)
            {
                if (remaining <= 0)
                {
                    break;
                }

                var snippet = content;
                if (snippet.Length > _maxSnippetChars)
                {
                    snippet = snippet[.._maxSnippetChars] + "\n...[truncated]";
                }

                if (snippet.Length > remaining)
                {
                    if (remaining > 64)
                    {
                        snippet = snippet[..remaining] + "\n...[truncated]";
                    }
                    else
                    {
                        break;
                    }
                }

                contextBuilder.AppendLine($"- Relevance: {score:P0}");
                contextBuilder.AppendLine(snippet);
                contextBuilder.AppendLine();

                remaining = _maxContextChars - contextBuilder.Length;
            }

            return contextBuilder.ToString();
        }

        private (string SystemPrompt, string UserPrompt) BuildPrompts(string question, string context)
        {
            string systemPrompt;
            if (_session.IsInterviewer)
            {
                systemPrompt = """
You are an interview assistant helping the interviewer review candidate resumes.
Analyze the provided resume context from one or more candidates and answer questions about them.
When referencing a candidate, use the name/username shown in the context (e.g. "[username - title]").
Provide objective, factual summaries based solely on the resume data provided.
If context is insufficient, clearly state that the information is not available.
""";
            }
            else
            {
                systemPrompt = """
You are a professional interview candidate assistant.
Answer in first-person as the candidate.
Only use the provided resume context when answering factual experience questions.
If context is insufficient, honestly say you do not have enough information.
Keep answers concise and interview-friendly.
""";
            }

            var userPrompt = $"""
Question:
{question}

Resume Context:
{context}
""";

            return (systemPrompt, userPrompt);
        }

        private ChatMessage[] BuildChatMessages(string systemPrompt, string userPrompt)
        {
            return
            [
                new ChatMessage(ChatRole.System, systemPrompt),
                new ChatMessage(ChatRole.User, userPrompt)
            ];
        }

        private ChatOptions CreateChatOptions(int maxOutputTokens)
        {
            return new ChatOptions
            {
                ModelId = _model,
                Temperature = (float)_temperature,
                MaxOutputTokens = maxOutputTokens
            };
        }

        private async IAsyncEnumerable<AgentStreamChunk> StreamChatAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, options, cancellationToken))
            {
                foreach (var chunk in ConvertUpdate(update))
                {
                    yield return chunk;
                }
            }
        }

        private static IEnumerable<AgentStreamChunk> ConvertUpdate(ChatResponseUpdate update)
        {
            var emittedAnswerContent = false;

            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case TextReasoningContent reasoningContent when !string.IsNullOrEmpty(reasoningContent.Text):
                        yield return new AgentStreamChunk(AgentStreamChunk.Thinking, reasoningContent.Text);
                        break;

                    case TextContent textContent when !string.IsNullOrEmpty(textContent.Text):
                        emittedAnswerContent = true;
                        yield return new AgentStreamChunk(AgentStreamChunk.Answer, textContent.Text);
                        break;
                }
            }

            if (!emittedAnswerContent && !string.IsNullOrEmpty(update.Text))
            {
                yield return new AgentStreamChunk(AgentStreamChunk.Answer, update.Text);
            }
        }
    }
}
