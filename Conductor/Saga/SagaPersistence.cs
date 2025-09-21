using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Conductor.Saga;

// In-Memory implementation for demonstration
public class InMemorySagaPersistence<TSagaState> : ISagaPersistence<TSagaState> where TSagaState : ISagaState
{
    private readonly ConcurrentDictionary<Guid, string> _sagas = new();
    private readonly ILogger<InMemorySagaPersistence<TSagaState>> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public InMemorySagaPersistence(ILogger<InMemorySagaPersistence<TSagaState>> logger)
    {
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
    }

    public Task<TSagaState?> GetAsync(Guid sagaId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_sagas.TryGetValue(sagaId, out var json))
        {
            var sagaState = JsonSerializer.Deserialize<TSagaState>(json, _jsonOptions);
            return Task.FromResult(sagaState);
        }

        return Task.FromResult<TSagaState?>(default);
    }

    public Task<TSagaState> SaveAsync(TSagaState sagaState, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sagaState);
        cancellationToken.ThrowIfCancellationRequested();

        var json = JsonSerializer.Serialize(sagaState, _jsonOptions);
        _sagas.AddOrUpdate(sagaState.SagaId, json, (key, oldValue) => json);

        _logger.LogDebug("Saved saga {SagaId} with status {Status}", sagaState.SagaId, sagaState.Status);

        return Task.FromResult(sagaState);
    }

    public Task<bool> DeleteAsync(Guid sagaId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var removed = _sagas.TryRemove(sagaId, out _);

        if (removed)
        {
            _logger.LogDebug("Deleted saga {SagaId}", sagaId);
        }

        return Task.FromResult(removed);
    }

    public Task<IEnumerable<TSagaState>> GetByStatusAsync(SagaStatus status, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sagas = _sagas.Values
            .Select(json => JsonSerializer.Deserialize<TSagaState>(json, _jsonOptions))
            .Where(saga => saga != null && saga.Status == status)
            .Cast<TSagaState>();

        return Task.FromResult(sagas);
    }

    public Task<IEnumerable<TSagaState>> GetTimeoutedSagasAsync(DateTime before, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sagas = _sagas.Values
            .Select(json => JsonSerializer.Deserialize<TSagaState>(json, _jsonOptions))
            .Where(saga => saga != null &&
                           saga.Status == SagaStatus.Running &&
                           saga.Metadata.Timeout.HasValue &&
                           saga.CreatedAt.Add(saga.Metadata.Timeout.Value) < before)
            .Cast<TSagaState>();

        return Task.FromResult(sagas);
    }

    public Task<IEnumerable<TSagaState>> GetByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(correlationId);
        cancellationToken.ThrowIfCancellationRequested();

        var sagas = _sagas.Values
            .Select(json => JsonSerializer.Deserialize<TSagaState>(json, _jsonOptions))
            .Where(saga => saga != null && saga.CorrelationId == correlationId)
            .Cast<TSagaState>();

        return Task.FromResult(sagas);
    }

    public Task<SagaStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var allSagas = _sagas.Values
            .Select(json => JsonSerializer.Deserialize<TSagaState>(json, _jsonOptions))
            .Where(saga => saga != null)
            .Cast<TSagaState>()
            .ToList();

        var statistics = new SagaStatistics
        {
            TotalSagas = allSagas.Count,
            RunningSagas = allSagas.Count(s => s.Status == SagaStatus.Running),
            CompletedSagas = allSagas.Count(s => s.Status == SagaStatus.Completed),
            FailedSagas = allSagas.Count(s => s.Status == SagaStatus.Failed),
            CompensatingSagas = allSagas.Count(s => s.Status == SagaStatus.Compensating),
            CompensatedSagas = allSagas.Count(s => s.Status == SagaStatus.Compensated),
            SuspendedSagas = allSagas.Count(s => s.Status == SagaStatus.Suspended),
            TimedOutSagas = allSagas.Count(s => s.Status == SagaStatus.TimedOut),
            SagasByType = allSagas.GroupBy(s => s.SagaType).ToDictionary(g => g.Key, g => g.Count())
        };

        var completedSagas = allSagas.Where(s => s.CompletedAt.HasValue).ToList();
        if (completedSagas.Any())
        {
            var totalExecutionTime = completedSagas.Sum(s => (s.CompletedAt!.Value - s.CreatedAt).TotalMilliseconds);
            statistics.AverageExecutionTime = TimeSpan.FromMilliseconds(totalExecutionTime / completedSagas.Count);
        }

        return Task.FromResult(statistics);
    }
}

// SQL Server implementation example
public class SqlServerSagaPersistence<TSagaState> : ISagaPersistence<TSagaState> where TSagaState : ISagaState
{
    private readonly string _connectionString;
    private readonly ILogger<SqlServerSagaPersistence<TSagaState>> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public SqlServerSagaPersistence(string connectionString, ILogger<SqlServerSagaPersistence<TSagaState>> logger)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<TSagaState?> GetAsync(Guid sagaId, CancellationToken cancellationToken = default)
    {
        // Implementation would use Entity Framework or direct SQL
        // This is a placeholder showing the structure

        const string sql = @"
            SELECT SagaData, Version, LastUpdatedAt
            FROM Sagas
            WHERE SagaId = @sagaId";

        // using var connection = new SqlConnection(_connectionString);
        // var json = await connection.QuerySingleOrDefaultAsync<string>(sql, new { sagaId });

        // if (json != null)
        // {
        //     return JsonSerializer.Deserialize<TSagaState>(json, _jsonOptions);
        // }

        await Task.CompletedTask; // Placeholder
        return default;
    }

    public async Task<TSagaState> SaveAsync(TSagaState sagaState, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sagaState);
        cancellationToken.ThrowIfCancellationRequested();

        const string sql = @"
            MERGE Sagas AS target
            USING (SELECT @sagaId AS SagaId) AS source
            ON target.SagaId = source.SagaId
            WHEN MATCHED THEN
                UPDATE SET
                    SagaData = @sagaData,
                    Status = @status,
                    CurrentStep = @currentStep,
                    Version = @version,
                    LastUpdatedAt = @lastUpdatedAt,
                    CompletedAt = @completedAt
            WHEN NOT MATCHED THEN
                INSERT (SagaId, SagaType, SagaData, Status, CurrentStep, Version, CreatedAt, LastUpdatedAt, CompletedAt, CorrelationId)
                VALUES (@sagaId, @sagaType, @sagaData, @status, @currentStep, @version, @createdAt, @lastUpdatedAt, @completedAt, @correlationId);";

        var sagaData = JsonSerializer.Serialize(sagaState, _jsonOptions);

        // Implementation would execute the SQL
        // using var connection = new SqlConnection(_connectionString);
        // await connection.ExecuteAsync(sql, new
        // {
        //     sagaId = sagaState.SagaId,
        //     sagaType = sagaState.SagaType,
        //     sagaData,
        //     status = sagaState.Status.ToString(),
        //     currentStep = sagaState.CurrentStep,
        //     version = sagaState.Version,
        //     createdAt = sagaState.CreatedAt,
        //     lastUpdatedAt = sagaState.LastUpdatedAt,
        //     completedAt = sagaState.CompletedAt,
        //     correlationId = sagaState.CorrelationId
        // });

        _logger.LogDebug("Saved saga {SagaId} to SQL Server", sagaState.SagaId);

        await Task.CompletedTask; // Placeholder
        return sagaState;
    }

    public async Task<bool> DeleteAsync(Guid sagaId, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM Sagas WHERE SagaId = @sagaId";

        // using var connection = new SqlConnection(_connectionString);
        // var rowsAffected = await connection.ExecuteAsync(sql, new { sagaId });
        // return rowsAffected > 0;

        await Task.CompletedTask; // Placeholder
        return true;
    }

    public async Task<IEnumerable<TSagaState>> GetByStatusAsync(SagaStatus status, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT SagaData FROM Sagas WHERE Status = @status";

        // using var connection = new SqlConnection(_connectionString);
        // var jsonResults = await connection.QueryAsync<string>(sql, new { status = status.ToString() });

        // return jsonResults.Select(json => JsonSerializer.Deserialize<TSagaState>(json, _jsonOptions))
        //                   .Where(saga => saga != null)
        //                   .Cast<TSagaState>();

        await Task.CompletedTask; // Placeholder
        return Enumerable.Empty<TSagaState>();
    }

    public async Task<IEnumerable<TSagaState>> GetTimeoutedSagasAsync(DateTime before, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT SagaData
            FROM Sagas
            WHERE Status = 'Running'
            AND JSON_VALUE(SagaData, '$.metadata.timeout') IS NOT NULL
            AND DATEADD(ms, CAST(JSON_VALUE(SagaData, '$.metadata.timeout') AS int), CreatedAt) < @before";

        // Implementation would execute the SQL query

        await Task.CompletedTask; // Placeholder
        return Enumerable.Empty<TSagaState>();
    }

    public async Task<IEnumerable<TSagaState>> GetByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT SagaData FROM Sagas WHERE CorrelationId = @correlationId";

        // Implementation would execute the SQL query

        await Task.CompletedTask; // Placeholder
        return Enumerable.Empty<TSagaState>();
    }

    public async Task<SagaStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                COUNT(*) as TotalSagas,
                SUM(CASE WHEN Status = 'Running' THEN 1 ELSE 0 END) as RunningSagas,
                SUM(CASE WHEN Status = 'Completed' THEN 1 ELSE 0 END) as CompletedSagas,
                SUM(CASE WHEN Status = 'Failed' THEN 1 ELSE 0 END) as FailedSagas,
                SUM(CASE WHEN Status = 'Compensating' THEN 1 ELSE 0 END) as CompensatingSagas,
                SUM(CASE WHEN Status = 'Compensated' THEN 1 ELSE 0 END) as CompensatedSagas,
                SUM(CASE WHEN Status = 'Suspended' THEN 1 ELSE 0 END) as SuspendedSagas,
                SUM(CASE WHEN Status = 'TimedOut' THEN 1 ELSE 0 END) as TimedOutSagas,
                AVG(CASE WHEN CompletedAt IS NOT NULL
                    THEN DATEDIFF(ms, CreatedAt, CompletedAt)
                    ELSE NULL END) as AverageExecutionTimeMs
            FROM Sagas";

        // Implementation would execute the SQL query and build statistics

        await Task.CompletedTask; // Placeholder
        return new SagaStatistics();
    }
}

// MongoDB implementation example
public class MongoSagaPersistence<TSagaState> : ISagaPersistence<TSagaState> where TSagaState : ISagaState
{
    private readonly ILogger<MongoSagaPersistence<TSagaState>> _logger;

    public MongoSagaPersistence(ILogger<MongoSagaPersistence<TSagaState>> logger)
    {
        _logger = logger;
    }

    // Implementation would use MongoDB.Driver
    // Similar structure to SQL implementation but using MongoDB queries

    public Task<TSagaState?> GetAsync(Guid sagaId, CancellationToken cancellationToken = default)
    {
        // var filter = Builders<TSagaState>.Filter.Eq(s => s.SagaId, sagaId);
        // return await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);

        return Task.FromResult<TSagaState?>(default);
    }

    public Task<TSagaState> SaveAsync(TSagaState sagaState, CancellationToken cancellationToken = default)
    {
        // var filter = Builders<TSagaState>.Filter.Eq(s => s.SagaId, sagaState.SagaId);
        // await _collection.ReplaceOneAsync(filter, sagaState, new ReplaceOptions { IsUpsert = true }, cancellationToken);

        return Task.FromResult(sagaState);
    }

    public Task<bool> DeleteAsync(Guid sagaId, CancellationToken cancellationToken = default)
    {
        // var filter = Builders<TSagaState>.Filter.Eq(s => s.SagaId, sagaId);
        // var result = await _collection.DeleteOneAsync(filter, cancellationToken);
        // return result.DeletedCount > 0;

        return Task.FromResult(true);
    }

    public Task<IEnumerable<TSagaState>> GetByStatusAsync(SagaStatus status, CancellationToken cancellationToken = default)
    {
        // var filter = Builders<TSagaState>.Filter.Eq(s => s.Status, status);
        // return await _collection.Find(filter).ToListAsync(cancellationToken);

        return Task.FromResult(Enumerable.Empty<TSagaState>());
    }

    public Task<IEnumerable<TSagaState>> GetTimeoutedSagasAsync(DateTime before, CancellationToken cancellationToken = default)
    {
        // Complex MongoDB query for timeout detection
        return Task.FromResult(Enumerable.Empty<TSagaState>());
    }

    public Task<IEnumerable<TSagaState>> GetByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default)
    {
        // var filter = Builders<TSagaState>.Filter.Eq(s => s.CorrelationId, correlationId);
        // return await _collection.Find(filter).ToListAsync(cancellationToken);

        return Task.FromResult(Enumerable.Empty<TSagaState>());
    }

    public Task<SagaStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        // MongoDB aggregation pipeline for statistics
        return Task.FromResult(new SagaStatistics());
    }
}