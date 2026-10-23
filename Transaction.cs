using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EllipticCurve;

namespace CloudCoin
{
    public class Transaction
    {
        public PublicKey FromAddress { get; set; }
        public PublicKey ToAddress { get; set; }
        public decimal Amount { get; set; }
        // Unix time in milliseconds. Covered by the signature so that two otherwise
        // identical transfers produce different hashes (used for replay protection).
        public long Timestamp { get; set; }
        public Signature Signature { get; set; }

        public Transaction(PublicKey fromAddress, PublicKey toAddress, decimal amount)
        {
            this.FromAddress = fromAddress;
            this.ToAddress = toAddress;
            this.Amount = amount;
            this.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        public void SignTransaction(PrivateKey signingKey)
        {
            if (this.FromAddress is null)
            {
                throw new Exception("Mining reward transactions are not signed");
            }
            if (AddressToHex(this.FromAddress) != AddressToHex(signingKey.publicKey()))
            {
                throw new Exception("You cannot sign transactions for other wallet!");
            }
            this.Signature = Ecdsa.sign(this.CalculateHash(), signingKey);
        }

        // Hash of the signed fields: from, to, amount and timestamp.
        public string CalculateHash()
        {
            string transactionData = string.Join("|",
                AddressToHex(this.FromAddress),
                AddressToHex(this.ToAddress),
                FormatAmount(this.Amount),
                this.Timestamp.ToString(CultureInfo.InvariantCulture));
            return Sha256Hex(transactionData);
        }

        // Deterministic serialization of every field, including the signature.
        // Block.CalculateHash uses this so a block hash commits to its transactions.
        public string Serialize()
        {
            return string.Join("|",
                AddressToHex(this.FromAddress),
                AddressToHex(this.ToAddress),
                FormatAmount(this.Amount),
                this.Timestamp.ToString(CultureInfo.InvariantCulture),
                this.Signature is null ? "" : this.Signature.toBase64());
        }

        public bool IsValid()
        {
            if (this.FromAddress is null) return true; // mining reward; rules enforced by Blockchain
            if (this.Signature is null)
            {
                throw new Exception("No signature is in this transaction");
            }
            return Ecdsa.verify(this.CalculateHash(), this.Signature, this.FromAddress);
        }

        public static string AddressToHex(PublicKey address)
        {
            return address is null ? "" : BitConverter.ToString(address.toDer()).Replace("-", "");
        }

        // Culture-invariant and scale-independent, so 10 and 10.0 serialize the same way.
        public static string FormatAmount(decimal amount)
        {
            return amount.ToString("G29", CultureInfo.InvariantCulture);
        }

        public static string Sha256Hex(string data)
        {
            using SHA256 sha256 = SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(data));
            return BitConverter.ToString(hashBytes).Replace("-", "");
        }
    }
}
