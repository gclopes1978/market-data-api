using System.Net;
using System.Text;

namespace MarketData.Tests;

/// <summary>
/// Shared HTTP stub: answers every request through a delegate and records the
/// absolute URI of each call, so tests can assert on routing and call counts.
/// </summary>
internal sealed class FakeHttpHandler(
    Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
{
    private readonly List<string> _requests = [];

    public IReadOnlyList<string> Requests => _requests;

    public int Calls => _requests.Count;

    public static FakeHttpHandler Always(
        HttpStatusCode status, string body, string mediaType = "application/json") =>
        new((_, _) => Respond(status, body, mediaType));

    /// <summary>Returns the bodies in order; the last one repeats for any further call.</summary>
    public static FakeHttpHandler Sequence(string mediaType, params string[] bodies) =>
        new((_, index) => Respond(
            HttpStatusCode.OK, bodies[Math.Min(index, bodies.Length - 1)], mediaType));

    public static HttpResponseMessage Respond(
        HttpStatusCode status, string body, string mediaType) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    public HttpClient CreateClient(string baseAddress = "https://example.test/") =>
        new(this) { BaseAddress = new Uri(baseAddress) };

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var index = _requests.Count;
        _requests.Add(request.RequestUri!.AbsoluteUri);
        return Task.FromResult(respond(request, index));
    }
}
