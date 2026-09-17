using System.Globalization;

namespace NexJob.Trigger.SalesforceStreaming;

/// <summary>
/// File-based implementation of <see cref="IStreamingReplayIdStore"/> that persists numeric Replay IDs to disk.
/// Employs atomic write operations to prevent file corruption during unexpected process termination.
/// </summary>
public sealed class FileStreamingReplayIdStore : IStreamingReplayIdStore
{
    private static readonly char[] DisallowedChars = ['/', '\\', ':', '*', '?', '"', '<', '>', '|'];
    private readonly string _storageDirectory;
    private readonly object _syncLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="FileStreamingReplayIdStore"/> class.
    /// </summary>
    /// <param name="storageDirectory">Directory where Replay ID files are saved.</param>
    public FileStreamingReplayIdStore(string storageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageDirectory);
        _storageDirectory = storageDirectory;
        Directory.CreateDirectory(_storageDirectory);
    }

    /// <inheritdoc/>
    public async Task<long?> GetReplayIdAsync(string channel, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);

        var filePath = GetFilePathForChannel(channel);
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var text = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            return long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                ? id
                : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task SaveReplayIdAsync(string channel, long replayId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);

        var targetPath = GetFilePathForChannel(channel);
        var tempPath = Path.Combine(_storageDirectory, $"{SanitizeChannelName(channel)}_{Guid.NewGuid():N}.tmp");

        await File.WriteAllTextAsync(tempPath, replayId.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);

        lock (_syncLock)
        {
            File.Move(tempPath, targetPath, overwrite: true);
        }
    }

    /// <summary>
    /// Generates a sanitized file path for the given Salesforce streaming channel.
    /// </summary>
    /// <param name="channel">The channel name.</param>
    /// <returns>The absolute file path.</returns>
    public string GetFilePathForChannel(string channel)
    {
        var sanitized = SanitizeChannelName(channel);
        return Path.Combine(_storageDirectory, $"{sanitized}.replay");
    }

    private static string SanitizeChannelName(string channel)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var chars = channel.TrimStart('/').ToCharArray();
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
