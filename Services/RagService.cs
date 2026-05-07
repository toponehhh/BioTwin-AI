using BioTwin_AI.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BioTwin_AI.Services
{
    /// <summary>
    /// Service for managing RAG (Retrieval-Augmented Generation) operations
    /// with sqlite-vec stored in the same SQLite database file.
    /// </summary>
    public class RagService
    {
        private readonly string _connectionString;
        private readonly ILogger<RagService> _logger;
        private const int VECTOR_SIZE = 128;

        public RagService(BioTwinDbContext dbContext, ILogger<RagService> logger)
        {
            _connectionString = dbContext.Database.GetConnectionString()
                ?? throw new InvalidOperationException("SQLite connection string is not configured.");
            _logger = logger;
        }

        /// <summary>
        /// Initialize sqlite-vec tables if they don't exist.
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                using var connection = OpenVectorConnection();
                using var command = connection.CreateCommand();
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS rag_chunks (
                        id TEXT PRIMARY KEY,
                        content TEXT NOT NULL,
                        metadata_json TEXT,
                        created_at TEXT NOT NULL
                    );

                    CREATE VIRTUAL TABLE IF NOT EXISTS rag_vectors USING vec0(
                        embedding float[128]
                    );

                    CREATE TABLE IF NOT EXISTS rag_vector_map (
                        vector_rowid INTEGER PRIMARY KEY,
                        chunk_id TEXT NOT NULL UNIQUE,
                        FOREIGN KEY(chunk_id) REFERENCES rag_chunks(id) ON DELETE CASCADE
                    );
                ";

                await command.ExecuteNonQueryAsync();
                _logger.LogInformation("sqlite-vec tables ready");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize sqlite-vec tables");
            }
        }

        /// <summary>
        /// Store an embedding in the vector database
        /// </summary>
        public async Task<string> StoreEmbeddingAsync(string content, Dictionary<string, string> metadata)
        {
            try
            {
                var pointId = Guid.NewGuid().ToString();
                var embedding = GenerateDeterministicEmbedding(content);
                var embeddingJson = SerializeVector(embedding);
                var metadataJson = JsonSerializer.Serialize(metadata);

                using var connection = OpenVectorConnection();
                using var transaction = connection.BeginTransaction();

                using (var chunkCommand = connection.CreateCommand())
                {
                    chunkCommand.Transaction = transaction;
                    chunkCommand.CommandText = @"
                        INSERT INTO rag_chunks (id, content, metadata_json, created_at)
                        VALUES (@id, @content, @metadata, @createdAt);
                    ";
                    chunkCommand.Parameters.AddWithValue("@id", pointId);
                    chunkCommand.Parameters.AddWithValue("@content", content);
                    chunkCommand.Parameters.AddWithValue("@metadata", metadataJson);
                    chunkCommand.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("O"));
                    await chunkCommand.ExecuteNonQueryAsync();
                }

                long vectorRowId;
                using (var vectorCommand = connection.CreateCommand())
                {
                    vectorCommand.Transaction = transaction;
                    vectorCommand.CommandText = "INSERT INTO rag_vectors (embedding) VALUES (@embedding);";
                    vectorCommand.Parameters.AddWithValue("@embedding", embeddingJson);
                    await vectorCommand.ExecuteNonQueryAsync();
                }

                using (var rowIdCommand = connection.CreateCommand())
                {
                    rowIdCommand.Transaction = transaction;
                    rowIdCommand.CommandText = "SELECT last_insert_rowid();";
                    vectorRowId = (long)(await rowIdCommand.ExecuteScalarAsync() ?? 0L);
                }

                using (var mapCommand = connection.CreateCommand())
                {
                    mapCommand.Transaction = transaction;
                    mapCommand.CommandText = @"
                        INSERT INTO rag_vector_map (vector_rowid, chunk_id)
                        VALUES (@rowid, @chunkId);
                    ";
                    mapCommand.Parameters.AddWithValue("@rowid", vectorRowId);
                    mapCommand.Parameters.AddWithValue("@chunkId", pointId);
                    await mapCommand.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();

                _logger.LogInformation("Stored embedding with ID {PointId}", pointId);
                return pointId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to store embedding");
                throw;
            }
        }

        /// <summary>
        /// Search for similar resume entries based on query
        /// </summary>
        public async Task<List<(string Content, double Score)>> SearchAsync(string query, int limit = 5)
        {
            try
            {
                _logger.LogInformation("Searching for query: {Query}", query);

                var queryEmbedding = GenerateDeterministicEmbedding(query);
                var queryEmbeddingJson = SerializeVector(queryEmbedding);
                var results = new List<(string Content, double Score)>();

                using var connection = OpenVectorConnection();
                using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT c.content, v.distance
                    FROM rag_vectors AS v
                    JOIN rag_vector_map AS m ON m.vector_rowid = v.rowid
                    JOIN rag_chunks AS c ON c.id = m.chunk_id
                    WHERE v.embedding MATCH @queryEmbedding
                      AND k = @k
                    ORDER BY v.distance;
                ";
                command.Parameters.AddWithValue("@queryEmbedding", queryEmbeddingJson);
                command.Parameters.AddWithValue("@k", limit);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var content = reader.GetString(0);
                    var distance = reader.GetDouble(1);
                    var score = 1.0 / (1.0 + distance);
                    results.Add((content, score));
                }

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to search embeddings");
                return new List<(string, double)>();
            }
        }

        /// <summary>
        /// Delete all embeddings for cleanup
        /// </summary>
        public async Task ClearAsync()
        {
            try
            {
                using var connection = OpenVectorConnection();
                using var command = connection.CreateCommand();
                command.CommandText = @"
                    DELETE FROM rag_vector_map;
                    DELETE FROM rag_vectors;
                    DELETE FROM rag_chunks;
                ";
                await command.ExecuteNonQueryAsync();

                _logger.LogInformation("Cleared all sqlite-vec embeddings");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear embeddings");
            }
        }

        private SqliteConnection OpenVectorConnection()
        {
            var connection = new SqliteConnection(_connectionString);
            connection.Open();
            connection.EnableExtensions(true);
            connection.LoadVector();
            return connection;
        }

        private static float[] GenerateDeterministicEmbedding(string text)
        {
            var vector = new float[VECTOR_SIZE];
            var tokens = text
                .ToLowerInvariant()
                .Split(new[] { ' ', '\r', '\n', '\t', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\'' },
                    StringSplitOptions.RemoveEmptyEntries);

            foreach (var token in tokens)
            {
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
                var index = BitConverter.ToInt32(hash, 0) % VECTOR_SIZE;
                if (index < 0)
                {
                    index += VECTOR_SIZE;
                }

                var sign = (hash[4] & 1) == 0 ? 1f : -1f;
                var magnitude = (hash[5] / 255f) + 0.01f;
                vector[index] += sign * magnitude;
            }

            Normalize(vector);
            return vector;
        }

        private static void Normalize(float[] vector)
        {
            double sum = 0;
            for (var i = 0; i < vector.Length; i++)
            {
                sum += vector[i] * vector[i];
            }

            if (sum <= double.Epsilon)
            {
                return;
            }

            var norm = (float)Math.Sqrt(sum);
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] /= norm;
            }
        }

        private static string SerializeVector(float[] vector)
        {
            return "[" + string.Join(",", vector.Select(v => v.ToString("G9", CultureInfo.InvariantCulture))) + "]";
        }
    }
}
