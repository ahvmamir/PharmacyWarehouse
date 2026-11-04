using System.Net;

namespace PharmacyWarehouse.Api.Tests;

// The Api also serves the Client in production. Routing once ran before the static files, which
// answered app.css with HTML and blazor.webassembly.js with a 500, so the installed app never started.
public class ClientHostingTests : ApiTestBase
{
    [Theory]
    [InlineData("/")]
    [InlineData("/locations")]
    [InlineData("/alerts")]
    [InlineData("/admin/users")]
    public async Task ClientRoute_ReturnsTheClientPage(string path)
    {
        using var client = App.NewClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("_framework/blazor.webassembly.js", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/css/app.css", "text/css")]
    [InlineData("/_framework/blazor.webassembly.js", "text/javascript")]
    public async Task ClientFile_IsServedAsItself(string path, string mediaType)
    {
        using var client = App.NewClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
    }
}
