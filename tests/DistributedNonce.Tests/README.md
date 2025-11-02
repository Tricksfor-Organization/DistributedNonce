# DistributedNonce.Tests

Integration and unit-style tests for the DistributedNonce library. The suite validates DI wiring, Redis-backed distributed locking, nonce sequencing, and concurrency safety.

## What these tests cover

- DI and lifetime
  - Registers DistributedLockManager, singleton IConnectionMultiplexer, and AddDistributedNonce
  - Resolves DistributedNonceService from an IServiceScope
- RPC behavior (mocked with NSubstitute)
  - First GetNextNonceAsync returns the chain nonce from EthGetTransactionCount
  - Subsequent calls increment locally when the chain nonce doesn’t advance
- Concurrency
  - Many concurrent GetNextNonceAsync calls on a single INonceService produce unique, contiguous nonces
- Redis integration
  - A redis:latest container is started with DotNet.Testcontainers for a real Redis backend

## Prerequisites

- .NET 9 SDK
- Docker engine running (required by Testcontainers to start Redis)

## How to run

- From the repository root, run the test suite:

  ```bash
  dotnet test -v minimal
  ```

  Notes:
  - Ensure Docker is running; the fixture starts a temporary Redis container automatically.
  - The tests keep the Redis multiplexer and service provider alive for the fixture lifetime and dispose them in OneTimeTearDown.

## Key libraries used

- NUnit — test framework
- NSubstitute — mocking (for Nethereum IClient)
- DotNet.Testcontainers — ephemeral Redis for integration testing

## Mocking pattern (NSubstitute)

Mock EthGetTransactionCount by stubbing the underlying IClient.SendRequestAsync for RpcRequest:

```csharp
using NSubstitute;
using Nethereum.JsonRpc.Client;
using Nethereum.Hex.HexTypes;
using System.Numerics;

var client = Substitute.For<IClient>();
client
  .SendRequestAsync<HexBigInteger>(Arg.Any<RpcRequest>())
  .Returns(Task.FromResult(new HexBigInteger(new BigInteger(7))));
```

## Concurrency expectation

Calling GetNextNonceAsync concurrently against the same INonceService instance must:
- Never return duplicates
- Produce a contiguous increasing range starting at the chain nonce (when the RPC keeps returning the same count)

The test suite asserts both uniqueness and contiguity (e.g., starting at 7: 7, 8, 9, …).

## DI and lifetime notes

- IConnectionMultiplexer must be a singleton and should not be disposed per request
- DistributedNonceService is resolved from a scope; create one INonceService per account
- The Redis container and DI scope are created once per fixture (OneTimeSetUp) and disposed in OneTimeTearDown

## Troubleshooting

- RedisConnectionException: It was not possible to connect to the redis server(s)
  - Ensure Docker is running and reachable
  - Avoid premature disposal of the IConnectionMultiplexer
  - Consider resilient options such as AbortOnConnectFail=false (already used in examples)
- Port conflicts
  - Testcontainers maps Redis to a random host port; no static port binding should be required

## Conventions for adding new tests

- Use NUnit + NSubstitute (avoid introducing other frameworks like Moq)
- Follow Arrange / Act / Assert
- Prefer clear, minimal assertions
- When testing concurrency, run multiple tasks against a single INonceService for the same address and verify uniqueness + contiguity
