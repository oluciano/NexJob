namespace NexJob.Trigger.Salesforce;

/// <summary>
/// File-based implementation of <see cref="IReplayIdStore"/> that persists binary Replay IDs to disk.
/// Employs atomic write operations to prevent file corruption during sudden process termination.
/// </summary>
public sealed class FileReplayIdStore : IReplayIdStore
{
    private static readonly char[] DisallowedChars = ['/', '\\', ':', '*', '?', '"', '<', '>', '|'];
    private readonly string _storageDirectory;
    private readonly object _syncLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="FileReplayIdStore"/> class.
    /// </summary>
    /// <param name="storageDirectory">Directory where replay ID token files are saved.</param>
    public FileReplayIdStore(string storageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageDirectory);
        _storageDirectory = storageDirectory;
        Directory.CreateDirectory(_storageDirectory);
    }

    /// <inheritdoc/>
    public async ValueTask<byte[]?> GetLastReplayIdAsync(string topic, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        var filePath = GetFilePathForTopic(topic);
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);
            return bytes.Length > 0 ? bytes : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public async ValueTask SaveReplayIdAsync(string topic, byte[] replayId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(replayId);

        var targetPath = GetFilePathForTopic(topic);
        var tempPath = Path.Combine(_storageDirectory, $"{SanitizeTopicName(topic)}_{Guid.NewGuid():N}.tmp");

        await File.WriteAllBytesAsync(tempPath, replayId, ct).ConfigureAwait(false);

        lock (_syncLock)
        {
            File.Move(tempPath, targetPath, overwrite: true);
        }
    }

    /// <summary>
    /// Generates a sanitized file path for the given Salesforce topic.
    /// </summary>
    /// <param name="topic">The Salesforce topic name.</param>
    /// <returns>The absolute file path.</returns>
    public string GetFilePathForTopic(string topic)
    {
        var sanitized = SanitizeTopicName(topic);
        return Path.Combine(_storageDirectory, $"{sanitized}.replay");
    }

    private static string SanitizeTopicName(string topic)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var chars = topic.TrimStart('/').ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (DisallowedChars.Contains(chars[i]) || invalidChars.Contains(chars[i]))
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }
}
