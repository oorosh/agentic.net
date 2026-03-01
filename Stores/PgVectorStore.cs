using Npgsql;
using Agentic.Abstractions;

namespace Agentic.Stores;

public sealed class PgVectorStore : IVectorStore, IDisposable
{
    private readonly string _connectionString;
    private readonly string _tableName;
    private readonly int _dimensions;
    private NpgsqlConnection? _connection;
    private SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public int Dimensions => _dimensions;

    public PgVectorStore(string connectionString, int dimensions = 1536, string tableName = "embeddings")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        if (dimensions <= 0) throw new ArgumentOutOfRangeException(nameof(dimensions), "Dimensions must be positive.");
        ValidateTableName(tableName);

        _connectionString = connectionString;
        _dimensions = dimensions;
        _tableName = tableName;
    }

    // Validate table name to prevent SQL injection: only allow alphanumeric and underscores.
    private static void ValidateTableName(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name must not be empty.", nameof(tableName));

        foreach (var c in tableName)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
                throw new ArgumentException($"Table name '{tableName}' contains invalid characters. Only letters, digits, and underscores are allowed.", nameof(tableName));
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;

            _connection = new NpgsqlConnection(_connectionString);
            await _connection.OpenAsync(cancellationToken);

            var cmd = _connection.CreateCommand();
            cmd.CommandText = $@"
                CREATE EXTENSION IF NOT EXISTS vector;
                
                CREATE TABLE IF NOT EXISTS {_tableName} (
                    id TEXT PRIMARY KEY,
                    vector vector({_dimensions}) NOT NULL
                );
                
                CREATE INDEX IF NOT EXISTS {_tableName}_vector_idx 
                ON {_tableName} USING hnsw (vector vector_cosine_ops);
            ";
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task UpsertAsync(string id, float[] vector, CancellationToken cancellationToken = default)
    {
        if (!_initialized) throw new InvalidOperationException("Vector store not initialized.");
        if (vector.Length != _dimensions)
            throw new ArgumentException($"Vector dimension must be {_dimensions}, got {vector.Length}.");

        var cmd = _connection!.CreateCommand();
        cmd.CommandText = $@"
            INSERT INTO {_tableName} (id, vector) VALUES ($1, $2)
            ON CONFLICT (id) DO UPDATE SET vector = EXCLUDED.vector
        ";
        cmd.Parameters.AddWithValue("$1", id);
        cmd.Parameters.AddWithValue("$2", ToPgVector(vector));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<(string Id, float[] Vector, float Score)>> SearchAsync(
        float[] queryVector,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        if (!_initialized) throw new InvalidOperationException("Vector store not initialized.");
        if (queryVector.Length != _dimensions)
            throw new ArgumentException($"Query vector dimension must be {_dimensions}, got {queryVector.Length}.");

        var cmd = _connection!.CreateCommand();
        cmd.CommandText = $@"
            SELECT id, vector, (vector <=> $1) AS distance
            FROM {_tableName}
            ORDER BY vector <=> $1
            LIMIT $2
        ";
        cmd.Parameters.AddWithValue("$1", ToPgVector(queryVector.AsSpan()));
        cmd.Parameters.AddWithValue("$2", topK);

        var results = new List<(string Id, float[] Vector, float Score)>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetString(0);
            var vector = FromPgVector(reader.GetFieldValue<string>(1));
            var distance = (float)reader.GetDouble(2);
            results.Add(new(id, vector, 1 - distance));
        }

        return results;
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!_initialized) throw new InvalidOperationException("Vector store not initialized.");

        var cmd = _connection!.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_tableName} WHERE id = $1";
        cmd.Parameters.AddWithValue("$1", id);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        if (!_initialized) throw new InvalidOperationException("Vector store not initialized.");

        var cmd = _connection!.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_tableName}";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string ToPgVector(ReadOnlySpan<float> vector)
    {
        return "[" + string.Join(",", vector.ToArray()) + "]";
    }

    private static float[] FromPgVector(string pgVector)
    {
        var trimmed = pgVector.Trim('[', ']');
        if (string.IsNullOrEmpty(trimmed)) return [];
        return trimmed.Split(',').Select(float.Parse).ToArray();
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _initLock.Dispose();
    }
}
