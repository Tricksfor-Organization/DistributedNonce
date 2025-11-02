# DistributedNonce

Redis-backed, distributed nonce service for Ethereum-like blockchains. Ensures concurrent calls across processes/instances never return duplicate nonces.

## Features

- Distributed locking via Redis (Tricksfor.DistributedLockManager)
- Safe concurrent `GetNextNonceAsync` with no duplicates
- Nethereum integration (IClient)
- DI-friendly with `AddDistributedNonce` extension

## Quick Start

```csharp
using DistributedLockManager;
using DistributedNonce;
using DistributedNonce.Services;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

var services = new ServiceCollection();

// Register lock manager, Redis, and nonce service
services.AddDistributedLockManager();
services.AddSingleton<IConnectionMultiplexer>(
    await ConnectionMultiplexer.ConnectAsync("localhost:6379,abortConnect=false"));
services.AddDistributedNonce();

var provider = services.BuildServiceProvider();
var nonceFactory = provider.GetRequiredService<DistributedNonceService>();

// Create per-account nonce service
var nonceService = nonceFactory.GetInstance("0xYourAddress", yourRpcClient);
var next = await nonceService.GetNextNonceAsync();
```

## Requirements

- .NET 9+
- Redis server/cluster

## Scalable Environments

Use a shared Redis instance across all app instances. The distributed lock is keyed by account address, ensuring nonce uniqueness even under high concurrency.

For more details, see the [GitHub repository](https://github.com/Tricksfor-Organization/DistributedNonce).
