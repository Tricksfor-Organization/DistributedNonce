# DistributedNonce

Redis-backed, distributed nonce service for Ethereum-like blockchains. It coordinates nonce access across processes/instances using a distributed lock so concurrent calls never return duplicate nonces.

## Features

- Distributed, Redis-backed locking (via Tricksfor.DistributedLockManager)
- Safe concurrent GetNextNonceAsync calls without duplicates
- Pluggable Nethereum RPC client (IClient)
- DI-friendly with AddDistributedNonce extension
- Supports pending or latest block modes

## Requirements

- .NET 9 (nullable enabled, implicit usings)
- Redis server/cluster reachable by your app
- Packages (transitive when you install DistributedNonce):
	- StackExchange.Redis
	- Tricksfor.DistributedLockManager
	- Nethereum (IClient, HexBigInteger, etc.)

## Install

Add the DistributedNonce package to your project (NuGet). This package brings the required dependencies transitively. Then register the services as shown below.

## Quick start (DI + usage)

Register the lock manager, a singleton IConnectionMultiplexer, and the nonce service. Resolve DistributedNonceService and create per-account INonceService instances.

```csharp
using DistributedLockManager;            // AddDistributedLockManager()
using DistributedNonce;                  // AddDistributedNonce()
using DistributedNonce.Services;         // DistributedNonceService
using Microsoft.Extensions.DependencyInjection;
using Nethereum.JsonRpc.Client;          // IClient
using Nethereum.Hex.HexTypes;            // HexBigInteger
using StackExchange.Redis;               // ConnectionMultiplexer

var services = new ServiceCollection();

// 1) Distributed lock manager (Redis-backed)
services.AddDistributedLockManager();

// 2) Redis connection (keep as singleton for app lifetime)
var redis = await ConnectionMultiplexer.ConnectAsync("localhost:6379,abortConnect=false");
services.AddSingleton<IConnectionMultiplexer>(redis);

// 3) Nonce service
services.AddDistributedNonce();

var provider = services.BuildServiceProvider();
using var scope = provider.CreateScope();

var nonceFactory = scope.ServiceProvider.GetRequiredService<DistributedNonceService>();

// Provide your Nethereum IClient (e.g., JsonRpcClient or from Web3)
IClient client = /* construct/resolve RPC client */;

// Create an INonceService instance per account address
var nonceService = nonceFactory.GetInstance("0xabc...", client, useLatestTransactionsOnly: false);

// Fetch the next nonce safely (no duplicates under concurrency)
HexBigInteger next = await nonceService.GetNextNonceAsync();
```

## Scalable environments (multiple instances/pods)

The nonce calculation is performed inside a distributed lock keyed by the account address, ensuring uniqueness across instances:

- Use a shared Redis instance/cluster accessible by all app instances
- Ensure identical address strings are used to construct INonceService across instances (lock key derives from address)
- Prefer resilient Redis settings:
	- abortConnect=false
	- connectRetry=5
	- reconnectRetryPolicy=exponential
	- syncTimeout=10000

Example configuration:

```csharp
var options = ConfigurationOptions.Parse("redis:6379");
options.AbortOnConnectFail = false;
options.ConnectRetry = 5;
options.ReconnectRetryPolicy = new ExponentialRetry(5000);
var mux = await ConnectionMultiplexer.ConnectAsync(options);
services.AddSingleton<IConnectionMultiplexer>(mux);
```

## API overview

- DistributedNonceService
	- GetInstance(string address, IClient client, bool useLatestTransactionsOnly = false) → INonceService

- INonceService (Nethereum.RPC.NonceServices)
	- Task<HexBigInteger> GetNextNonceAsync()
	- Task ResetNonceAsync()

Behavior
- First call returns the chain’s transaction count (EthGetTransactionCount) for the address
- Subsequent calls increment locally if the chain value hasn’t advanced
- When useLatestTransactionsOnly = true, ignores pending transactions
- Exceptions are wrapped in InvalidOperationException if RPC/lock errors occur

## Concurrency example

```csharp
var nonceService = nonceFactory.GetInstance("0xabc...", client);
var calls = Enumerable.Range(0, 20).Select(_ => nonceService.GetNextNonceAsync());
var results = await Task.WhenAll(calls);

// assert uniqueness and sequentiality
var values = results.Select(r => r.Value).ToArray();
if (values.Distinct().Count() != values.Length) throw new Exception("Duplicates detected");
```

## Testing patterns

- Unit testing with NSubstitute
	- Mock EthGetTransactionCount by stubbing:
		```csharp
		var client = Substitute.For<Nethereum.JsonRpc.Client.IClient>();
		client.SendRequestAsync<HexBigInteger>(Arg.Any<RpcRequest>())
					.Returns(Task.FromResult(new HexBigInteger(new BigInteger(7))));
		```
	- Validate first call returns 7; subsequent calls increment (8, 9, …) when RPC keeps returning 7

- Integration testing with DotNet.Testcontainers
	- Run redis:latest on a dynamic port
	- Register lock manager, singleton IConnectionMultiplexer, and AddDistributedNonce
	- Keep the multiplexer and provider alive for the fixture lifetime; dispose in OneTimeTearDown

## Best practices

- Register IConnectionMultiplexer as a singleton; don’t dispose it per request
- Resolve DistributedNonceService as scoped/transient; create one INonceService per account address
- Use the exact same address string across instances to align on the same lock key
- Handle transient RPC/Redis errors with retries at the application level if needed

## License

MIT. See LICENSE for details.
