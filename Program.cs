using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using EllipticCurve;


namespace CloudCoin
{
    class Program
    {
        static void Main(string[] args)
        {
            PrivateKey key1 = new PrivateKey();
            PublicKey wallet1 = key1.publicKey();

            PrivateKey key2 = new PrivateKey();
            PublicKey wallet2 = key2.publicKey();

            Blockchain CloudCoin = new Blockchain(3, 100);

            Console.WriteLine("Start the miner");
            CloudCoin.MinePendingTransactions(wallet1);
            Console.WriteLine("\nBalance of wallet is $" + CloudCoin.GetBalanceOfWallet(wallet1).ToString());

            Transaction tx1 = new Transaction(wallet1, wallet2, 10);
            tx1.SignTransaction(key1);
            CloudCoin.addPendingTransaction(tx1);
            Console.WriteLine("Start the miner");
            CloudCoin.MinePendingTransactions(wallet2);
            Console.WriteLine("\nBalance of wallet1 is $" + CloudCoin.GetBalanceOfWallet(wallet1).ToString());
            Console.WriteLine("\nBalance of wallet2 is $" + CloudCoin.GetBalanceOfWallet(wallet2).ToString());

            string blockJSON = JsonConvert.SerializeObject(CloudCoin, Formatting.Indented);
            Console.WriteLine(blockJSON);



            if (CloudCoin.IsChainValid())
            {
                Console.WriteLine("Blockchain is valid");
            }
            else
            {
                Console.WriteLine("Blockchain not valid");
            }

        }
    }




}
