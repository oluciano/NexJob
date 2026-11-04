using Microsoft.Extensions.Logging;

namespace NexJob.Trigger.Salesforce.Tests;

internal sealed record LogEntry(LogLevel Level, Exception? Exception, string Message);
