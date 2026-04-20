using System.Net;
using System.Text;
using Diva.Agents.Tests.Helpers;
using Diva.Core.Configuration;
using Diva.Core.Models;
using Diva.Infrastructure.A2A;
using Microsoft.Extensions.Logging.Abstractions;

namespace Diva.Agents.Tests;

/// <summary>
/// Unit tests for <see cref="A2AAgentClient"/>.
/// Uses a mock <see cref="HttpMessageHandler"/> to simulate remote agent responses.
/// </summary>
public class A2AAgentClientTests
{
    private static A2AAgentClient CreateClient(HttpMessageHandler handler, A2AOptions? opts = null)
    {
        var http = new HttpClient(handler);
        return new A2AAgentClient(
            http,
            AgentTestFixtures.Opts(opts ?? new A2AOptions()),
            NullLogger<A2AAgentClient>.Instance);
    }

    private static AgentRequest MakeRequest(string query = "hello", int depth = 0)
    {
        var r = new AgentRequest { Query = query };
        r.Metadata["a2a_depth"] = depth;
        return r;
    }

    // ── DiscoverAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DiscoverAsync_FetchesAgentCard()
    {
        var handler = new FakeHandler("""{ "name": "TestAgent", "version": "1" }""", "application/json");
        var sut = CreateClient(handler);

        var card = await sut.DiscoverAsync("https://agent.example.com", CancellationToken.None);

        Assert.NotNull(card);
        Assert.Contains("agent.example.com/.well-known/agent.json", handler.LastRequestUri!.ToString());
    }

    // ── SendTaskAsync — SSE parsing ───────────────────────────────────────────

    [Fact]
    public async Task SendTaskAsync_ParsesSSEChunks()
    {
        var sse = """
            data: {"type":"thinking","content":"Planning..."}
            
            data: {"type":"final_response","content":"The answer"}
            
            data: {"type":"done"}
            
            """;
        var handler = new FakeHandler(sse, "text/event-stream");
        var sut = CreateClient(handler);

        var chunks = new List<AgentStreamChunk>();
        await foreach (var c in sut.SendTaskAsync("https://agent.example.com", "test-token", MakeRequest(), CancellationToken.None))
            chunks.Add(c);

        Assert.Equal(3, chunks.Count);
        Assert.Equal("thinking", chunks[0].Type);
        Assert.Equal("Planning...", chunks[0].Content);
        Assert.Equal("final_response", chunks[1].Type);
        Assert.Equal("The answer", chunks[1].Content);
        Assert.Equal("done", chunks[2].Type);
    }

    [Fact]
    public async Task SendTaskAsync_StopsOnDoneChunk()
    {
        var sse = """
            data: {"type":"done"}
            
            data: {"type":"thinking","content":"Should not be seen"}
            
            """;
        var handler = new FakeHandler(sse, "text/event-stream");
        var sut = CreateClient(handler);

        var chunks = new List<AgentStreamChunk>();
        await foreach (var c in sut.SendTaskAsync("https://agent.example.com", null, MakeRequest(), CancellationToken.None))
            chunks.Add(c);

        Assert.Single(chunks);
        Assert.Equal("done", chunks[0].Type);
    }

    [Fact]
    public async Task SendTaskAsync_SkipsMalformedLines()
    {
        var sse = """
            data: not-json
            
            data: {"type":"done"}
            
            """;
        var handler = new FakeHandler(sse, "text/event-stream");
        var sut = CreateClient(handler);

        var chunks = new List<AgentStreamChunk>();
        await foreach (var c in sut.SendTaskAsync("https://agent.example.com", null, MakeRequest(), CancellationToken.None))
            chunks.Add(c);

        // Only the valid "done" chunk should be parsed
        Assert.Single(chunks);
        Assert.Equal("done", chunks[0].Type);
    }

    // ── SendTaskAsync — Auth headers ──────────────────────────────────────────

    [Fact]
    public async Task SendTaskAsync_SetsBearerAuth()
    {
        var sse = "data: {\"type\":\"done\"}\n\n";
        var handler = new FakeHandler(sse, "text/event-stream");
        var sut = CreateClient(handler);

        await foreach (var _ in sut.SendTaskAsync("https://agent.example.com", "my-bearer-token", MakeRequest(), CancellationToken.None))
        { }

        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization?.Scheme);
        Assert.Equal("my-bearer-token", handler.LastRequest.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task SendTaskAsync_SetsApiKeyHeader()
    {
        var sse = "data: {\"type\":\"done\"}\n\n";
        var handler = new FakeHandler(sse, "text/event-stream");
        var sut = CreateClient(handler);

        await foreach (var _ in sut.SendTaskAsync("https://agent.example.com", "key-123", MakeRequest(), CancellationToken.None, authScheme: "ApiKey"))
        { }

        Assert.True(handler.LastRequest!.Headers.Contains("X-API-Key"));
        Assert.Equal("key-123", handler.LastRequest.Headers.GetValues("X-API-Key").First());
    }

    [Fact]
    public async Task SendTaskAsync_SetsCustomHeader()
    {
        var sse = "data: {\"type\":\"done\"}\n\n";
        var handler = new FakeHandler(sse, "text/event-stream");
        var sut = CreateClient(handler);

        await foreach (var _ in sut.SendTaskAsync("https://agent.example.com", "custom-val", MakeRequest(), CancellationToken.None, authScheme: "Custom", customHeaderName: "X-My-Header"))
        { }

        Assert.True(handler.LastRequest!.Headers.Contains("X-My-Header"));
        Assert.Equal("custom-val", handler.LastRequest.Headers.GetValues("X-My-Header").First());
    }

    // ── SendTaskAsync — Delegation depth ──────────────────────────────────────

    [Fact]
    public async Task SendTaskAsync_IncrementsDepthHeader()
    {
        var sse = "data: {\"type\":\"done\"}\n\n";
        var handler = new FakeHandler(sse, "text/event-stream");
        var sut = CreateClient(handler);

        await foreach (var _ in sut.SendTaskAsync("https://agent.example.com", null, MakeRequest(depth: 2), CancellationToken.None))
        { }

        Assert.True(handler.LastRequest!.Headers.Contains("X-A2A-Depth"));
        Assert.Equal("3", handler.LastRequest.Headers.GetValues("X-A2A-Depth").First());
    }

    [Fact]
    public async Task SendTaskAsync_DefaultsDepthToOne_WhenNotInMetadata()
    {
        var sse = "data: {\"type\":\"done\"}\n\n";
        var handler = new FakeHandler(sse, "text/event-stream");
        var sut = CreateClient(handler);

        var request = new AgentRequest { Query = "hi" };

        await foreach (var _ in sut.SendTaskAsync("https://agent.example.com", null, request, CancellationToken.None))
        { }

        Assert.Equal("1", handler.LastRequest!.Headers.GetValues("X-A2A-Depth").First());
    }

    // ── Helper: fake HTTP handler ─────────────────────────────────────────────

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        private readonly string _contentType;
        private readonly HttpStatusCode _statusCode;

        public HttpRequestMessage? LastRequest { get; private set; }
        public Uri? LastRequestUri { get; private set; }

        public FakeHandler(string responseBody, string contentType, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseBody = responseBody;
            _contentType = contentType;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestUri = request.RequestUri;

            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, _contentType),
            };
            return Task.FromResult(response);
        }
    }
}
