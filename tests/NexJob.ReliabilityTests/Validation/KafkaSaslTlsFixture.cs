using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using NexJob.Tests;
using Xunit;

namespace NexJob.ReliabilityTests.Validation;

/// <summary>
/// One Kafka broker (KRaft) whose external listener is SASL_SSL with the PLAIN mechanism, over a certificate signed by a
/// CA generated for the test run.
/// </summary>
public sealed class KafkaSaslTlsFixture : IAsyncLifetime
{
    /// <summary>The user the broker accepts.</summary>
    public const string User = "alice";

    /// <summary>The password of <see cref="User"/>.</summary>
    public const string Password = "alice-secret";

    private const string StorePassword = "changeit";
    private readonly int _hostPort = TestPorts.Next();
    private readonly IContainer _broker;

    /// <summary>Initializes a new instance of the <see cref="KafkaSaslTlsFixture"/> class.</summary>
    public KafkaSaslTlsFixture()
    {
        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = DateTimeOffset.UtcNow.AddDays(1);
        using var caKey = RSA.Create(2048);
        var caRequest = new CertificateRequest("CN=NexJob test CA", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var ca = caRequest.CreateSelfSigned(notBefore, notAfter);
        CaPem = ca.ExportCertificatePem();

        using var serverKey = RSA.Create(2048);
        var serverRequest = new CertificateRequest("CN=localhost", serverKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        serverRequest.CertificateExtensions.Add(names.Build());
        serverRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        using var signed = serverRequest.Create(ca, notBefore, notAfter, RandomNumberGenerator.GetBytes(8));
        using var withKey = signed.CopyWithPrivateKey(serverKey);
        var keystore = withKey.Export(X509ContentType.Pfx, StorePassword);

        _broker = new ContainerBuilder()
            .WithImage("confluentinc/cp-kafka:7.6.0")
            .WithPortBinding(_hostPort, 9094)
            .WithResourceMapping(keystore, "/etc/kafka/secrets/keystore.p12")
            .WithResourceMapping(Encoding.ASCII.GetBytes(StorePassword), "/etc/kafka/secrets/creds")
            .WithEnvironment("CLUSTER_ID", "MkU3OEVBNTcwNTJENDM2Qk")
            .WithEnvironment("KAFKA_NODE_ID", "1")
            .WithEnvironment("KAFKA_PROCESS_ROLES", "broker,controller")
            .WithEnvironment("KAFKA_CONTROLLER_QUORUM_VOTERS", "1@localhost:9093")
            .WithEnvironment("KAFKA_LISTENERS", "INTERNAL://:9092,CONTROLLER://:9093,EXTERNAL://:9094")
            .WithEnvironment("KAFKA_ADVERTISED_LISTENERS", $"INTERNAL://localhost:9092,EXTERNAL://localhost:{_hostPort}")
            .WithEnvironment("KAFKA_LISTENER_SECURITY_PROTOCOL_MAP", "CONTROLLER:PLAINTEXT,INTERNAL:PLAINTEXT,EXTERNAL:SASL_SSL")
            .WithEnvironment("KAFKA_INTER_BROKER_LISTENER_NAME", "INTERNAL")
            .WithEnvironment("KAFKA_CONTROLLER_LISTENER_NAMES", "CONTROLLER")
            .WithEnvironment("KAFKA_SASL_ENABLED_MECHANISMS", "PLAIN")
            .WithEnvironment("KAFKA_LISTENER_NAME_EXTERNAL_PLAIN_SASL_JAAS_CONFIG", $"org.apache.kafka.common.security.plain.PlainLoginModule required user_{User}=\"{Password}\";")
            .WithEnvironment("KAFKA_SSL_KEYSTORE_FILENAME", "keystore.p12")
            .WithEnvironment("KAFKA_SSL_KEYSTORE_CREDENTIALS", "creds")
            .WithEnvironment("KAFKA_SSL_KEY_CREDENTIALS", "creds")
            .WithEnvironment("KAFKA_SSL_KEYSTORE_TYPE", "PKCS12")
            .WithEnvironment("KAFKA_SSL_KEYSTORE_LOCATION", "/etc/kafka/secrets/keystore.p12")
            .WithEnvironment("KAFKA_SSL_KEYSTORE_PASSWORD", StorePassword)
            .WithEnvironment("KAFKA_SSL_KEY_PASSWORD", StorePassword)
            .WithEnvironment("KAFKA_SSL_CLIENT_AUTH", "none")
            .WithEnvironment("KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR", "1")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR", "1")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_MIN_ISR", "1")
            .WithEnvironment("KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS", "0")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Kafka Server started"))
            .Build();
    }

    /// <summary>Gets the PEM of the CA that signed the broker's certificate.</summary>
    public string CaPem { get; }

    /// <summary>Gets the address of the SASL_SSL listener.</summary>
    public string BootstrapServers => $"localhost:{_hostPort}";

    /// <inheritdoc/>
    public Task InitializeAsync() => _broker.StartAsync();

    /// <inheritdoc/>
    public Task DisposeAsync() => _broker.DisposeAsync().AsTask();
}
