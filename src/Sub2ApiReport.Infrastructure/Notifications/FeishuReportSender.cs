using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sub2ApiReport.Application.Notifications;
using Sub2ApiReport.Application.Reports;
using Sub2ApiReport.Domain.Notifications;
using Sub2ApiReport.Domain.Reports;

namespace Sub2ApiReport.Infrastructure.Notifications;

internal sealed class FeishuReportSender(IHttpClientFactory httpClientFactory, TimeProvider timeProvider)
    : WebhookReportSender(httpClientFactory, timeProvider)
{
    private const int PostByteBudget = 12_000;
    private const string WebhookHost = "open.feishu.cn";
    private static readonly JsonSerializerOptions SerializerOptions = new();

    public override NotificationChannelType ChannelType => NotificationChannelType.Feishu;

    protected override int ContentByteBudget => PostByteBudget;

    protected override string BuildRequestUrl(
        WebhookDeliveryOptions options,
        DateTimeOffset now) => options.Url;

    protected override IReadOnlyDictionary<string, string> BuildHeaders(
        WebhookDeliveryOptions options,
        DateTimeOffset now)
    {
        var timestamp = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        return new Dictionary<string, string>
        {
            ["X-Lark-Signature"] = ComputeSignature(timestamp, options.SignSecret),
            ["X-Lark-Request-Timestamp"] = timestamp,
        };
    }

    protected override byte[] BuildRequestBody(
        WebhookDeliveryOptions options,
        OutboundPart part,
        DateTimeOffset now)
    {
        var timestamp = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var rows = part.Body
            .Split('\n')
            .Select(line => (IReadOnlyList<PostText>)[new PostText("text", line)])
            .ToArray();
        var envelope = new PostMessage(
            timestamp,
            ComputeSignature(timestamp, options.SignSecret),
            "post",
            new PostContent(new PostPost(new PostLocale(part.Subject, rows))));
        return JsonSerializer.SerializeToUtf8Bytes(envelope, SerializerOptions);
    }

    protected override string? ParseBusinessError(string responseBody)
    {
        var response = JsonSerializer.Deserialize<FeishuResponse>(responseBody)
            ?? throw new JsonException();
        var code = response.Code ?? response.StatusCode;
        if (code is null || code.Value == 0)
        {
            return null;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"飞书返回错误码 {code.Value}（{response.Message ?? response.StatusMessage ?? "无描述"}）");
    }

    protected override bool IsAllowedWebhook(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Host, WebhookHost, StringComparison.OrdinalIgnoreCase);

    private static string ComputeSignature(string timestamp, string signSecret)
    {
        // Feishu custom bots sign the empty message using "timestamp\nsecret" as the HMAC key.
        var key = Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{timestamp}\n{signSecret}"));
        using var hmac = new HMACSHA256(key);
        return Convert.ToBase64String(hmac.ComputeHash([]));
    }

    [method: JsonConstructor]
    internal sealed record PostMessage(
        [property: JsonPropertyName("timestamp")] string Timestamp,
        [property: JsonPropertyName("sign")] string Sign,
        [property: JsonPropertyName("msg_type")] string MsgType,
        [property: JsonPropertyName("content")] PostContent Content);

    [method: JsonConstructor]
    internal sealed record PostContent(
        [property: JsonPropertyName("post")] PostPost Post);

    [method: JsonConstructor]
    internal sealed record PostPost(
        [property: JsonPropertyName("zh_cn")] PostLocale ZhCn);

    [method: JsonConstructor]
    internal sealed record PostLocale(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("content")] IReadOnlyList<IReadOnlyList<PostText>> Content);

    [method: JsonConstructor]
    internal sealed record PostText(
        [property: JsonPropertyName("tag")] string Tag,
        [property: JsonPropertyName("text")] string Text);

    [method: JsonConstructor]
    private sealed record FeishuResponse(
        [property: JsonPropertyName("code")] int? Code,
        [property: JsonPropertyName("msg")] string? Message,
        [property: JsonPropertyName("StatusCode")] int? StatusCode,
        [property: JsonPropertyName("StatusMessage")] string? StatusMessage);
}
