using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using NUnit.Framework;
using DistributedLockManager;
using DistributedNonce.Services;
using Nethereum.JsonRpc.Client;
using Nethereum.Hex.HexTypes;
using System.Numerics;
using NSubstitute;

namespace DistributedNonce.Tests;

[TestFixture]
public class DistributedNonceIntegrationTests
{
    private IServiceScope? scope;
    private TestcontainersContainer? _redisContainer;
    private const string RedisImage = "redis:latest";
    private const int RedisPort = 6379;
    private IConnectionMultiplexer? _mux;
    private ServiceProvider? _provider;
    private IClient _clientReturns5 = default!;
    private IClient _clientReturns7 = default!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _redisContainer = new TestcontainersBuilder<TestcontainersContainer>()
            .WithImage(RedisImage)
            .WithCleanUp(true)
            .WithName($"dtm-redis-{Guid.NewGuid():N}")
            .WithPortBinding(RedisPort, true)
            .Build();

        await _redisContainer.StartAsync();

        if (_redisContainer == null) Assert.Fail("Redis container not started");
        var container = _redisContainer!;
        var host = container.Hostname;
        var port = container.GetMappedPublicPort(RedisPort);
        var endpoint = $"{host}:{port}";

        // create the connection multiplexer and register it in DI (keep it alive for all tests)
        _mux = await ConnectionMultiplexer.ConnectAsync(endpoint);

        var services = new ServiceCollection();
        services.AddDistributedLockManager();
        services.AddDistributedNonce();
        services.AddSingleton<IConnectionMultiplexer>(_mux);

        _provider = services.BuildServiceProvider();
        scope = _provider.CreateScope();

        // Prepare reusable RPC client mocks for tests
        _clientReturns5 = Substitute.For<IClient>();
        _clientReturns5
            .SendRequestAsync<HexBigInteger>(Arg.Any<RpcRequest>())
            .Returns(Task.FromResult(new HexBigInteger(new BigInteger(5))));

        _clientReturns7 = Substitute.For<IClient>();
        _clientReturns7
            .SendRequestAsync<HexBigInteger>(Arg.Any<RpcRequest>())
            .Returns(Task.FromResult(new HexBigInteger(new BigInteger(7))));
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (_redisContainer is not null)
            await _redisContainer.StopAsync();

        scope?.Dispose();
        if (_provider is not null)
            await _provider.DisposeAsync();
        _mux?.Dispose();
    }

    [Test, Order(1)]
    public void Resolve_DistributedNonceService_FromScope_Succeeds()
    {
        if (scope is null) Assert.Fail("Service scope not initialized");
        var sp = scope!.ServiceProvider;
        var service = sp.GetService<DistributedNonceService>();

        Assert.That(service, Is.Not.Null, "DistributedNonceService should be registered and resolvable");
    }

    [Test, Order(2)]
    public async Task GetInstance_Returns_INonceService_And_Reset_Works()
    {
        if (scope is null) Assert.Fail("Service scope not initialized");
        var sp = scope!.ServiceProvider;
        var service = sp.GetRequiredService<DistributedNonceService>();

        // use a shared dummy Nethereum client; ResetNonceAsync doesn't call RPC
        var client = _clientReturns5;

        var nonceService = service.GetInstance("0x0000000000000000000000000000000000000001", client);
        Assert.That(nonceService, Is.Not.Null);

        // Should not throw; uses distributed lock + redis
        await nonceService.ResetNonceAsync();
        Assert.Pass();
    }

    [Test, Order(3)]
    public async Task GetNextNonceAsync_Returns_ChainNonce_OnFirstCall()
    {
        if (scope is null) Assert.Fail("Service scope not initialized");
        var sp = scope!.ServiceProvider;
        var service = sp.GetRequiredService<DistributedNonceService>();

        var client = _clientReturns5;

        var nonceService = service.GetInstance("0x0000000000000000000000000000000000000001", client);

        var next = await nonceService.GetNextNonceAsync();

        Assert.That(next.Value, Is.EqualTo(new BigInteger(5)));
    }

    [Test, Order(4)]
    public async Task GetNextNonceAsync_Increments_When_ChainNonce_DoesNotIncrease()
    {
        if (scope is null) Assert.Fail("Service scope not initialized");
        var sp = scope!.ServiceProvider;
        var service = sp.GetRequiredService<DistributedNonceService>();

        var client = _clientReturns7;

        var nonceService = service.GetInstance("0x0000000000000000000000000000000000000002", client);

        var first = await nonceService.GetNextNonceAsync();
        var second = await nonceService.GetNextNonceAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first.Value, Is.EqualTo(new BigInteger(7)));
            // since the RPC keeps returning 7, the service should locally increment to 8
            Assert.That(second.Value, Is.EqualTo(new BigInteger(8)));
        });
    }

    [Test, Order(5)]
    public async Task GetNextNonceAsync_Concurrent_Calls_Produce_Unique_Sequential_Values()
    {
        if (scope is null) Assert.Fail("Service scope not initialized");
        var sp = scope!.ServiceProvider;
        var service = sp.GetRequiredService<DistributedNonceService>();

        var client = _clientReturns7;
        var address = "0x0000000000000000000000000000000000000003";
        var nonceService = service.GetInstance(address, client);

        int count = 20;
        var tasks = Enumerable.Range(0, count)
            .Select(_ => nonceService.GetNextNonceAsync())
            .ToArray();

        var results = await Task.WhenAll(tasks);
        var values = results.Select(x => x.Value).ToArray();

        // All values must be unique
        Assert.That(values.Distinct().Count(), Is.EqualTo(count));

        // They should form a contiguous range starting from the chain value (7)
        var expected = Enumerable.Range(7, count).Select(i => new BigInteger(i));
        CollectionAssert.AreEquivalent(expected, values);
    }
}
