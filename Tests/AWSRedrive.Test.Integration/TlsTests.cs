using AWSRedrive.Test.Integration.Infrastructure;
using Xunit;

namespace AWSRedrive.Test.Integration;

/// <summary>
/// IgnoreCertificateErrors, against a sink presenting a self-signed certificate
/// on port 8443. See HttpMessageProcessor.CreateOptions.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class TlsTests : IntegrationTest
{
    [Fact]
    public async Task IgnoreCertificateErrorsAllowsDeliveryOverSelfSignedHttps()
    {
        var correlation = NewCorrelation();

        await Queues.SendAsync("it-https-lax", JsonBody(correlation));

        var records = await Sink.WaitForDeliveryAsync(correlation);

        var record = Assert.Single(records);
        Assert.Equal("/http/tls", record.Path);
        Assert.Equal("POST", record.Method);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task WithoutIgnoreCertificateErrorsSelfSignedHttpsIsRejected()
    {
        var correlation = NewCorrelation();

        await Queues.SendAsync("it-https-strict", JsonBody(correlation));

        // The certificate is self-signed and this alias does not waive validation,
        // so the handler refuses the connection on every attempt. Nothing should
        // ever reach the sink; 30s covers several redelivery cycles at a 5s
        // visibility timeout.
        await Sink.AssertNoDeliveryAsync(correlation, TimeSpan.FromSeconds(30));
    }
}
