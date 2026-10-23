using System;
using System.Collections.Generic;
using System.Linq;
using EllipticCurve;

namespace CloudCoin
{
    public class Blockchain
    {
        // Fixed so the genesis block is deterministic and can be checked by IsChainValid.
        public const string GenesisTimestamp = "202101010000000000";

        public List<Block> Chain { get; set; }
        public int Difficulty { get; set; }
        List<Transaction> pendingTransactions { get; set; }
        public decimal MiningReward { get; set; }

        public Blockchain(int difficulty, decimal miningReward)
        {
            this.Chain = new List<Block>();
            this.Chain.Add(CreateGenesisBlock());
            this.Difficulty = difficulty;
            this.MiningReward = miningReward;
            this.pendingTransactions = new List<Transaction>();
        }

        public IReadOnlyList<Transaction> PendingTransactions => this.pendingTransactions.AsReadOnly();

        public Block CreateGenesisBlock()
        {
            return new Block(0, GenesisTimestamp, new List<Transaction>());
        }

        public Block GetLatestBlock()
        {
            return this.Chain.Last();
        }

        public void AddBlock(Block newBlock)
        {
            newBlock.PreviousHash = this.GetLatestBlock().Hash;
            newBlock.Hash = newBlock.CalculateHash();
            this.Chain.Add(newBlock);
        }

        public void MinePendingTransactions(PublicKey miningRewardWallet)
        {
            Transaction rewardTx = new Transaction(null, miningRewardWallet, MiningReward);
            this.pendingTransactions.Add(rewardTx);

            Block newBlock = new Block(GetLatestBlock().Index + 1, DateTime.Now.ToString("yyyyMMddHHmmssffff"), this.pendingTransactions, GetLatestBlock().Hash);
            newBlock.Mine(this.Difficulty);
            Console.WriteLine("Block successfully mined");
            this.Chain.Add(newBlock);
            this.pendingTransactions = new List<Transaction>();
        }

        public void addPendingTransaction(Transaction transaction)
        {
            if (transaction.FromAddress is null || transaction.ToAddress is null)
            {
                throw new Exception("Transactions must include a to and from address");
            }
            if (transaction.Amount <= 0)
            {
                throw new Exception("Transaction amount must be greater than zero");
            }
            if (transaction.IsValid() == false)
            {
                throw new Exception("Cannot add an invalid transaction to a block");
            }

            // Replay protection: the same signed transaction (same hash) can only be used once.
            string txHash = transaction.CalculateHash();
            bool seenBefore = this.Chain.SelectMany(b => b.Transactions)
                .Concat(this.pendingTransactions)
                .Any(t => t.FromAddress != null && t.CalculateHash() == txHash);
            if (seenBefore)
            {
                throw new Exception("Duplicate transaction (replay) rejected");
            }

            // Count coins already committed by this wallet's pending transactions.
            string fromHex = Transaction.AddressToHex(transaction.FromAddress);
            decimal pendingSpend = this.pendingTransactions
                .Where(t => Transaction.AddressToHex(t.FromAddress) == fromHex && t.FromAddress != null)
                .Sum(t => t.Amount);
            if (pendingSpend + transaction.Amount > this.GetBalanceOfWallet(transaction.FromAddress))
            {
                throw new Exception("There must be sufficient money in wallet");
            }

            this.pendingTransactions.Add(transaction);
        }

        public decimal GetBalanceOfWallet(PublicKey address)
        {
            decimal balance = 0;
            string addressHex = Transaction.AddressToHex(address);

            foreach (Block block in this.Chain)
            {
                foreach (Transaction transaction in block.Transactions)
                {
                    if (!(transaction.FromAddress is null) && Transaction.AddressToHex(transaction.FromAddress) == addressHex)
                    {
                        balance -= transaction.Amount;
                    }
                    if (Transaction.AddressToHex(transaction.ToAddress) == addressHex)
                    {
                        balance += transaction.Amount;
                    }
                }
            }
            return balance;
        }

        public bool IsChainValid()
        {
            if (this.Chain.Count == 0)
            {
                return false;
            }

            // Genesis block must match the deterministic genesis block exactly.
            Block genesis = this.Chain[0];
            Block expectedGenesis = CreateGenesisBlock();
            if (genesis.Index != 0
                || genesis.PreviousHash != expectedGenesis.PreviousHash
                || genesis.Timestamp != expectedGenesis.Timestamp
                || genesis.Transactions.Count != 0
                || genesis.Hash != expectedGenesis.Hash
                || genesis.Hash != genesis.CalculateHash())
            {
                return false;
            }

            var seenTransactions = new HashSet<string>();
            var balances = new Dictionary<string, decimal>();

            for (int i = 1; i < this.Chain.Count; i++)
            {
                Block currentBlock = this.Chain[i];
                Block previousBlock = this.Chain[i - 1];

                if (currentBlock.Index != i)
                {
                    return false;
                }
                // Stored hash must match the contents (now including transactions).
                if (currentBlock.Hash != currentBlock.CalculateHash())
                {
                    return false;
                }
                if (currentBlock.PreviousHash != previousBlock.Hash)
                {
                    return false;
                }
                if (!currentBlock.HasValidProofOfWork(this.Difficulty))
                {
                    return false;
                }
                if (!HasValidTransactions(currentBlock, seenTransactions, balances))
                {
                    return false;
                }
            }
            return true;
        }

        bool HasValidTransactions(Block block, HashSet<string> seenTransactions, Dictionary<string, decimal> balances)
        {
            int rewardCount = 0;
            foreach (Transaction tx in block.Transactions)
            {
                if (tx.ToAddress is null || tx.Amount <= 0)
                {
                    return false;
                }

                if (tx.FromAddress is null)
                {
                    // At most one reward per block, for exactly the configured amount, unsigned.
                    rewardCount++;
                    if (rewardCount > 1 || tx.Amount != this.MiningReward || tx.Signature != null)
                    {
                        return false;
                    }
                }
                else
                {
                    try
                    {
                        if (!tx.IsValid())
                        {
                            return false;
                        }
                    }
                    catch (Exception)
                    {
                        return false; // missing or malformed signature
                    }

                    if (!seenTransactions.Add(tx.CalculateHash()))
                    {
                        return false; // replayed transaction
                    }

                    string fromHex = Transaction.AddressToHex(tx.FromAddress);
                    balances.TryGetValue(fromHex, out decimal fromBalance);
                    if (fromBalance < tx.Amount)
                    {
                        return false; // spends more than the wallet held
                    }
                    balances[fromHex] = fromBalance - tx.Amount;
                }

                string toHex = Transaction.AddressToHex(tx.ToAddress);
                balances.TryGetValue(toHex, out decimal toBalance);
                balances[toHex] = toBalance + tx.Amount;
            }
            return true;
        }
    }
}
