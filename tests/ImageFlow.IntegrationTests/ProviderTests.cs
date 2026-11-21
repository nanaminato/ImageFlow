using System.Net;
using System.Text;
using ImageFlow;
namespace ImageFlow.IntegrationTests;

public class ProviderTests
{
    static EngineOptions Official() => new() { Provider = "OpenAI", ApiKey = "unit-test-credential" };
    internal static EngineOptions Compatible() => new() { Provider = "OpenAICompatible", ProviderProfile = "gateway-test", CredentialEnvironmentVariable = "IMAGEFLOW_TEST_GATEWAY_KEY", BaseUrl = "https://example.test/api/native/v1", ApiKey = "local-test-value", CompatibleCapabilities = new() { PromptModels = ["gpt-6-luna"], ImageModels = ["gpt-image-2.6-flare"], Sizes = ["1024x1024"], Qualities = ["medium"], Complexities = ["high"], Formats = ["png"] } };
    sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request);
    }
    static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    [Fact]
    public async Task OpenAiPlannerUsesStructuredOutputAndValidatesCount()
    {
        var http = new HttpClient(new Handler(async r =>
        {
            Assert.Equal("https://api.openai.com/v1/responses", r.RequestUri!.ToString());
            var body = await r.Content!.ReadAsStringAsync(); Assert.Contains("json_schema", body); Assert.Contains("gpt-6-luna", body);
            return Json("""{"output":[{"content":[{"type":"output_text","text":"{\"prompts\":[\"One\",\"Two\"]}"}]}]}""");
        }));
        var provider = new OpenAiProvider(http, Official());
        Assert.Equal(new[] { "One", "Two" }, await provider.PlanAsync("theme", 2, CancellationToken.None));
        await Assert.ThrowsAsync<ProviderFailure>(() => provider.PlanAsync("theme", 3, CancellationToken.None));
    }
    [Fact]
    public async Task OpenAiImageReturnsBytesUsageAndRequestId()
    {
        var http = new HttpClient(new Handler(async r =>
        {
            Assert.Equal("/v1/images/generations", r.RequestUri!.AbsolutePath);
            Assert.Contains("gpt-image-2.6-flare", await r.Content!.ReadAsStringAsync());
            var response = Json(PngFixture.Response); response.Headers.Add("x-request-id", "request-test"); return response;
        }));
        var image = await new OpenAiProvider(http, Official()).GenerateAsync(Official().CreateImageRequest("stored prompt"), CancellationToken.None);
        PngFixture.AssertDecodable(image.Bytes); Assert.Equal("request-test", image.RequestId); Assert.Contains("42", image.UsageJson);
    }
    [Theory]
    [InlineData(429, true, false)]
    [InlineData(500, false, true)]
    [InlineData(400, false, false)]
    public async Task OpenAiResponseClassifiesRetrySafety(int status, bool retryable, bool unknown)
    {
        var http = new HttpClient(new Handler(_ => { var response = new HttpResponseMessage((HttpStatusCode)status); response.Headers.RetryAfter = new(TimeSpan.FromSeconds(45)); return Task.FromResult(response); }));
        var failure = await Assert.ThrowsAsync<ProviderFailure>(() => new OpenAiProvider(http, Official()).GenerateAsync(Official().CreateImageRequest("prompt"), CancellationToken.None));
        Assert.Equal(retryable, failure.Retryable); Assert.Equal(unknown, failure.Unknown); Assert.Equal(TimeSpan.FromSeconds(45), failure.RetryAfter);
    }
    [Fact]
    public async Task CompatibleProviderUsesExplicitBaseUrlAndLocalCredential()
    {
        var options = Compatible();
        options.Validate();
        var http = new HttpClient(new Handler(r =>
        {
            Assert.Equal("https://example.test/api/native/v1/images/generations", r.RequestUri!.ToString());
            Assert.Equal("Bearer", r.Headers.Authorization!.Scheme);
            Assert.Equal(options.GetApiKey(), r.Headers.Authorization.Parameter);
            return Task.FromResult(Json(PngFixture.Response));
        }));
        await new OpenAiProvider(http, options).GenerateAsync(options.CreateImageRequest("test"), CancellationToken.None);
        options.Provider = "OpenAI";
        Assert.Equal("api.openai.com", options.GetBaseUri().Host);
    }
    [Theory]
    [InlineData("http://example.test/v1")]
    [InlineData("https://user:password@example.test/v1")]
    [InlineData("https://example.test/v1?key=secret")]
    public void CompatibleProviderRejectsUnsafeConfiguration(string url)
    {
        Assert.Throws<InvalidOperationException>(() => new EngineOptions { Provider = "OpenAICompatible", BaseUrl = url, ApiKey = "test" }.Validate());
    }
    [Fact]
    public async Task NetworkFailureIsUnknown()
    {
        var http = new HttpClient(new Handler(_ => throw new HttpRequestException("network")));
        var failure = await Assert.ThrowsAsync<ProviderFailure>(() => new OpenAiProvider(http, Official()).GenerateAsync(Official().CreateImageRequest("prompt"), CancellationToken.None));
        Assert.True(failure.Unknown); Assert.False(failure.Retryable);
    }
    [Fact]
    public async Task MalformedResponseRetainsRequestIdForManualReview()
    {
        var http=new HttpClient(new Handler(_=>{
            var response=Json("invalid JSON");response.Headers.Add("x-request-id","req-malformed");return Task.FromResult(response);
        }));
        var failure=await Assert.ThrowsAsync<ProviderFailure>(()=>new OpenAiProvider(http,Official()).GenerateAsync(Official().CreateImageRequest("test"),CancellationToken.None));
        Assert.True(failure.Unknown);Assert.Equal("req-malformed",failure.RequestId);
    }

}
