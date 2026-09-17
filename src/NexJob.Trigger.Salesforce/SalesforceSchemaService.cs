using System.Collections;
using System.Collections.Concurrent;
using System.Text.Json;
using Avro;
using Avro.Generic;
using Avro.IO;
using Microsoft.Extensions.Logging;

namespace NexJob.Trigger.Salesforce;

/// <summary>
/// Service responsible for caching Avro schemas and deserializing binary event payloads to JSON.
/// </summary>
public sealed class SalesforceSchemaService : ISalesforceSchemaService
{
    private readonly ConcurrentDictionary<string, Schema> _schemaCache = new(StringComparer.Ordinal);
    private readonly Func<string, CancellationToken, Task<string>>? _schemaFetcher;
    private readonly ILogger<SalesforceSchemaService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SalesforceSchemaService"/> class.
    /// </summary>
    /// <param name="schemaFetcher">Optional remote fetcher for unknown schemas.</param>
    /// <param name="logger">Diagnostic logger.</param>
    public SalesforceSchemaService(
        Func<string, CancellationToken, Task<string>>? schemaFetcher,
        ILogger<SalesforceSchemaService> logger)
    {
        _schemaFetcher = schemaFetcher;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public void RegisterSchema(string schemaId, string schemaJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaId);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaJson);

        var schema = Schema.Parse(schemaJson);
        _schemaCache[schemaId] = schema;
        _logger.LogDebug("Registered schema {SchemaId} in cache.", schemaId);
    }

    /// <inheritdoc/>
    public async ValueTask<string> DecodePayloadToJsonAsync(string schemaId, byte[] payloadBytes, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaId);
        ArgumentNullException.ThrowIfNull(payloadBytes);

        if (payloadBytes.Length == 0)
        {
            return "{}";
        }

        var schema = await GetOrFetchSchemaAsync(schemaId, ct).ConfigureAwait(false);

        try
        {
            using var stream = new MemoryStream(payloadBytes);
            var decoder = new BinaryDecoder(stream);
            var reader = new GenericDatumReader<GenericRecord>(schema, schema);
            var record = reader.Read(reuse: default!, decoder);

            if (record is null)
            {
                return "{}";
            }

            var dictionary = ToDictionary(record);
            return JsonSerializer.Serialize(dictionary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decode Avro binary payload for schema {SchemaId}", schemaId);
            throw new InvalidOperationException($"Failed to decode Avro payload for schema {schemaId}: {ex.Message}", ex);
        }
    }

    private static Dictionary<string, object?> ToDictionary(GenericRecord record)
    {
        if (record.Schema is RecordSchema recordSchema)
        {
            return recordSchema.Fields.ToDictionary(
                field => field.Name,
                field => NormalizeValue(record[field.Name]),
                StringComparer.Ordinal);
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal);
    }

    private static object? NormalizeValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is GenericRecord subRecord)
        {
            return ToDictionary(subRecord);
        }

        if (value is byte[] bytes)
        {
            return Convert.ToBase64String(bytes);
        }

        if (value is not string && value is IEnumerable list)
        {
            var resultList = new List<object?>();
            foreach (var item in list)
            {
                resultList.Add(NormalizeValue(item));
            }

            return resultList;
        }

        return value;
    }

    private async ValueTask<Schema> GetOrFetchSchemaAsync(string schemaId, CancellationToken ct)
    {
        if (_schemaCache.TryGetValue(schemaId, out var cachedSchema))
        {
            return cachedSchema;
        }

        if (_schemaFetcher is null)
        {
            throw new KeyNotFoundException($"Schema '{schemaId}' is not registered and no schema fetcher is configured.");
        }

        _logger.LogInformation("Schema {SchemaId} not found in cache. Fetching from Salesforce Pub/Sub API...", schemaId);
        var schemaJson = await _schemaFetcher(schemaId, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(schemaJson))
        {
            throw new InvalidOperationException($"Remote schema fetcher returned empty schema for '{schemaId}'.");
        }

        var schema = Schema.Parse(schemaJson);
        _schemaCache[schemaId] = schema;
        return schema;
    }
}
