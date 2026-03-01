using System.Data;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Agentic.Abstractions;

namespace Agentic.Core;

public sealed class SqliteMemoryService : IMemoryService, IDisposable
{
    private readonly string _connectionString;
    private readonly IVectorStore? _vectorStore;
    private SqliteConnection? _connection;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public SqliteMemoryService(string dbPath, IVectorStore? vectorStore = null)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath
        }.ToString();
        _vectorStore = vectorStore;
    }

    public SqliteMemoryService(IVectorStore vectorStore) : this(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "memory.db"), vectorStore) { }

    public SqliteMemoryService() : this(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "memory.db"), null) { }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;

            _connection = new SqliteConnection(_connectionString);
            await _connection.OpenAsync(cancellationToken);

            var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS memory (
                    id TEXT PRIMARY KEY,
                    content TEXT NOT NULL
                );
            ";
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            try
            {
                var alterCmd = _connection.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE memory ADD COLUMN embedding TEXT;";
                await alterCmd.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (SqliteException ex) when (ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
            {
                // Column already exists from a previous migration — safe to ignore.
            }

            if (_vectorStore is not null)
            {
                await _vectorStore.InitializeAsync(cancellationToken);
            }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task StoreMessageAsync(string id, string content, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var cmd = _connection!.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO memory(id, content) VALUES($id, $content);";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$content", content);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> RetrieveRelevantAsync(string query, int topK = 5, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        List<string> list = new();

        if (tokens.Length == 0)
        {
            var cmd0 = _connection!.CreateCommand();
            cmd0.CommandText = "SELECT content FROM memory ORDER BY rowid DESC LIMIT $limit";
            cmd0.Parameters.AddWithValue("$limit", topK);

            await using var rdr0 = await cmd0.ExecuteReaderAsync(cancellationToken);
            while (await rdr0.ReadAsync(cancellationToken))
            {
                list.Add(rdr0.GetString(0));
            }

            return list;
        }

        var sql = "SELECT content FROM memory WHERE";
        for (var i = 0; i < tokens.Length; i++)
        {
            if (i > 0) sql += " OR";
            sql += " content LIKE $token" + i;
        }
        sql += " ORDER BY rowid DESC LIMIT $limit";

        var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        for (var i = 0; i < tokens.Length; i++)
            cmd.Parameters.AddWithValue("$token" + i, "%" + tokens[i] + "%");
        cmd.Parameters.AddWithValue("$limit", topK);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(reader.GetString(0));
        }

        if (list.Count == 0)
        {
            var cmd2 = _connection!.CreateCommand();
            cmd2.CommandText = "SELECT content FROM memory ORDER BY rowid DESC LIMIT $limit";
            cmd2.Parameters.AddWithValue("$limit", topK);

            await using var rdr2 = await cmd2.ExecuteReaderAsync(cancellationToken);
            while (await rdr2.ReadAsync(cancellationToken))
            {
                list.Add(rdr2.GetString(0));
            }
        }

        return list;
    }

    public async Task StoreEmbeddingAsync(string id, float[] embedding, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        if (_vectorStore is not null)
        {
            await _vectorStore.UpsertAsync(id, embedding, cancellationToken);
            return;
        }

        var cmd = _connection!.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO memory(id, content, embedding) VALUES($id, COALESCE((SELECT content FROM memory WHERE id = $id), ''), $embedding);";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$embedding", JsonSerializer.Serialize(embedding));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<(string Content, float Score)>> RetrieveSimilarAsync(float[] queryEmbedding, int topK = 5, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        if (_vectorStore is not null)
        {
            var results = await _vectorStore.SearchAsync(queryEmbedding, topK, cancellationToken);
            var contents = new List<(string Content, float Score)>();
            foreach (var (id, _, score) in results)
            {
                var content = await GetContentByIdAsync(id, cancellationToken);
                if (content is not null)
                {
                    contents.Add((content, score));
                }
            }
            return contents;
        }

        var cmd = _connection!.CreateCommand();
        // Load only the most recent rows to bound memory usage; cosine scoring is done in-process.
        cmd.CommandText = "SELECT content, embedding FROM memory WHERE embedding IS NOT NULL ORDER BY rowid DESC LIMIT 1000;";

        var similarities = new List<(string Content, float Score)>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var content = reader.GetString(0);
            var embeddingJson = reader.GetString(1);
            var embedding = JsonSerializer.Deserialize<float[]>(embeddingJson)!;
            var score = VectorMath.CosineSimilarity(queryEmbedding, embedding);
            similarities.Add((content, score));
        }

        return similarities
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .ToList();
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("Memory service not initialized. Call InitializeAsync first.");
        }
    }

    public async Task DeleteMessageAsync(string id, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var cmd = _connection!.CreateCommand();
        cmd.CommandText = "DELETE FROM memory WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        if (_vectorStore is not null)
        {
            await _vectorStore.DeleteAsync(id, cancellationToken);
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var cmd = _connection!.CreateCommand();
        cmd.CommandText = "DELETE FROM memory;";
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        if (_vectorStore is not null)
        {
            await _vectorStore.DeleteAllAsync(cancellationToken);
        }
    }

    private async Task<string?> GetContentByIdAsync(string id, CancellationToken cancellationToken)
    {
        var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT content FROM memory WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result as string;
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _initLock.Dispose();
        if (_vectorStore is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
