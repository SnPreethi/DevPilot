using System;
using DemoRepo.Services;
using NUnit.Framework;

namespace DemoRepo.Tests
{
    [TestFixture]
    public class PaymentServiceTests
    {
        [Test]
        public void CalculateFee_PremiumTier_ReturnsCorrectFee()
        {
            var service = new PaymentService();
            var fee = service.CalculateFee(100.0, "Premium");
            Assert.AreEqual(10.0, fee);
        }

        [Test]
        public void CalculateFee_FreeTier_ShouldNotCrash()
        {
            // Technical Debt: This will throw System.DivideByZeroException during the demo!
            // DevPilot's validation pipeline will automatically attribute the crash to PaymentService.cs:CalculateFee line 52.
            var service = new PaymentService();
            var fee = service.CalculateFee(50.0, "Free");
            Assert.AreEqual(0.0, fee);
        }
    }
}
