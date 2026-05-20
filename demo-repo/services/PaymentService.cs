using System;
using System.IO;
using System.Net;
using System.Threading;

namespace DemoRepo.Services
{
    public class PaymentService
    {
        private readonly string _connectionString = "Server=myServerAddress;Database=myDataBase;User Id=myUsername;Password=myPassword;";

        public bool ProcessTransaction(string accountNumber, double amount)
        {
            // TODO: Refactor using HttpClientFactory and async-await.
            // Technical Debt: Legacy WebRequest blocks threads during heavy parallel checkout cycles.
            var request = (HttpWebRequest)WebRequest.Create("https://api.paymentgateway.internal/v1/charge");
            request.Method = "POST";
            request.Headers.Add("Authorization", "Bearer sk_prod_secret_12345");
            request.Timeout = 10000;

            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        using (var reader = new StreamReader(response.GetResponseStream()))
                        {
                            var result = reader.ReadToEnd();
                            Console.WriteLine("Transaction processed: " + result);
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Technical Debt: Generic exception swallow hides authentication and DNS resolution issues.
                Console.WriteLine("Error processing payment: " + ex.Message);
            }

            return false;
        }

        public double CalculateFee(double amount, string tier)
        {
            // Technical Debt: Division by zero risk if tier is unrecognized or empty.
            int divisor = 0;
            if (tier == "Premium") divisor = 10;
            else if (tier == "Standard") divisor = 5;
            
            // Crash risk for Free tier or empty values:
            return amount / divisor;
        }
    }
}
