using System.Net;
using System.Text;
namespace ImageFlow;

public class MockProvider : IPromptPlanningProvider, IImageGenerationProvider
{
    public Task<string[]> PlanAsync(string theme, int count, CancellationToken ct) => Task.FromResult(Enumerable.Range(1, count).Select(i => $"{theme}; composition {i}; {(i % 3 == 0 ? "soft watercolor" : i % 3 == 1 ? "cinematic wide angle" : "detailed close-up")}; intentional lighting, no lettering.").ToArray());
    public async Task<ImageResult> GenerateAsync(ImageGenerationRequest request, CancellationToken ct)
    {
        await Task.Delay(250, ct);
        var safe = WebUtility.HtmlEncode(request.Prompt);
        var svg = $"<svg xmlns='http://www.w3.org/2000/svg' width='768' height='512'><defs><linearGradient id='g'><stop stop-color='#182c4e'/><stop offset='1' stop-color='#578a83'/></linearGradient></defs><rect width='768' height='512' fill='url(#g)'/><circle cx='560' cy='160' r='90' fill='#e9be74'/><path d='M0 420L210 180L430 420L610 280L768 450V512H0Z' fill='#203d4b'/><text x='32' y='46' fill='white' font-family='sans-serif' font-size='24'>ImageFlow · MOCK preview</text><foreignObject x='32' y='430' width='704' height='80'><div xmlns='http://www.w3.org/1999/xhtml' style='color:white;font:14px sans-serif'>{safe}</div></foreignObject></svg>";
        return new(Encoding.UTF8.GetBytes(svg), "svg", "image/svg+xml", null, null);
    }
}
