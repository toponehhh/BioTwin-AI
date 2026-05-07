using BioTwin_AI.Data;
using System.Text;

namespace BioTwin_AI.Services
{
    /// <summary>
    /// Service for AI agent interaction with LLM using RAG context
    /// </summary>
    public class AgentService
    {
        private readonly RagService _ragService;
        private readonly BioTwinDbContext _dbContext;
        private readonly ILogger<AgentService> _logger;
        private readonly string _llmBaseUrl;

        public AgentService(
            RagService ragService,
            BioTwinDbContext dbContext,
            ILogger<AgentService> logger,
            IConfiguration config)
        {
            _ragService = ragService;
            _dbContext = dbContext;
            _logger = logger;
            _llmBaseUrl = config["LLM:BaseUrl"] ?? "http://localhost:5000";
        }

        /// <summary>
        /// Process interview question and generate response based on resume RAG
        /// </summary>
        public async Task<string> AnswerQuestionAsync(string question)
        {
            try
            {
                _logger.LogInformation("Processing question: {Question}", question);

                // Step 1: Retrieve relevant resume context from vector DB
                var relevantContent = await _ragService.SearchAsync(question, limit: 3);

                var contextBuilder = new StringBuilder();
                contextBuilder.AppendLine("## Relevant Resume Context:");
                foreach (var (content, score) in relevantContent)
                {
                    contextBuilder.AppendLine($"(Relevance: {score:P0})");
                    contextBuilder.AppendLine(content);
                    contextBuilder.AppendLine();
                }

                // Step 2: For prototype, return structured response
                var response = GeneratePrototypeResponse(question, contextBuilder.ToString());

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to answer question");
                return "I apologize, but I encountered an error processing your question. Please try again.";
            }
        }

        /// <summary>
        /// Prototype response generation (replace with real LLM call in production)
        private string GeneratePrototypeResponse(string question, string context)
        {
            var response = new StringBuilder();

            response.AppendLine($"**Question:** {question}\n");
            response.AppendLine("**Response:**");
            response.AppendLine();

            // Simple keyword matching for prototype
            if (question.ToLower().Contains("concurrency") || question.ToLower().Contains("高并发"))
            {
                response.AppendLine("Yes, I have hands-on experience with high-concurrency systems. From the relevant projects in my background:");
                response.AppendLine(context);
                response.AppendLine("I've successfully handled complex concurrency challenges in production environments.");
            }
            else if (question.ToLower().Contains("skill") || question.ToLower().Contains("技术"))
            {
                response.AppendLine("I possess a strong technical skill set:");
                response.AppendLine(context);
            }
            else
            {
                response.AppendLine("Great question. Based on my experience:");
                response.AppendLine(context);
            }

            return response.ToString();
        }
    }
}
