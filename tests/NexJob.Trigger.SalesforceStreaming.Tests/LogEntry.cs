using Microsoft.Extensions.Logging;

namespace NexJob.Trigger.SalesforceStreaming.Tests;

internal sealed record LogEntry(LogLevel Level, Exception? Exception, string Message);
