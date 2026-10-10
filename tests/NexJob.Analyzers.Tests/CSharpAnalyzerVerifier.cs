using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;

namespace NexJob.Analyzers.Tests;

internal static class CSharpAnalyzerVerifier<TAnalyzer>
    where TAnalyzer : Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer, new()
{
    private const string NexJobSources = @"
namespace NexJob
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    public interface IJob
    {
        Task ExecuteAsync(CancellationToken cancellationToken);
    }

    public interface IJob<in TInput>
    {
        Task ExecuteAsync(TInput input, CancellationToken cancellationToken);
    }

    public sealed class JobRecord
    {
    }

    public enum DuplicatePolicy
    {
        AllowAfterFailed,
        ThrowOnDuplicate
    }

    public interface IScheduler
    {
        Task EnqueueAsync(
            JobRecord job,
            DuplicatePolicy duplicatePolicy = DuplicatePolicy.AllowAfterFailed,
            CancellationToken cancellationToken = default);

        Task EnqueueAsync<TJob>(
            string? queue = null,
            string? idempotencyKey = null,
            TimeSpan? deadlineAfter = null,
            CancellationToken cancellationToken = default) where TJob : IJob;

        Task EnqueueAsync<TJob, TInput>(
            TInput input,
            string? queue = null,
            string? idempotencyKey = null,
            TimeSpan? deadlineAfter = null,
            CancellationToken cancellationToken = default) where TJob : IJob<TInput>;

        Task EnqueueAsync<TJob>(
            int maxAttempts,
            string? queue = null,
            string? idempotencyKey = null,
            TimeSpan? deadlineAfter = null,
            CancellationToken cancellationToken = default) where TJob : IJob;

        Task EnqueueAsync<TJob, TInput>(
            TInput input,
            int maxAttempts,
            string? queue = null,
            string? idempotencyKey = null,
            TimeSpan? deadlineAfter = null,
            CancellationToken cancellationToken = default) where TJob : IJob<TInput>;
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class RetryAttribute : Attribute
    {
        public RetryAttribute(int attempts) => Attempts = attempts;
        public int Attempts { get; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class ThrottleAttribute : Attribute
    {
        public ThrottleAttribute(string resource, int maxConcurrent)
        {
            Resource = resource;
            MaxConcurrent = maxConcurrent;
        }

        public string Resource { get; }
        public int MaxConcurrent { get; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class ExecutionTimeoutAttribute : Attribute
    {
        public ExecutionTimeoutAttribute(string timeout) => Timeout = timeout;
        public string Timeout { get; }
    }
}
";

    public static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, XUnitVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        test.TestState.Sources.Add(("NexJobStubs.cs", NexJobSources));
        test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", @"
root = true
[*.cs]
dotnet_diagnostic.NXJ011.severity = info
dotnet_diagnostic.NXJ016.severity = info
dotnet_diagnostic.NXJ017.severity = info
dotnet_diagnostic.NXJ018.severity = info
dotnet_diagnostic.NXJ019.severity = info
"));
        test.ExpectedDiagnostics.AddRange(expected);

        await test.RunAsync();
    }
}
