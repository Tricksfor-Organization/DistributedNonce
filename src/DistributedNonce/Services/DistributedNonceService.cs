using System.Numerics;
using DistributedLockManager.Interfaces;
using Nethereum.Hex.HexTypes;
using Nethereum.JsonRpc.Client;
using Nethereum.RPC.Eth;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.RPC.Eth.Transactions;
using Nethereum.RPC.NonceServices;

namespace DistributedNonce.Services;

public class DistributedNonceService(IDistributedLockService distributedLockService)
{
    private readonly IDistributedLockService _distributedLockService = distributedLockService;
    private const string LockKeyPrefix = "DistributedNonce_";

    public INonceService GetInstance(string address, IClient client, bool useLatestTransactionsOnly = false)
    {
        return new CreateDistributedNonceServiceInstance(address, client, _distributedLockService, useLatestTransactionsOnly);
    }

    private sealed class CreateDistributedNonceServiceInstance(string accountAddress, IClient client, IDistributedLockService distributedLockService, bool useLatestTransactionsOnly = false) : INonceService, IDisposable
    {
        private readonly IDistributedLockService _distributedLockService = distributedLockService;
        private readonly string _address = accountAddress;
        private readonly SemaphoreSlim _chainIdSemaphore = new(1, 1);
        private IClient _client = client;
        private BigInteger? _chainId = null;
        private int _disposed = 0;
        public IClient Client
        {
            get => _client;
            set
            {
                if (value is null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                if (!ReferenceEquals(_client, value))
                {
                    _chainIdSemaphore.Wait();
                    try
                    {
                        _client = value;
                        _chainId = null;
                    }
                    finally
                    {
                        _chainIdSemaphore.Release();
                    }
                }
            }
        }
        public BigInteger CurrentNonce { get; set; } = -1;
        public bool UseLatestTransactionsOnly { get; set; } = useLatestTransactionsOnly;

        private async Task<BigInteger> GetChainIdAsync()
        {
            if (_chainId.HasValue)
            {
                return _chainId.Value;
            }

            await _chainIdSemaphore.WaitAsync().ConfigureAwait(continueOnCapturedContext: false);
            try
            {
                if (_chainId.HasValue)
                {
                    return _chainId.Value;
                }

                var ethChainId = new EthChainId(Client);
                var chainIdHex = await ethChainId.SendRequestAsync().ConfigureAwait(continueOnCapturedContext: false);
                _chainId = chainIdHex.Value;
                return _chainId.Value;
            }
            finally
            {
                _chainIdSemaphore.Release();
            }
        }

        public async Task<HexBigInteger> GetNextNonceAsync()
        {
            HexBigInteger nextNonce = new(BigInteger.Zero);

            EthGetTransactionCount ethGetTransactionCount = new(Client);

            BigInteger chainId;
            try
            {
                chainId = await GetChainIdAsync().ConfigureAwait(continueOnCapturedContext: false);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"An error occurred during get next nonce for account: {_address}", exception);
            }

            await _distributedLockService.RunWithLockAsync(func: async () =>
            {
                try
                {
                    BlockParameter block = BlockParameter.CreatePending();
                    if (UseLatestTransactionsOnly)
                    {
                        block = BlockParameter.CreateLatest();
                    }

                    HexBigInteger hexBigInteger =
                        await ethGetTransactionCount.SendRequestAsync(_address, block).
                            ConfigureAwait(continueOnCapturedContext: false);
                    if (hexBigInteger.Value <= CurrentNonce)
                    {
                        CurrentNonce += (BigInteger)1;
                        hexBigInteger = new HexBigInteger(CurrentNonce);
                    }
                    else
                    {
                        CurrentNonce = hexBigInteger.Value;
                    }

                    nextNonce = hexBigInteger;
                }
                catch(Exception exception)
                {
                    throw new InvalidOperationException($"An error occurred during get next nonce for account: {_address}, {exception.Message}");
                }
            }, $"{LockKeyPrefix}{chainId}_{_address}", CancellationToken.None);

            return nextNonce;
        }

        public async Task ResetNonceAsync()
        {
            BigInteger chainId;
            try
            {
                chainId = await GetChainIdAsync().ConfigureAwait(continueOnCapturedContext: false);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"An error occurred during reset nonce for account: {_address}", exception);
            }

            await _distributedLockService.RunWithLockAsync(func: async () =>
            {
                try
                {
                    CurrentNonce = -1;
                    await Task.CompletedTask;
                }
                catch (Exception)
                {
                    throw new InvalidOperationException($"An error occurred during reset nonce for account: {_address}.");
                }
            }, $"{LockKeyPrefix}{chainId}_{_address}", CancellationToken.None);
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
            {
                _chainIdSemaphore.Dispose();
            }
        }
    }
}