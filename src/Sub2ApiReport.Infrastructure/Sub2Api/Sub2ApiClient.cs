using System.Globalization;
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
        var page = await GetUserPageAsync(connection, 1, 1, cancellationToken);
        return new Sub2ApiConnectionProbe(page.Total);
    }

    public async Task<IReadOnlyList<Sub2ApiExternalUser>> GetUsersAsync(
        Sub2ApiConnectionCredentials connection,
        CancellationToken cancellationToken)
    {
        var users = new Dictionary<long, Sub2ApiExternalUser>();
        for (var pageNumber = 1; pageNumber <= MaximumPages; pageNumber++)
        {
            var page = await GetUserPageAsync(connection, pageNumber, PageSize, cancellationToken);
            foreach (var user in page.Items)
            {
                if (user.Id <= 0 || string.IsNullOrWhiteSpace(user.Email) || string.IsNullOrWhiteSpace(user.Status))
                {
                    throw InvalidResponse();
                }

                users.TryAdd(user.Id, new Sub2ApiExternalUser(
                    user.Id,
                    user.Email.Trim(),
                    string.IsNullOrWhiteSpace(user.Username) ? null : user.Username.Trim(),
                    user.Status.Trim()));
            }

            if (page.Page >= page.Pages)
            {
                return users.Values.OrderBy(user => user.ExternalId).ToArray();
            }
        }

        throw InvalidResponse();
    }

    public async Task<IReadOnlyList<Sub2ApiExternalKey>> GetApiKeysAsync(
        Sub2ApiConnectionCredentials connection,
        long externalUserId,
        CancellationToken cancellationToken)
    {
        var keys = new Dictionary<long, Sub2ApiExternalKey>();
        for (var pageNumber = 1; pageNumber <= MaximumPages; pageNumber++)
        {
            var page = await GetPageAsync(connection, externalUserId, pageNumber, PageSize, cancellationToken);
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
                    key.LastUsedAt)))
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

    public async Task<Sub2ApiUsageStats> GetUsageStatsAsync(
        Sub2ApiConnectionCredentials connection,
        long externalUserId,
        long externalApiKeyId,
        DateOnly startDate,
        DateOnly endDate,
        string timezone,
        CancellationToken cancellationToken)
    {
        if (externalApiKeyId <= 0 || endDate < startDate)
        {
            throw new ArgumentException("The usage statistics range is invalid.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(timezone);
        var groupQuery = connection.CodexGroupId is { } groupId
            ? string.Create(CultureInfo.InvariantCulture, $"&group_id={groupId}")
            : string.Empty;
        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"/api/v1/admin/usage/stats?user_id={externalUserId}&api_key_id={externalApiKeyId}{groupQuery}&start_date={startDate:yyyy-MM-dd}&end_date={endDate:yyyy-MM-dd}&timezone={Uri.EscapeDataString(timezone.Trim())}&nocache=true");
        var endpoint = new Uri($"{connection.BaseUrl.TrimEnd('/')}{path}", UriKind.Absolute);
        var stats = await GetDataAsync<UpstreamUsageStats>(connection, endpoint, cancellationToken);
        if (stats.TotalRequests < 0
            || stats.TotalInputTokens < 0
            || stats.TotalOutputTokens < 0
            || stats.TotalCacheTokens < 0
            || stats.TotalCacheCreationTokens < 0
            || stats.TotalCacheReadTokens < 0
            || stats.TotalTokens < 0
            || stats.TotalCost < 0
            || stats.TotalActualCost < 0
            || stats.AverageDurationMs < 0)
        {
            throw InvalidResponse();
        }

        return new Sub2ApiUsageStats(
            stats.TotalRequests,
            stats.TotalInputTokens,
            stats.TotalOutputTokens,
            stats.TotalCacheTokens,
            stats.TotalCacheCreationTokens,
            stats.TotalCacheReadTokens,
            stats.TotalTokens,
            stats.TotalCost,
            stats.TotalActualCost,
            stats.AverageDurationMs);
    }

    private Task<UpstreamPage> GetPageAsync(
        Sub2ApiConnectionCredentials connection,
        long externalUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var path = $"/api/v1/admin/users/{externalUserId}/api-keys?page={page}&page_size={pageSize}&sort_by=id&sort_order=asc";
        var endpoint = new Uri($"{connection.BaseUrl.TrimEnd('/')}{path}", UriKind.Absolute);
        return GetDataAsync<UpstreamPage>(connection, endpoint, cancellationToken);
    }

    private Task<UpstreamUserPage> GetUserPageAsync(
        Sub2ApiConnectionCredentials connection,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var path = $"/api/v1/admin/users?page={page}&page_size={pageSize}&sort_by=id&sort_order=asc";
        var endpoint = new Uri($"{connection.BaseUrl.TrimEnd('/')}{path}", UriKind.Absolute);
        return GetDataAsync<UpstreamUserPage>(connection, endpoint, cancellationToken);
    }

    private async Task<T> GetDataAsync<T>(
        Sub2ApiConnectionCredentials connection,
        Uri endpoint,
        CancellationToken cancellationToken)
        where T : class
    {
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
                if (attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), timeProvider, cancellationToken);
                    continue;
                }

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
                    var envelope = await response.Content.ReadFromJsonAsync<UpstreamEnvelope<T>>(
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

    private TimeSpan GetRetryAfter(HttpResponseMessage response)
    {
        var value = response.Headers.RetryAfter?.Delta;
        if (value is null && response.Headers.RetryAfter?.Date is { } date)
        {
            value = date - timeProvider.GetUtcNow();
        }

        return value is { } delay && delay > TimeSpan.Zero
            ? TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds, 5000))
            : TimeSpan.FromMilliseconds(200);
    }

    private static Sub2ApiClientException InvalidResponse() => new(
        Sub2ApiFailureKind.InvalidResponse,
        "Sub2API returned an invalid response.");

    private sealed record UpstreamEnvelope<T>(
        [property: JsonPropertyName("code")] int Code,
        [property: JsonPropertyName("data")] T? Data)
        where T : class;

    [method: JsonConstructor]
    private sealed record UpstreamPage(
        [property: JsonPropertyName("items")] IReadOnlyList<UpstreamApiKey> Items,
        [property: JsonPropertyName("total")] long Total,
        [property: JsonPropertyName("page")] int Page,
        [property: JsonPropertyName("pages")] int Pages);

    [method: JsonConstructor]
    private sealed record UpstreamUserPage(
        [property: JsonPropertyName("items")] IReadOnlyList<UpstreamUser> Items,
        [property: JsonPropertyName("total")] long Total,
        [property: JsonPropertyName("page")] int Page,
        [property: JsonPropertyName("pages")] int Pages);

    [method: JsonConstructor]
    private sealed record UpstreamUser(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("username")] string? Username,
        [property: JsonPropertyName("status")] string Status);

    [method: JsonConstructor]
    private sealed record UpstreamUsageStats(
        [property: JsonPropertyName("total_requests")] long TotalRequests,
        [property: JsonPropertyName("total_input_tokens")] long TotalInputTokens,
        [property: JsonPropertyName("total_output_tokens")] long TotalOutputTokens,
        [property: JsonPropertyName("total_cache_tokens")] long TotalCacheTokens,
        [property: JsonPropertyName("total_cache_creation_tokens")] long TotalCacheCreationTokens,
        [property: JsonPropertyName("total_cache_read_tokens")] long TotalCacheReadTokens,
        [property: JsonPropertyName("total_tokens")] long TotalTokens,
        [property: JsonPropertyName("total_cost")] decimal TotalCost,
        [property: JsonPropertyName("total_actual_cost")] decimal TotalActualCost,
        [property: JsonPropertyName("average_duration_ms")] decimal AverageDurationMs);

    [method: JsonConstructor]
    private sealed record UpstreamApiKey(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("group_id")] long? GroupId,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("last_used_at")] DateTimeOffset? LastUsedAt);
}
