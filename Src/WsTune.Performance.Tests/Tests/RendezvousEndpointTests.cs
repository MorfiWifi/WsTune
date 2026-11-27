using WsTuneCommon.Models;

namespace WsTune.Performance.Tests.Tests;

/// <summary>
/// Guards rendezvous failover config: the primary endpoint plus any fallbacks are turned into
/// full hub URLs (identity/peerType appended, existing query preserved) so a dead primary can be
/// rotated out.
/// </summary>
[Trait("Category", "Unit")]
public class RendezvousEndpointTests
{
    [Fact]
    public void Listener_BuildsPrimaryThenFallbacks_WithIdentityAndPeerType()
    {
        var settings = new AppSettings
        {
            Identity = "N56",
            SignalREndpoint = "http://hub-a:1/hub",
            SignalRFallbackEndpoints = ["http://hub-b:2/hub"]
        };

        var endpoints = WsTuneCli.Listener.Transport.TransportHostService.BuildEndpoints(settings, "client");

        Assert.Equal(2, endpoints.Count);
        Assert.Equal("http://hub-a:1/hub?identity=N56&peerType=client", endpoints[0]);
        Assert.Equal("http://hub-b:2/hub?identity=N56&peerType=client", endpoints[1]);
    }

    [Fact]
    public void Server_PreservesExistingQuery_AndUsesServerPeerType()
    {
        var settings = new AppSettings
        {
            Identity = "A17",
            SignalREndpoint = "http://hub-a:1/hub?token=abc"
        };

        var endpoints = WsTuneCli.Server.Transport.TransportHostService.BuildEndpoints(settings);

        Assert.Single(endpoints);
        Assert.Equal("http://hub-a:1/hub?token=abc&identity=A17&peerType=server", endpoints[0]);
    }
}