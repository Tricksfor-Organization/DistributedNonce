# GitHub Copilot Instructions for DistributedNonce

These instructions guide Copilot to produce high-quality, idiomatic completions for this repository, and help contributors use the library correctly in scalable environments.

## Project overview

DistributedNonce is a .NET 9 library that implements a distributed, Redis-backed nonce service for blockchain transactions. It coordinates nonce access across processes/instances via a distributed lock so concurrent calls do not return duplicate nonces.

Key points
- Backend: Redis via StackExchange.Redis
- Locking: Tricksfor.DistributedLockManager (RunWithLockAsync)
- RPC: Nethereum IClient with EthGetTransactionCount
- Test stack: NUnit, NSubstitute, DotNet.Testcontainers

## Code organization

```
src/DistributedNonce/
├── Services/
│   └── DistributedNonceService.cs    # Core service, produces INonceService instances per account
└── Configuration.cs                  # DI extension AddDistributedNonce

tests/DistributedNonce.Tests/
└── DistributedNonceIntegrationTests.cs  # DI + Redis + NSubstitute tests
```

## How to register and use the library (DI)

Use the provided DI extension and register a singleton IConnectionMultiplexer. The lock manager must also be registered.

```csharp
using DistributedLockManager;            // AddDistributedLockManager()
using DistributedNonce;                  // AddDistributedNonce()
using DistributedNonce.Services;         // DistributedNonceService
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

var services = new ServiceCollection();

// 1) Register the lock manager (Redis-backed)
services.AddDistributedLockManager();

// 2) Register Redis connection multiplexer as singleton
var redis = await ConnectionMultiplexer.ConnectAsync("localhost:6379,abortConnect=false");
services.AddSingleton<IConnectionMultiplexer>(redis);

// 3) Register the nonce service
services.AddDistributedNonce();

var provider = services.BuildServiceProvider();
using var scope = provider.CreateScope();

var nonceFactory = scope.ServiceProvider.GetRequiredService<DistributedNonceService>();
// Provide your Nethereum IClient and account address
var client = /* resolve or construct Nethereum.JsonRpc.Client.IClient */;
var nonceService = nonceFactory.GetInstance("0xabc...", client, useLatestTransactionsOnly: false);

var next = await nonceService.GetNextNonceAsync();
```

Recommendations
- Keep IConnectionMultiplexer as a singleton for the app lifetime (do not scope/dispose it per request).
- Resolve DistributedNonceService as scoped or transient and create per-account INonceService instances as needed.
- Pass useLatestTransactionsOnly = true if you want to ignore pending transactions when computing the chain nonce.

## Using in a scalable environment (no duplicates across instances)

The nonce calculation is wrapped in a distributed lock keyed by account address. To scale horizontally (multiple instances/pods):
- Use a shared Redis instance/cluster all app instances can reach.
- Ensure the same address string is used to construct INonceService across instances; the lock key is derived from the address.
- Prefer a resilient Redis connection string (examples):
	- abortConnect=false
	- connectRetry=5
	- reconnectRetryPolicy=exponential
	- syncTimeout=10000

Example configuration snippet
```csharp
var options = ConfigurationOptions.Parse("redis:6379");
options.AbortOnConnectFail = false;
options.ConnectRetry = 5;
options.ReconnectRetryPolicy = new ExponentialRetry(5000);
var mux = await ConnectionMultiplexer.ConnectAsync(options);
services.AddSingleton<IConnectionMultiplexer>(mux);
```

Behavioral contract
- Input: account address (string), IClient (Nethereum), optional flag useLatestTransactionsOnly
- Output: HexBigInteger nonces that do not repeat for a given address, even under concurrent calls
- Error modes: will throw InvalidOperationException wrapping RPC/lock errors
- Concurrency: concurrent callers to GetNextNonceAsync serialize via the distributed lock to avoid duplicates

## Testing patterns and expectations

Unit tests (mock RPC)
- Mock eth_getTransactionCount by stubbing IClient.SendRequestAsync<HexBigInteger>(RpcRequest) to return a fixed value.
- Validate first call returns chain value; subsequent calls increment locally when the chain value doesn’t change.

Example mock with NSubstitute
```csharp
var client = Substitute.For<Nethereum.JsonRpc.Client.IClient>();
client
	.SendRequestAsync<HexBigInteger>(Arg.Any<RpcRequest>())
	.Returns(Task.FromResult(new HexBigInteger(new BigInteger(7))));
```

Concurrency test pattern
- Create a single INonceService for an address and issue many concurrent GetNextNonceAsync calls.
- Assert that all results are unique and form a contiguous range starting at the mocked chain value.

Integration tests (Redis + DI)
- Use DotNet.Testcontainers to provision redis:latest on a dynamic port.
- Register IConnectionMultiplexer as singleton, AddDistributedLockManager(), AddDistributedNonce().
- Keep the multiplexer and provider alive for the test fixture lifetime; dispose in OneTimeTearDown.

## Copilot guidance: do/don’t in this repo

Do
- Prefer NUnit for tests; use NSubstitute for mocks (not Moq).
- Use the existing DI extension AddDistributedNonce and AddDistributedLockManager.
- Use IConnectionMultiplexer as a singleton; do not dispose it prematurely.
- Mock IClient via SendRequestAsync<HexBigInteger>(RpcRequest) when stubbing EthGetTransactionCount.
- Write tests using Arrange/Act/Assert and keep assertions clear and minimal.

Don’t
- Don’t introduce new test frameworks or mocking libraries.
- Don’t hardcode shell commands into code; prefer code-first configuration.
- Don’t bypass the distributed lock when adding features that change nonce semantics.

## Common extensions Copilot may generate

- Additional helper to build INonceService per address
```csharp
public static INonceService CreateNonceService(
		this IServiceProvider sp, string address, IClient client, bool latestOnly = false)
		=> sp.GetRequiredService<DistributedNonceService>().GetInstance(address, client, latestOnly);
```

- Test helper to create a mock client returning a given chain nonce
```csharp
public static IClient CreateClientReturning(int value)
{
		var client = Substitute.For<IClient>();
		client.SendRequestAsync<HexBigInteger>(Arg.Any<RpcRequest>())
					.Returns(Task.FromResult(new HexBigInteger(new BigInteger(value))));
		return client;
}
```

## Acceptance checklist Copilot should satisfy
- Build compiles on .NET 9 with nullable enabled and implicit usings.
- New tests use NUnit + NSubstitute and pass locally.
- DI examples use AddDistributedNonce and a singleton IConnectionMultiplexer.
- Concurrency examples do not produce duplicate nonces.
