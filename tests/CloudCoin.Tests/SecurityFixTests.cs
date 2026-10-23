using System;
using System.Collections.Generic;
using CloudCoin;
using EllipticCurve;
using Xunit;

namespace CloudCoin.Tests
{
    public class SecurityFixTests
    {
        const int Difficulty = 2;
        const decimal Reward = 100;

        // Builds a valid chain: genesis, a block rewarding wallet 1, then a block with a
        // signed 10-coin transfer from wallet 1 to wallet 2 (reward to wallet 2).
        static (Blockchain chain, PrivateKey key1, PrivateKey key2, Transaction transfer) BuildChain()
        {
            var key1 = new PrivateKey();
            var key2 = new PrivateKey();
            var chain = new Blockchain(Difficulty, Reward);
            chain.MinePendingTransactions(key1.publicKey());

            var transfer = new Transaction(key1.publicKey(), key2.publicKey(), 10);
            transfer.SignTransaction(key1);
            chain.addPendingTransaction(transfer);
            chain.MinePendingTransactions(key2.publicKey());
            return (chain, key1, key2, transfer);
        }

        static Transaction Signed(PrivateKey from, PublicKey to, decimal amount)
        {
            var tx = new Transaction(from.publicKey(), to, amount);
            tx.SignTransaction(from);
            return tx;
        }

        // Appends a block with the given transactions, correctly linked and mined.
        static Block AppendMinedBlock(Blockchain chain, List<Transaction> transactions)
        {
            Block latest = chain.GetLatestBlock();
            var block = new Block(latest.Index + 1, DateTime.Now.ToString("yyyyMMddHHmmssffff"), transactions, latest.Hash);
            block.Mine(chain.Difficulty);
            chain.Chain.Add(block);
            return block;
        }

        [Fact]
        public void HonestChain_IsValid_AndBalancesAreCorrect()
        {
            var (chain, key1, key2, _) = BuildChain();
            Assert.True(chain.IsChainValid());
            Assert.Equal(90m, chain.GetBalanceOfWallet(key1.publicKey()));
            Assert.Equal(110m, chain.GetBalanceOfWallet(key2.publicKey()));
        }

        // ---- Fix 1: block hash covers transaction contents ----

        [Fact]
        public void Regression_TamperingWithMinedAmount_MakesChainInvalid()
        {
            var (chain, _, _, _) = BuildChain();
            Assert.True(chain.IsChainValid());

            chain.Chain[2].Transactions[0].Amount = 1000;

            Assert.False(chain.IsChainValid());
            Assert.NotEqual(chain.Chain[2].Hash, chain.Chain[2].CalculateHash());
        }

        [Fact]
        public void BlockHash_ChangesWhenAnyTransactionFieldChanges()
        {
            var (chain, _, key2, _) = BuildChain();
            Block block = chain.Chain[2];
            Transaction tx = block.Transactions[0];
            string original = block.CalculateHash();

            tx.Amount += 1;
            Assert.NotEqual(original, block.CalculateHash());
            tx.Amount -= 1;

            tx.Timestamp += 1;
            Assert.NotEqual(original, block.CalculateHash());
            tx.Timestamp -= 1;

            var to = tx.ToAddress;
            tx.ToAddress = new PrivateKey().publicKey();
            Assert.NotEqual(original, block.CalculateHash());
            tx.ToAddress = to;

            var sig = tx.Signature;
            tx.Signature = null;
            Assert.NotEqual(original, block.CalculateHash());
            tx.Signature = sig;

            Assert.Equal(original, block.CalculateHash());
        }

        [Fact]
        public void TransactionSerialization_IsScaleIndependentForAmounts()
        {
            var key = new PrivateKey();
            var a = new Transaction(key.publicKey(), key.publicKey(), 10m) { Timestamp = 1 };
            var b = new Transaction(key.publicKey(), key.publicKey(), 10.000m) { Timestamp = 1 };
            Assert.Equal(a.CalculateHash(), b.CalculateHash());
            Assert.Equal(a.Serialize(), b.Serialize());
        }

        // ---- Fix 2: IsChainValid re-verifies signatures, proof-of-work and genesis ----

        [Fact]
        public void ForgedSignature_InLinkedAndMinedBlock_MakesChainInvalid()
        {
            var (chain, key1, _, _) = BuildChain();
            var attacker = new PrivateKey();

            // Attacker spends wallet 1's coins, signing with their own key, and mines a
            // properly linked block so hashes, links and proof-of-work all check out.
            var theft = new Transaction(key1.publicKey(), attacker.publicKey(), 50);
            theft.Signature = Ecdsa.sign(theft.CalculateHash(), attacker);
            AppendMinedBlock(chain, new List<Transaction> { theft });

            Assert.False(chain.IsChainValid());
        }

        [Fact]
        public void TamperedAmount_ThenRemined_IsStillCaughtBySignatureCheck()
        {
            var (chain, _, _, _) = BuildChain();
            Block last = chain.Chain[2];
            last.Transactions[0].Amount = 50; // signature no longer matches
            last.Nonce = 0;
            last.Hash = last.CalculateHash();
            last.Mine(chain.Difficulty);

            Assert.Equal(last.Hash, last.CalculateHash());
            Assert.False(chain.IsChainValid());
        }

        [Fact]
        public void UnsignedNonRewardTransaction_MakesChainInvalid()
        {
            var (chain, key1, key2, _) = BuildChain();
            var unsigned = new Transaction(key1.publicKey(), key2.publicKey(), 5);
            AppendMinedBlock(chain, new List<Transaction> { unsigned });
            Assert.False(chain.IsChainValid());
        }

        [Fact]
        public void BlockWithoutProofOfWork_MakesChainInvalid()
        {
            var (chain, key1, key2, _) = BuildChain();
            Block latest = chain.GetLatestBlock();
            var block = new Block(latest.Index + 1, "20260101000000000", new List<Transaction> { Signed(key1, key2.publicKey(), 1) }, latest.Hash);
            // Pick a nonce whose hash does NOT meet the difficulty, so the hash is
            // internally consistent but no work was done.
            while (block.HasValidProofOfWork(chain.Difficulty))
            {
                block.Nonce++;
                block.Hash = block.CalculateHash();
            }
            chain.Chain.Add(block);

            Assert.Equal(block.Hash, block.CalculateHash());
            Assert.False(chain.IsChainValid());
        }

        [Fact]
        public void ReplacedGenesisBlock_MakesChainInvalid()
        {
            var key1 = new PrivateKey();
            var chain = new Blockchain(Difficulty, Reward);
            // Swap in a different genesis before mining, so every later link is consistent.
            chain.Chain[0] = new Block(0, "199901010000000000", new List<Transaction>());
            chain.MinePendingTransactions(key1.publicKey());

            Assert.Equal(chain.Chain[0].Hash, chain.Chain[1].PreviousHash);
            Assert.False(chain.IsChainValid());
        }

        [Fact]
        public void InflatedMiningReward_MakesChainInvalid()
        {
            var (chain, key1, _, _) = BuildChain();
            AppendMinedBlock(chain, new List<Transaction> { new Transaction(null, key1.publicKey(), 1_000_000) });
            Assert.False(chain.IsChainValid());
        }

        [Fact]
        public void TwoRewardsInOneBlock_MakesChainInvalid()
        {
            var (chain, key1, _, _) = BuildChain();
            AppendMinedBlock(chain, new List<Transaction>
            {
                new Transaction(null, key1.publicKey(), Reward),
                new Transaction(null, key1.publicKey(), Reward),
            });
            Assert.False(chain.IsChainValid());
        }

        [Fact]
        public void MinedOverspend_MakesChainInvalid()
        {
            var (chain, key1, key2, _) = BuildChain();
            // Validly signed, but wallet 1 only holds 90.
            AppendMinedBlock(chain, new List<Transaction> { Signed(key1, key2.publicKey(), 500) });
            Assert.False(chain.IsChainValid());
        }

        [Fact]
        public void ReplayedTransactionInLaterBlock_MakesChainInvalid()
        {
            var (chain, _, _, transfer) = BuildChain();
            AppendMinedBlock(chain, new List<Transaction> { transfer });
            Assert.False(chain.IsChainValid());
        }

        // ---- Fix 3: amount, pending-overspend and replay rules ----

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public void NonPositiveAmount_IsRejected(int amount)
        {
            var (chain, key1, key2, _) = BuildChain();
            var tx = Signed(key1, key2.publicKey(), amount);
            Assert.Throws<Exception>(() => chain.addPendingTransaction(tx));
            Assert.Empty(chain.PendingTransactions);
        }

        [Fact]
        public void PendingTransactions_CannotExceedBalance()
        {
            var (chain, key1, key2, _) = BuildChain(); // wallet 1 holds 90
            chain.addPendingTransaction(Signed(key1, key2.publicKey(), 60));
            var second = Signed(key1, key2.publicKey(), 60);
            Assert.Throws<Exception>(() => chain.addPendingTransaction(second));
            Assert.Single(chain.PendingTransactions);

            chain.addPendingTransaction(Signed(key1, key2.publicKey(), 30)); // exactly the remainder
            Assert.Equal(2, chain.PendingTransactions.Count);
        }

        [Fact]
        public void ReplayOfMinedTransaction_IsRejected()
        {
            var (chain, _, _, transfer) = BuildChain();
            Assert.Throws<Exception>(() => chain.addPendingTransaction(transfer));
        }

        [Fact]
        public void DuplicatePendingTransaction_IsRejected()
        {
            var (chain, key1, key2, _) = BuildChain();
            var tx = Signed(key1, key2.publicKey(), 5);
            chain.addPendingTransaction(tx);
            Assert.Throws<Exception>(() => chain.addPendingTransaction(tx));
            Assert.Single(chain.PendingTransactions);
        }

        [Fact]
        public void CannotSignForAnotherWallet()
        {
            var key1 = new PrivateKey();
            var other = new PrivateKey();
            var tx = new Transaction(key1.publicKey(), other.publicKey(), 1);
            Assert.Throws<Exception>(() => tx.SignTransaction(other));
        }
    }
}
