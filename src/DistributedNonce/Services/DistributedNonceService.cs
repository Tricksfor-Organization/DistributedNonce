using System.Numerics;
using DistributedLockManager.Interfaces;
using Nethereum.Hex.HexTypes;
using Nethereum.JsonRpc.Client;
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

    private sealed class CreateDistributedNonceServiceInstance(string accountAddress, IClient client, IDistributedLockService distributedLockService, bool useLatestTransactionsOnly = false) : INonceService
    {
        private readonly IDistributedLockService _distributedLockService = distributedLockService;
        private readonly string _address = accountAddress;
        public IClient Client { get; set; } = client;
        public BigInteger CurrentNonce { get; set; } = -1;
        public bool UseLatestTransactionsOnly { get; set; } = useLatestTransactionsOnly;

        public async Task<HexBigInteger> GetNextNonceAsync()
        {
            HexBigInteger nextNonce = new(BigInteger.Zero);

            EthGetTransactionCount ethGetTransactionCount = new(Client);

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
            }, $"{LockKeyPrefix}{_address}", CancellationToken.None);

            return nextNonce;
        }

        public async Task ResetNonceAsync()
        {
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
            }, $"{LockKeyPrefix}{_address}", CancellationToken.None);
        }
    }
}