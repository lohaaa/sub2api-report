using System.Net;
using System.Text;
using Sub2ApiReport.Application.Sub2Api;
using Sub2ApiReport.Infrastructure.Sub2Api;

namespace Sub2ApiReport.IntegrationTests;

public sealed class Sub2ApiClientTests
{
    private static readonly Sub2ApiConnectionCredentials Connection = new(
        "https://sub2api.example.com",
        "synthetic-admin-key",
        42,
        7,
        1);

    [Fact]
    public async Task ClientReadsAllPagesFiltersGroupAndIgnoresSecretAndUnknownFields()
    {
        var handler = new StubHandler((request, call, _) =>
        {
            Assert.Equal("synthetic-admin-key", request.Headers.GetValues("x-api-key").Single());
            return Task.FromResult(Json(call == 1
                ? PageJson(1, 2, """
                    {"id":101,"key":"business-secret-one","name":"Alpha","status":"active","group_id":7,"last_used_at":null,"created_at":"2026-08-01T00:00:00Z","updated_at":"2026-08-01T00:00:00Z","future_field":{"nested":true}},
                    {"id":102,"key":"business-secret-two","name":"Other group","status":"active","group_id":8,"last_used_at":null,"created_at":"2026-08-01T00:00:00Z","updated_at":"2026-08-01T00:00:00Z"}
                    """)
                : PageJson(2, 2, """
                    {"id":103,"key":"business-secret-three","name":"Beta","status":"inactive","group_id":7,"last_used_at":"2026-08-25T01:02:03Z","created_at":"2026-08-02T00:00:00Z","updated_at":"2026-08-25T01:02:03Z"}
                    """)));
        });
        var client = CreateClient(handler);

        var keys = await client.GetApiKeysAsync(Connection, CancellationToken.None);

        Assert.Equal([101, 103], keys.Select(key => key.ExternalId));
        Assert.Equal("Beta", keys[1].Name);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task ClientAcceptsAnEmptyPage()
    {
        var client = CreateClient(new StubHandler((_, _, _) =>
            Task.FromResult(Json(PageJson(1, 1, string.Empty, total: 0)))));

        var keys = await client.GetApiKeysAsync(Connection, CancellationToken.None);

        Assert.Empty(keys);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, Sub2ApiFailureKind.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, Sub2ApiFailureKind.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, Sub2ApiFailureKind.Incompatible)]
    public async Task ClientClassifiesNonRetryableStatusCodes(
        HttpStatusCode statusCode,
        Sub2ApiFailureKind expectedKind)
    {
        var client = CreateClient(new StubHandler((_, _, _) =>
            Task.FromResult(new HttpResponseMessage(statusCode))));

        var exception = await Assert.ThrowsAsync<Sub2ApiClientException>(() =>
            client.TestAsync(Connection, CancellationToken.None));

        Assert.Equal(expectedKind, exception.Kind);
    }

    [Fact]
    public async Task ClientHonorsRateLimitRetriesWithoutExposingResponseBody()
    {
        var handler = new StubHandler((_, _, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("synthetic-sensitive-upstream-body"),
            };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
            return Task.FromResult(response);
        });
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<Sub2ApiClientException>(() =>
            client.TestAsync(Connection, CancellationToken.None));

        Assert.Equal(Sub2ApiFailureKind.RateLimited, exception.Kind);
        Assert.Equal(3, handler.CallCount);
        Assert.DoesNotContain("synthetic-sensitive", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClientRetriesServerErrorsThenFailsAsUnavailable()
    {
        var handler = new StubHandler((_, _, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<Sub2ApiClientException>(() =>
            client.TestAsync(Connection, CancellationToken.None));

        Assert.Equal(Sub2ApiFailureKind.Unavailable, exception.Kind);
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task ClientClassifiesTimeouts()
    {
        var handler = new StubHandler((_, _, _) =>
            throw new TaskCanceledException("synthetic timeout"));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<Sub2ApiClientException>(() =>
            client.TestAsync(Connection, CancellationToken.None));

        Assert.Equal(Sub2ApiFailureKind.Timeout, exception.Kind);
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task ClientReadsUsageStatsWithClosedNaturalDayRange()
    {
        Uri? requestedUri = null;
        var handler = new StubHandler((request, _, _) =>
        {
            requestedUri = request.RequestUri;
            return Task.FromResult(Json("""
                {
                  "code": 0,
                  "data": {
                    "total_requests": 12,
                    "total_input_tokens": 100,
                    "total_output_tokens": 50,
                    "total_cache_tokens": 25,
                    "total_cache_creation_tokens": 10,
                    "total_cache_read_tokens": 15,
                    "total_tokens": 175,
                    "total_cost": 1.234567890123456789,
                    "total_actual_cost": 0.987654321098765432,
                    "average_duration_ms": 123.45,
                    "future_field": true
                  }
                }
                """));
        });
        var client = CreateClient(handler);

        var stats = await client.GetUsageStatsAsync(
            Connection,
            101,
            new DateOnly(2026, 7, 2),
            new DateOnly(2026, 7, 31),
            "Asia/Shanghai",
            CancellationToken.None);

        Assert.NotNull(requestedUri);
        Assert.Equal("/api/v1/admin/usage/stats", requestedUri.AbsolutePath);
        Assert.Contains("user_id=42", requestedUri.Query, StringComparison.Ordinal);
        Assert.Contains("api_key_id=101", requestedUri.Query, StringComparison.Ordinal);
        Assert.Contains("group_id=7", requestedUri.Query, StringComparison.Ordinal);
        Assert.Contains("start_date=2026-07-02", requestedUri.Query, StringComparison.Ordinal);
        Assert.Contains("end_date=2026-07-31", requestedUri.Query, StringComparison.Ordinal);
        Assert.Contains("timezone=Asia%2FShanghai", requestedUri.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nocache=true", requestedUri.Query, StringComparison.Ordinal);
        Assert.Equal(12, stats.TotalRequests);
        Assert.Equal(175, stats.TotalTokens);
        Assert.Equal(1.234567890123456789m, stats.TotalCost);
        Assert.Equal(0.987654321098765432m, stats.TotalActualCost);
    }

    [Fact]
    public async Task ClientRejectsNegativeUsageStats()
    {
        var client = CreateClient(new StubHandler((_, _, _) => Task.FromResult(Json("""
            {
              "code": 0,
              "data": {
                "total_requests": -1,
                "total_input_tokens": 0,
                "total_output_tokens": 0,
                "total_cache_tokens": 0,
                "total_cache_creation_tokens": 0,
                "total_cache_read_tokens": 0,
                "total_tokens": 0,
                "total_cost": 0,
                "total_actual_cost": 0,
                "average_duration_ms": 0
              }
            }
            """))));

        var exception = await Assert.ThrowsAsync<Sub2ApiClientException>(() =>
            client.GetUsageStatsAsync(
                Connection,
                101,
                new DateOnly(2026, 7, 2),
                new DateOnly(2026, 7, 31),
                "Asia/Shanghai",
                CancellationToken.None));

        Assert.Equal(Sub2ApiFailureKind.InvalidResponse, exception.Kind);
    }

    [Fact]
    public async Task ClientRejectsMalformedJson()
    {
        var client = CreateClient(new StubHandler((_, _, _) => Task.FromResult(Json("{not-json"))));

        var exception = await Assert.ThrowsAsync<Sub2ApiClientException>(() =>
            client.TestAsync(Connection, CancellationToken.None));

        Assert.Equal(Sub2ApiFailureKind.InvalidResponse, exception.Kind);
    }

    private static Sub2ApiClient CreateClient(HttpMessageHandler handler) => new(
        new HttpClient(handler),
        TimeProvider.System);

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static string PageJson(int page, int pages, string items, int total = 3) => $$"""
        {
          "code": 0,
          "message": "success",
          "data": {
            "items": [{{items}}],
            "total": {{total}},
            "page": {{page}},
            "page_size": 100,
            "pages": {{pages}}
          }
        }
        """;

    private sealed class StubHandler(
        Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return handler(request, CallCount, cancellationToken);
        }
    }
}
