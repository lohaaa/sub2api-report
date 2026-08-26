using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Sub2ApiReport.Application.Sub2Api;

namespace Sub2ApiReport.Infrastructure.Sub2Api;

public sealed class Sub2ApiClient(HttpClient httpClient, TimeProvider timeProvider) : ISub2ApiClient
{
    private const int PageSize = 100;
    private const int MaximumPages = 100;
    private const long MaximumResponseBytes = 10 * 1024 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    public async Task<Sub2ApiConnectionProbe> TestAsync(
        Sub2ApiConnectionCredentials connection,
        CancellationToken cancellationToken)
    {
        var page = await GetPageAsync(connection, 1, 1, cancellationToken);
        return new Sub2ApiConnectionProbe(page.Total);
    }

    public async Task<IReadOnlyList<Sub2ApiExternalKey>> GetApiKeysAsync(
        Sub2ApiConnectionCredentials connection,
        CancellationToken cancellationToken)
    {
        var keys = new Dictionary<long, Sub2ApiExternalKey>();
        for (var pageNumber = 1; pageNumber <= MaximumPages; pageNumber++)
        {
            var page = await GetPageAsync(connection, pageNumber, PageSize, cancellationToken);
            foreach (var key in page.Items)
            {
                if (key.Id <= 0 || string.IsNullOrWhiteSpace(key.Name) || string.IsNullOrWhiteSpace(key.Status))
                {
                    throw InvalidResponse();
                }

                if (connection.CodexGroupId is not null && key.GroupId != connection.CodexGroupId)
                {
                    continue;
                }

                if (!keys.TryAdd(key.Id, new Sub2ApiExternalKey(
                    key.Id,
                    key.Name.Trim(),
                    key.Status.Trim(),
                    key.GroupId,
                    key.LastUsedAt,
                    key.CreatedAt,
                    key.UpdatedAt)))
                {
                    throw InvalidResponse();
                }
            }

            if (page.Page >= page.Pages)
            {
                return keys.Values.OrderBy(key => key.ExternalId).ToArray();
            }

            if (page.Page != pageNumber || page.Pages < page.Page || page.Pages > MaximumPages)
            {
                throw InvalidResponse();
            }
        }

        throw new Sub2ApiClientException(
            Sub2ApiFailureKind.InvalidResponse,
            "Sub2API returned more pages than the configured safety limit.");
    }

    private async Task<UpstreamPage> GetPageAsync(
        Sub2ApiConnectionCredentials connection,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var path = $"/api/v1/admin/users/{connection.UserId}/api-keys?page={page}&page_size={pageSize}&sort_by=id&sort_order=asc";
        var endpoint = new Uri($"{connection.BaseUrl.TrimEnd('/')}{path}", UriKind.Absolute);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.TryAddWithoutValidation("x-api-key", connection.AdminApiKey);
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(RequestTimeout);

            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutSource.Token);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new Sub2ApiClientException(
                    Sub2ApiFailureKind.Timeout,
                    "The Sub2API request timed out.",
                    innerException: exception);
            }
            catch (HttpRequestException exception)
            {
                if (attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), timeProvider, cancellationToken);
                    continue;
                }

                throw new Sub2ApiClientException(
                    Sub2ApiFailureKind.Unavailable,
                    "Sub2API could not be reached.",
                    innerException: exception);
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var retryAfter = GetRetryAfter(response);
                    if (attempt < 3)
                    {
                        await Task.Delay(retryAfter, timeProvider, cancellationToken);
                        continue;
                    }

                    throw new Sub2ApiClientException(
                        Sub2ApiFailureKind.RateLimited,
                        "Sub2API rate limited the request.",
                        retryAfter);
                }

                if ((int)response.StatusCode >= 500)
                {
                    if (attempt < 3)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), timeProvider, cancellationToken);
                        continue;
                    }

                    throw new Sub2ApiClientException(
                        Sub2ApiFailureKind.Unavailable,
                        "Sub2API returned a server error.");
                }

                EnsureSuccessfulStatus(response.StatusCode);
                try
                {
                    await response.Content.LoadIntoBufferAsync(MaximumResponseBytes, cancellationToken);
                    var envelope = await response.Content.ReadFromJsonAsync<UpstreamEnvelope>(
                        cancellationToken: cancellationToken);
                    if (envelope?.Code != 0 || envelope.Data is null)
                    {
                        throw InvalidResponse();
                    }

                    return envelope.Data;
                }
                catch (Sub2ApiClientException)
                {
                    throw;
                }
                catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or global::System.Text.Json.JsonException)
                {
                    throw new Sub2ApiClientException(
                        Sub2ApiFailureKind.InvalidResponse,
                        "Sub2API returned an invalid response.",
                        innerException: exception);
                }
            }
        }

        throw new InvalidOperationException("The retry loop exited unexpectedly.");
    }

    private static void EnsureSuccessfulStatus(HttpStatusCode statusCode)
    {
        var failure = statusCode switch
        {
            HttpStatusCode.Unauthorized => Sub2ApiFailureKind.Unauthorized,
            HttpStatusCode.Forbidden => Sub2ApiFailureKind.Forbidden,
            HttpStatusCode.NotFound => Sub2ApiFailureKind.Incompatible,
            _ when (int)statusCode is < 200 or >= 300 => Sub2ApiFailureKind.Unavailable,
            _ => (Sub2ApiFailureKind?)null,
        };
        if (failure is not null)
        {
            throw new Sub2ApiClientException(failure.Value, "Sub2API rejected the request.");
        }
    }

    private static TimeSpan GetRetryAfter(HttpResponseMessage response)
    {
        var value = response.Headers.RetryAfter?.Delta;
        if (value is null && response.Headers.RetryAfter?.Date is { } date)
        {
            value = date - DateTimeOffset.UtcNow;
        }

        return value is { } delay && delay > TimeSpan.Zero
            ? TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds, 5000))
            : TimeSpan.FromMilliseconds(200);
    }

    private static Sub2ApiClientException InvalidResponse() => new(
        Sub2ApiFailureKind.InvalidResponse,
        "Sub2API returned an invalid response.");

    private sealed record UpstreamEnvelope(
        [property: JsonPropertyName("code")] int Code,
        [property: JsonPropertyName("data")] UpstreamPage? Data);

    private sealed record UpstreamPage(
        [property: JsonPropertyName("items")] IReadOnlyList<UpstreamApiKey> Items,
        [property: JsonPropertyName("total")] long Total,
        [property: JsonPropertyName("page")] int Page,
        [property: JsonPropertyName("page_size")] int PageSize,
        [property: JsonPropertyName("pages")] int Pages);

    private sealed record UpstreamApiKey(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("group_id")] long? GroupId,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("last_used_at")] DateTimeOffset? LastUsedAt,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);
}
