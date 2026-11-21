using ImageFlow;
namespace ImageFlow.IntegrationTests;

public class ModelCatalogTests
{
    static EngineOptions Official() => new() { Provider = "OpenAI", ApiKey = "test", CredentialEnvironmentVariable = "IMAGEFLOW_CATALOG_TEST_KEY" };

    [Fact]
    public void DefaultsAreExplicitAndSupported()
    {
        var options = Official(); options.Validate();
        Assert.Equal("gpt-6-luna", options.PromptModel); Assert.Equal("gpt-image-2.6-flare", options.ImageModel);
        Assert.Equal("medium", options.ImageQuality); Assert.Equal("high", options.ImageComplexity); Assert.Equal("png", options.OutputFormat);
        options.ImageModel = "gpt-image-2.6-sunburst"; options.Validate();
    }
    [Theory]
    [InlineData("gpt-image-1.5")]
    [InlineData("gpt-image-1-mini")]
    [InlineData("gpt-image-2")]
    [InlineData("chatgpt-image-latest")]
    [InlineData("unverified-image")]
    public void OfficialRejectsOldOrUnknownModelsWithoutAliases(string model)
    { var options = Official(); options.ImageModel = model; Assert.Throws<InvalidOperationException>(options.Validate); }

    [Theory]
    [InlineData("gpt-4.1-mini")]
    [InlineData("gpt-4.1-nano")]
    [InlineData("gpt-4.1")]
    public void OldPlanningModelsAreNotSupported(string model)
    { var options = Official(); options.PromptModel = model; Assert.Throws<InvalidOperationException>(options.Validate); }

    [Theory]
    [InlineData("1536x864", true)]
    [InlineData("3840x2160", true)]
    [InlineData("512x512", false)]
    [InlineData("4096x2048", false)]
    [InlineData("2048x2048", true)]
    [InlineData("1025x1024", false)]
    [InlineData("3840x3840", false)]
    [InlineData("3840x512", false)]
    [InlineData("auto", false)]
    public void ValidatesExplicitDimensionsAgainstOfficialConstraints(string size, bool supported)
    {
        var options = Official(); options.ImageSize = size;
        if (supported) options.Validate(); else Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void RejectsUnsupportedFormatQualityAndComplexity()
    {
        foreach (var options in new[] { Official(), Official(), Official() }.Select((o, i) =>
        { if (i == 0) o.OutputFormat = "svg"; else if (i == 1) o.ImageQuality = "auto"; else o.ImageComplexity = "invalid"; return o; }))
            Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void GatewayRequiresExplicitCapabilitiesAndDoesNotInheritOfficialModelLifecycle()
    {
        var options = ProviderTests.Compatible(); options.Validate();
        options.CompatibleCapabilities.ImageModels = []; Assert.Throws<InvalidOperationException>(options.Validate);
        options.CompatibleCapabilities.ImageModels = ["gpt-image-1.5"]; options.ImageModel = "gpt-image-1.5";
        options.Validate(); // Operator declaration, not a claim about OpenAI model support.
        options.ImageSize = "1536x864"; Assert.Throws<InvalidOperationException>(options.Validate);
        options.ImageSize = "1024x1024"; options.CredentialEnvironmentVariable = "OPENAI_API_KEY";
        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData("endpoint")]
    [InlineData("profile")]
    [InlineData("credential")]
    [InlineData("adapter")]
    public async Task MismatchedSnapshotCannotSendOrReuseCredentials(string mismatch)
    {
        var options = ProviderTests.Compatible(); var request = options.CreateImageRequest("prompt");
        request = mismatch switch {
            "endpoint" => request with { Endpoint = "https://other.test/v1/" },
            "profile" => request with { ProviderProfile = "other" },
            "credential" => request with { CredentialReference = "OTHER_SECRET" },
            _ => request with { AdapterVersion = "old" }
        };
        using var http = new HttpClient(new RejectNetwork());
        await Assert.ThrowsAsync<InvalidOperationException>(() => new OpenAiProvider(http, options).GenerateAsync(request, CancellationToken.None));
    }
    sealed class RejectNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw new Xunit.Sdk.XunitException("No network request was expected.");
    }
}
