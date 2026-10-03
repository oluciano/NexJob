using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexJob.Internal;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// The Slack alert recipe of the Alerts guide (docs/wiki/guides/alerts.md) works as written: the forwarder only queues the
/// message, a background sender posts it, and a webhook that is slow or failing never reaches the worker (issue #205).
/// </summary>
public sealed class AlertRecipeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ExhaustedJob_IsPostedToTheWebhookOnce_WithTypeQueueAndError()
    {
        // N1 (Positive)
        var webhook = new FakeWebhook();
        using var host = await StartAsync(webhook);

        await DispatcherFor(host).DispatchAsync(Job(queue: "payments"), new InvalidOperationException("card declined"));

        await WaitAsync(() => webhook.Posts.Count == 1);
        var body = TextOf(webhook.Posts.Single());
        body.Should().Contain("AlertRecipeTests+PaymentJob").And.Contain("payments").And.Contain("card declined");
        body.Should().NotContain("secret-input", "the job input is never sent");
    }

    [Fact]
    public async Task SlowWebhook_DoesNotBlockTheWorker()
    {
        // N2 (Negative): the dispatcher awaits the forwarder inside the worker slot, so it must return at once.
        var webhook = new FakeWebhook { Delay = TimeSpan.FromSeconds(30) };
        using var host = await StartAsync(webhook);

        var dispatch = DispatcherFor(host).DispatchAsync(Job(), new InvalidOperationException("boom"));

        await FluentActions.Awaiting(() => dispatch.WaitAsync(TimeSpan.FromSeconds(2))).Should().NotThrowAsync("the forwarder only queues the message");
    }

    [Fact]
    public async Task FailingWebhook_IsSurvived_AndTheNextAlertStillGoesOut()
    {
        // N2 (Negative)
        var webhook = new FakeWebhook { FailFirst = 1 };
        using var host = await StartAsync(webhook);
        var dispatcher = DispatcherFor(host);

        await dispatcher.DispatchAsync(Job(), new InvalidOperationException("first"));
        await WaitAsync(() => webhook.Attempts >= 1);
        await dispatcher.DispatchAsync(Job(), new InvalidOperationException("second"));

        await WaitAsync(() => webhook.Posts.Count == 1);
        TextOf(webhook.Posts.Single()).Should().Contain("second");
    }

    [Fact]
    public async Task OutageOfManyJobs_FillsTheQueue_ThenDropsWithoutThrowing()
    {
        // N3 (Boundary): a burst bigger than the queue is dropped, never throws and never grows without limit.
        var webhook = new FakeWebhook { Delay = TimeSpan.FromSeconds(30) };
        using var host = await StartAsync(webhook);
        var dispatcher = DispatcherFor(host);

        var burst = Enumerable.Range(0, 500).Select(_ => dispatcher.DispatchAsync(Job(), new InvalidOperationException("x")));

        await FluentActions.Awaiting(() => Task.WhenAll(burst).WaitAsync(TimeSpan.FromSeconds(5))).Should().NotThrowAsync();
    }

    [Fact]
    public async Task VeryLongErrorMessage_IsTruncated()
    {
        // N3 (Invalid input)
        var webhook = new FakeWebhook();
        using var host = await StartAsync(webhook);

        await DispatcherFor(host).DispatchAsync(Job(), new InvalidOperationException(new string('x', 10_000)));

        await WaitAsync(() => webhook.Posts.Count == 1);
        TextOf(webhook.Posts.Single()).Length.Should().BeLessThan(1_000);
    }

    private static string TextOf(string body) => System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("text").GetString()!;

    private static async Task<IHost> StartAsync(FakeWebhook webhook)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(l => l.ClearProviders())
            .ConfigureServices(services =>
            {
                services.AddSingleton<SlackAlertQueue>();
                services.AddSingleton<IDeadLetterForwarder, SlackAlertForwarder>();
                services.AddHostedService(sp => new SlackAlertSender(
                    sp.GetRequiredService<SlackAlertQueue>(),
                    new HttpClient(webhook) { Timeout = TimeSpan.FromSeconds(5) },
                    "https://hooks.slack.test/services/T00/B00/XXXX",
                    NullLogger<SlackAlertSender>.Instance));
            })
            .Build();
        await host.StartAsync();
        return host;
    }

    private static DefaultDeadLetterDispatcher DispatcherFor(IHost host) =>
        new(host.Services.GetRequiredService<IServiceScopeFactory>(), Mock.Of<ILogger<DefaultDeadLetterDispatcher>>());

    private static JobRecord Job(string queue = "default") => new()
    {
        Id = JobId.New(),
        JobType = typeof(PaymentJob).AssemblyQualifiedName!,
        InputType = typeof(string).AssemblyQualifiedName!,
        InputJson = "\"secret-input\"",
        Queue = queue,
        Attempts = 3,
        MaxAttempts = 3,
        CreatedAt = DateTimeOffset.UtcNow,
        Status = JobStatus.Failed,
    };

    private static async Task WaitAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition was not reached in time.");
            }

            await Task.Delay(25);
        }
    }

    private sealed class PaymentJob : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeWebhook : HttpMessageHandler
    {
        private int _attempts;

        public List<string> Posts { get; } = [];

        public TimeSpan Delay { get; init; }

        public int FailFirst { get; init; }

        public int Attempts => Volatile.Read(ref _attempts);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var attempt = Interlocked.Increment(ref _attempts);
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            if (attempt <= FailFirst)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            lock (Posts)
            {
                Posts.Add(body);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    // ── The recipe, as it appears in the guide ────────────────────────────────────────────────────────────────

    public sealed class SlackAlertQueue
    {
        public Channel<string> Messages { get; } = Channel.CreateBounded<string>(
            new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.DropWrite });
    }

    public sealed class SlackAlertForwarder : IDeadLetterForwarder
    {
        private readonly SlackAlertQueue _queue;

        public SlackAlertForwarder(SlackAlertQueue queue) => _queue = queue;

        public bool AppliesTo(JobRecord failedJob) => true;

        public Task ForwardAsync(JobRecord failedJob, Exception lastException, CancellationToken cancellationToken)
        {
            var jobType = failedJob.JobType.Split(',')[0];
            var error = lastException.Message.Length > 300 ? lastException.Message[..300] + "..." : lastException.Message;

            // TryWrite never waits: when the queue is full the alert is dropped, the worker is not held.
            _queue.Messages.Writer.TryWrite(
                $":red_circle: *{jobType}* failed for good on queue `{failedJob.Queue}` after {failedJob.Attempts} attempts\n" +
                $"Job `{failedJob.Id.Value}`: {error}");

            return Task.CompletedTask;
        }
    }

    public sealed class SlackAlertSender : BackgroundService
    {
        private readonly SlackAlertQueue _queue;
        private readonly HttpClient _http;
        private readonly string _webhookUrl;
        private readonly ILogger<SlackAlertSender> _logger;

        public SlackAlertSender(SlackAlertQueue queue, HttpClient http, string webhookUrl, ILogger<SlackAlertSender> logger)
        {
            _queue = queue;
            _http = http;
            _webhookUrl = webhookUrl;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var text in _queue.Messages.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var response = await _http.PostAsJsonAsync(_webhookUrl, new { text }, stoppingToken);
                    response.EnsureSuccessStatusCode();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Log it and move on: an alert that cannot be delivered must not stop the next ones.
                    _logger.LogWarning(ex, "The Slack alert could not be delivered");
                }
            }
        }
    }
}
