using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using stellar_dotnet_sdk;
using stellar_dotnet_sdk.responses;
using stellar_dotnet_sdk.responses.operations;

namespace Tradeio.Tests
{
    [TestClass]
    public class UnitTests
    {
        private const string StellarNodeUri = "https://horizon-testnet.stellar.org";
        private const string StellarFriendBotUri = "https://friendbot.stellar.org";
        
        private const string Account1SecretSeed = "SDANVBPXX6UUXYZZ7KO45BCEO2WGCKYTVK7RJQKJMC7RTZ5CS3ES35YO";
        private const string Account2SecretSeed = "SD2RGMSJ7MHR66AMUIJOOSJA6HNAL4C3E5O66Z2ZLQWOILB5IEJB7TBI";
        private const string MultisigAccountSecretSeed = "SC4MOEVNHHCUUCIDWOHM4HQIXYJMYMIYIMMXG4OSBQ3RE3T3D7YFIQEK";
        /*
         master key weight: 3
         low threshold: 2
         medium threshold: 2
         high threshold: 3
         Account 1 key weight: 1
         Account 2 key weight: 1
        */

        [ClassInitialize]
        public static void ClassInit(TestContext context)
        {
            Network.UseTestNetwork();
        }
        
        [TestMethod]
        public async Task ListenPaymentsTest()
        {
            var result = (payment: false, deposit: false);
            using (var stellarServer = new Server(StellarNodeUri))
            {
                KeyPair account1 = KeyPair.FromSecretSeed(Account1SecretSeed);
                KeyPair account2 = KeyPair.FromSecretSeed(Account2SecretSeed);

                var tcs = new TaskCompletionSource<(bool payment, bool deposit)>();
                
                IEventSource eventSource = stellarServer
                    .Payments
                    .ForAccount(account2)
                    .Cursor("now")
                    .Stream((sender, operation) =>
                    {
                        if (operation is PaymentOperationResponse payment)
                        {
                            if (payment.To.Address == account2.Address)
                            {
                                result.deposit = true;
                            }
                            else if (payment.From.Address == account2.Address)
                            {
                                result.payment = true;
                            }

                            if (result.payment && result.deposit)
                            {
                                tcs.SetResult(result);
                            }
                        }
                    });

                eventSource.Connect();

                await SendPayment(account1, account2, 1, stellarServer);
                await SendPayment(account2, account1, 1, stellarServer);

                await Task.WhenAny(tcs.Task, Task.Delay(10000));
                eventSource.Dispose();
            }
            
            Assert.IsTrue(result.payment);
            Assert.IsTrue(result.deposit);
        }
        
        [TestMethod]
        public async Task MultisigTest()
        {
            using (var stellarServer = new Server(StellarNodeUri))
            {
                KeyPair multisigAccount = KeyPair.FromSecretSeed(MultisigAccountSecretSeed);
                KeyPair account1 = KeyPair.FromSecretSeed(Account1SecretSeed);
                KeyPair account2 = KeyPair.FromSecretSeed(Account2SecretSeed);
                
                AccountResponse senderAccount = await stellarServer.Accounts.Account(multisigAccount);
                Operation payment = new PaymentOperation.Builder(account1, new AssetTypeNative(), "1").Build();
                
                Transaction transaction = new Transaction.Builder(senderAccount)
                    .AddOperation(payment)
                    .Build();
                
                transaction.Sign(account1);
                transaction.Sign(account2);
                
                SubmitTransactionResponse result = await stellarServer.SubmitTransaction(transaction);
                
                Assert.IsTrue(result.IsSuccess());
            }
        }

        private async Task SendPayment(KeyPair from, KeyPair to, int amount, Server stellarServer)
        {
            AccountResponse senderAccount = await stellarServer.Accounts.Account(from);
            Operation payment = new PaymentOperation.Builder(to, new AssetTypeNative(), amount.ToString()).Build();
                
            Transaction transaction = new Transaction.Builder(senderAccount)
                .AddOperation(payment)
                .Build();
                
            transaction.Sign(from);
            await stellarServer.SubmitTransaction(transaction);
        }
        
        private async Task CreateAccountWithFriendBot(string accountAddress)
        {
            using (HttpClient httpClient = new HttpClient())
            {
                string requestUri = $"{StellarFriendBotUri}/?addr={accountAddress}";
                HttpResponseMessage response = await httpClient.GetAsync(requestUri);

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new Exception("Something went wrong during request to Friendbot");
                }
            }
        }
    }
}