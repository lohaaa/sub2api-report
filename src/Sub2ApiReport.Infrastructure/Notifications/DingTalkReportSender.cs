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

internal sealed class DingTalkReportSender(IHttpClientFactory httpClientFactory, TimeProvider timeProvider)
    : WebhookReportSender(httpClientFactory, timeProvider)
{
    private const int MarkdownByteBudget = 16_000;
    private const string WebhookHost = "oapi.dingtalk.com";
    private static readonly JsonSerializerOptions SerializerOptions = new();

    public override NotificationChannelType ChannelType => NotificationChannelType.DingTalk;

    protected override int ContentByteBudget => MarkdownByteBudget;

    protected override string BuildRequestUrl(
        WebhookDeliveryOptions options,
        DateTimeOffset now)
    {
        var timestamp = now.ToUnixTimeMilliseconds();
        var stringToSign = string.Create(CultureInfo.InvariantCulture, $"{timestamp}\n{options.SignSecret}");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(options.SignSecret));
        var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{options.Url}&timestamp={timestamp}&sign={Uri.EscapeDataString(signature)}");
    }

    protected override IReadOnlyDictionary<string, string> BuildHeaders(
        WebhookDeliveryOptions options,
        DateTimeOffset now) => new Dictionary<string, string>();

    protected override byte[] BuildRequestBody(
        WebhookDeliveryOptions options,
        OutboundPart part,
        DateTimeOffset now) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new DingTalkEnvelope("markdown", new DingTalkMarkdown(part.Subject, part.Body)),
            SerializerOptions);

    protected override string? ParseBusinessError(string responseBody)
    {
        var envelope = JsonSerializer.Deserialize<DingTalkResponse>(responseBody)
            ?? throw new JsonException();
        return envelope.ErrCode == 0
            ? null
            : string.Create(
                CultureInfo.InvariantCulture,
                $"钉钉返回错误码 {envelope.ErrCode}（{envelope.ErrMsg ?? "无描述"}）");
    }

    protected override bool IsAllowedWebhook(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Host, WebhookHost, StringComparison.OrdinalIgnoreCase);

    [method: JsonConstructor]
    internal sealed record DingTalkEnvelope(
        [property: JsonPropertyName("msgtype")] string MsgType,
        [property: JsonPropertyName("markdown")] DingTalkMarkdown Markdown);

    [method: JsonConstructor]
    internal sealed record DingTalkMarkdown(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("text")] string Text);

    [method: JsonConstructor]
    private sealed record DingTalkResponse(
        [property: JsonPropertyName("errcode")] int ErrCode,
        [property: JsonPropertyName("errmsg")] string? ErrMsg);
}
