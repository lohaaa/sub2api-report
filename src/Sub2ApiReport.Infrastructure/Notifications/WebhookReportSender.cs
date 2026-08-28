using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sub2ApiReport.Application.Notifications;
using Sub2ApiReport.Application.Reports;
using Sub2ApiReport.Domain.Notifications;
using Sub2ApiReport.Domain.Reports;

namespace Sub2ApiReport.Infrastructure.Notifications;

/// <summary>Shared retry/verification pipeline for HTTPS webhook channels.</summary>
internal abstract class WebhookReportSender(IHttpClientFactory httpClientFactory, TimeProvider timeProvider)
    : IReportSender
{
    internal const string HttpClientName = "notifications-webhook";

    private const int MaximumAttempts = 3;
    private const int MaximumResponseBytes = 64 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    public abstract NotificationChannelType ChannelType { get; }

    /// <summary>UTF-8 byte budget for the rendered text content of one part.</summary>
    protected abstract int ContentByteBudget { get; }

    /// <summary>Builds the channel-specific rows that are kept intact during message sharding.</summary>
    protected abstract IReadOnlyList<string> BuildContentLines(
        ReportDocument report,
        ChannelDeliveryContext context);

    public IReadOnlyList<OutboundPart> Render(ReportDocument report, ChannelDeliveryContext context)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (context.Webhook is null)
        {
            throw new ArgumentException("The webhook channel context is required.", nameof(context));
        }

        var subject = ReportMessageRenderer.BuildSubject(report);
        var lines = BuildContentLines(report, context);
        var shards = ShardLines(lines, ContentByteBudget);
        return shards
            .Select((shard, index) =>
            {
                var title = shards.Count == 1
                    ? subject
                    : string.Create(CultureInfo.InvariantCulture, $"{subject}（{index + 1}/{shards.Count}）");
                var body = string.Join('\n', shard);
                return new OutboundPart(
                    index,
                    shards.Count,
                    title,
                    body,
                    null,
                    DeliveryPayloadHash.Compute(title, body, null));
            })
            .ToArray();
    }

    public async Task<ChannelSendOutcome> SendPartAsync(
        OutboundPart part,
        ChannelDeliveryContext context,
        CancellationToken cancellationToken)
    {
        if (context.Webhook is not { } webhook)
        {
            return ChannelSendOutcome.Fail("invalid_channel", "The webhook channel is not configured.");
        }

        if (!IsAllowedWebhook(webhook.Url))
        {
            return ChannelSendOutcome.Fail("invalid_webhook", null);
        }

        var now = timeProvider.GetUtcNow();
        var payload = BuildRequestBody(webhook, part, now);
        var url = BuildRequestUrl(webhook, now);
        var headers = BuildHeaders(webhook, now);
        return await SendAsync(url, headers, payload, cancellationToken);
    }

    public async Task<ChannelSendOutcome> SendTestAsync(
        ChannelDeliveryContext context,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var lines = new[]
        {
            "这是一条 Sub2API Report 渠道测试消息。",
            $"发送时间 {now:yyyy-MM-dd HH:mm:ss} UTC。",
            "消息内容为合成示例，不包含真实用量数据。",
        };
        var part = new OutboundPart(
            0,
            1,
            "[Sub2API Report] 渠道测试",
            string.Join('\n', lines),
            null,
            DeliveryPayloadHash.Compute("test", string.Join('\n', lines), null));
        return await SendPartAsync(part, context, cancellationToken);
    }

    /// <summary>Builds the final request URL; DingTalk appends timestamp and signature here.</summary>
    protected abstract string BuildRequestUrl(WebhookDeliveryOptions options, DateTimeOffset now);

    protected abstract IReadOnlyDictionary<string, string> BuildHeaders(
        WebhookDeliveryOptions options,
        DateTimeOffset now);

    protected abstract byte[] BuildRequestBody(
        WebhookDeliveryOptions options,
        OutboundPart part,
        DateTimeOffset now);

    /// <summary>Returns null when the business code marks success; otherwise a safe error description.</summary>
    protected abstract string? ParseBusinessError(string responseBody);

    protected abstract bool IsAllowedWebhook(string url);

    private async Task<ChannelSendOutcome> SendAsync(
        string url,
        IReadOnlyDictionary<string, string> headers,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        var httpClient = httpClientFactory.CreateClient(HttpClientName);
        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Accept.ParseAdd("application/json");
            foreach (var (name, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }

            request.Content = new ByteArrayContent(payload);
            request.Content.Headers.ContentType = new global::System.Net.Http.Headers.MediaTypeHeaderValue(
                "application/json");
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
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt < MaximumAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), timeProvider, cancellationToken);
                    continue;
                }

                return ChannelSendOutcome.Fail("timeout", "The webhook request timed out.");
            }
            catch (HttpRequestException)
            {
                if (attempt < MaximumAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), timeProvider, cancellationToken);
                    continue;
                }

                return ChannelSendOutcome.Fail("unavailable", "The webhook endpoint could not be reached.");
            }

            using (response)
            {
                if (response.StatusCode == global::System.Net.HttpStatusCode.TooManyRequests)
                {
                    if (attempt < MaximumAttempts)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(attempt), timeProvider, cancellationToken);
                        continue;
                    }

                    return ChannelSendOutcome.Fail("rate_limited", "The webhook endpoint rate limited the request.");
                }

                if ((int)response.StatusCode >= 500)
                {
                    if (attempt < MaximumAttempts)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), timeProvider, cancellationToken);
                        continue;
                    }

                    return ChannelSendOutcome.Fail("unavailable", "The webhook endpoint returned a server error.");
                }

                if (response.StatusCode is < global::System.Net.HttpStatusCode.OK
                    or > global::System.Net.HttpStatusCode.NoContent)
                {
                    return ChannelSendOutcome.Fail(
                        "rejected",
                        $"The webhook endpoint rejected the request with HTTP {(int)response.StatusCode}.");
                }

                string responseBody;
                try
                {
                    await response.Content.LoadIntoBufferAsync(MaximumResponseBytes, cancellationToken);
                    responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return ChannelSendOutcome.Fail("timeout", "Reading the webhook response timed out.");
                }
                catch (Exception exception)
                    when (exception is HttpRequestException or InvalidOperationException or IOException)
                {
                    return ChannelSendOutcome.Fail("invalid_response", "The webhook response could not be read.");
                }

                string? businessError;
                try
                {
                    businessError = ParseBusinessError(responseBody);
                }
                catch (global::System.Text.Json.JsonException)
                {
                    return ChannelSendOutcome.Fail("invalid_response", "The webhook returned an invalid response.");
                }

                return businessError is null
                    ? ChannelSendOutcome.Ok
                    : ChannelSendOutcome.Fail("business_error", businessError);
            }
        }

        return ChannelSendOutcome.Fail("unavailable", "The webhook retry loop exited unexpectedly.");
    }

    private static List<IReadOnlyList<string>> ShardLines(
        IReadOnlyList<string> lines,
        int byteBudget)
    {
        var shards = new List<IReadOnlyList<string>>();
        var current = new List<string>();
        var currentBytes = 0;
        foreach (var line in lines)
        {
            var lineBytes = Encoding.UTF8.GetByteCount(line) + 1;
            if (current.Count > 0 && currentBytes + lineBytes > byteBudget)
            {
                shards.Add(current);
                current = [];
                currentBytes = 0;
            }

            current.Add(line);
            currentBytes += lineBytes;
        }

        if (current.Count > 0)
        {
            shards.Add(current);
        }

        if (shards.Count == 0)
        {
            shards.Add([]);
        }

        return shards;
    }
}
