using Server.Envir;
using Xunit;

namespace ProtocolTests
{
    public sealed class WebServerIPNTests
    {
        [Fact]
        public void IPNPayloadValidationAcceptsCompletePayment()
        {
            WebServer.ClearRecentIPNIdsForTests();

            bool valid = WebServer.TryValidateIPNPayload(
                "payment_status=Completed&txn_id=TXN-1&receiver_email=shop%40example.com&mc_gross=5.00&custom=Hero&mc_currency=GBP&txn_type=web_accept",
                out string transactionId,
                out bool duplicate,
                out string rejection);

            Assert.True(valid, rejection);
            Assert.Equal("TXN-1", transactionId);
            Assert.False(duplicate);
        }

        [Fact]
        public void IPNPayloadValidationRejectsMissingRequiredField()
        {
            WebServer.ClearRecentIPNIdsForTests();

            bool valid = WebServer.TryValidateIPNPayload(
                "payment_status=Completed&txn_id=TXN-2&receiver_email=shop%40example.com&custom=Hero&mc_currency=GBP&txn_type=web_accept",
                out _,
                out bool duplicate,
                out string rejection);

            Assert.False(valid);
            Assert.False(duplicate);
            Assert.Contains("mc_gross", rejection);
        }

        [Fact]
        public void IPNPayloadValidationDeduplicatesQueuedTransactionIds()
        {
            WebServer.ClearRecentIPNIdsForTests();
            string payload = "payment_status=Completed&txn_id=TXN-3&receiver_email=shop%40example.com&mc_gross=5.00&custom=Hero&mc_currency=GBP&txn_type=web_accept";

            Assert.True(WebServer.TryValidateIPNPayload(payload, out _, out _, out _));

            bool valid = WebServer.TryValidateIPNPayload(payload, out _, out bool duplicate, out string rejection);

            Assert.False(valid);
            Assert.True(duplicate);
            Assert.Contains("TXN-3", rejection);
        }
    }
}
