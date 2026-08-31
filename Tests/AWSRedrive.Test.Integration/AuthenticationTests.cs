using System.Text;
using AWSRedrive.Test.Integration.Infrastructure;
using Xunit;

namespace AWSRedrive.Test.Integration;

/// <summary>
/// The three authentication modes. Each alias points at a sink route that
/// challenges anything without the right credential, so a delivery that was
/// recorded exactly once proves the credential was accepted - a rejected
/// attempt would come back around and be recorded again.
/// See HttpMessageProcessor.AddAuthentication and CreateOptions.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class AuthenticationTests : IntegrationTest
{
    [Fact]
    public async Task AuthTokenArrivesAsTheAuthorizationHeader()
    {
        var correlation = NewCorrelation();

        await Queues.SendAsync("it-auth-token", JsonBody(correlation));

        var records = await Sink.WaitForDeliveryAsync(correlation);

        var record = Assert.Single(records);
        Assert.Equal("/secure/token", record.Path);
        Assert.Equal(Env.ExpectedAuthToken, record.Header("Authorization"));
    }

    [Fact]
    public async Task AwsGatewayTokenArrivesAsTheXApiKeyHeader()
    {
        var correlation = NewCorrelation();

        await Queues.SendAsync("it-gateway-token", JsonBody(correlation));

        var records = await Sink.WaitForDeliveryAsync(correlation);

        var record = Assert.Single(records);
        Assert.Equal("/secure/apikey", record.Path);
        Assert.Equal(Env.ExpectedApiKey, record.Header("x-api-key"));
    }

    [Fact]
    public async Task BasicCredentialsAreAcceptedByASinkThatChallengesEveryoneElse()
    {
        var correlation = NewCorrelation();

        await Queues.SendAsync("it-basic-auth", JsonBody(correlation));

        var records = await Sink.WaitForDeliveryAsync(correlation);

        var record = Assert.Single(records);
        Assert.Equal("/secure/basic", record.Path);

        var expected = "Basic " + Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{Env.ExpectedBasicUser}:{Env.ExpectedBasicPassword}"));
        Assert.Equal(expected, record.Header("Authorization"));
    }
}
