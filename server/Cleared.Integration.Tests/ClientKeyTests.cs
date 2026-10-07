using System.Net;
using Cleared.API.RateLimiting;

namespace Cleared.Integration.Tests;

// The key decides who shares an allowance, so it has to group the right addresses and no others.
public class ClientKeyTests
{
    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")]
    public void An_IPv4_address_counts_as_itself(string address, string expected)
    {
        Assert.Equal(expected, ClientKey.From(IPAddress.Parse(address)));
    }

    [Fact]
    public void Addresses_in_one_IPv6_64_block_share_a_key()
    {
        var first = ClientKey.From(IPAddress.Parse("2001:db8:aaaa:bbbb::1"));
        var second = ClientKey.From(IPAddress.Parse("2001:db8:aaaa:bbbb:ffff:ffff:ffff:ffff"));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Addresses_in_different_IPv6_64_blocks_do_not()
    {
        var first = ClientKey.From(IPAddress.Parse("2001:db8:aaaa:bbbb::1"));
        var second = ClientKey.From(IPAddress.Parse("2001:db8:aaaa:cccc::1"));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void A_request_with_no_address_still_gets_a_key()
    {
        Assert.Equal("unknown", ClientKey.From(null));
    }
}
