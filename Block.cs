using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CloudCoin
{
    public class Block
    {
        public int Index { get; set; }
        public string PreviousHash { get; set; }
        public string Timestamp { get; set; }
        public string Hash { get; set; }
        public int Nonce { get; set; }
        public List<Transaction> Transactions { get; set; }

        public Block(int index, string timestamp, List<Transaction> transactions, string previousHash = "")
        {
            this.Index = index;
            this.Timestamp = timestamp;
            this.Transactions = transactions;
            this.PreviousHash = previousHash;
            this.Nonce = 0;
            this.Hash = CalculateHash();
        }

        public string CalculateHash()
        {
            // Previously this concatenated Transactions.ToString(), which is only the
            // list's type name, so the block hash did not cover the transactions.
            string transactionData = string.Join(";", this.Transactions.Select(t => t.Serialize()));
            string blockData = string.Join("|",
                this.Index.ToString(CultureInfo.InvariantCulture),
                this.PreviousHash ?? "",
                this.Timestamp ?? "",
                transactionData,
                this.Nonce.ToString(CultureInfo.InvariantCulture));
            return Transaction.Sha256Hex(blockData);
        }

        public bool HasValidProofOfWork(int difficulty)
        {
            return this.Hash != null && this.Hash.StartsWith(new string('0', difficulty), StringComparison.Ordinal);
        }

        public void Mine(int difficulty)
        {
            while (!HasValidProofOfWork(difficulty))
            {
                this.Nonce++;
                this.Hash = this.CalculateHash();
            }
            Console.WriteLine("Block has been mined: " + this.Hash);
        }
    }
}
