using System.Globalization;
using System.Text;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Sub2ApiReport.Application.Notifications;
using Sub2ApiReport.Application.Reports;
using Sub2ApiReport.Domain.Notifications;
using Sub2ApiReport.Domain.Reports;
using Sub2ApiReport.Infrastructure.Reports;

namespace Sub2ApiReport.Infrastructure.Notifications;

internal sealed class EmailReportSender(TimeProvider timeProvider) : IReportSender
{
    private const int SmtpTimeoutMilliseconds = 15_000;
    private const string CsvAttachmentName = "sub2api-report.csv";

    public NotificationChannelType ChannelType => NotificationChannelType.Email;

    public IReadOnlyList<OutboundPart> Render(ReportDocument report, ChannelDeliveryContext context)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (context.Email is null)
        {
            throw new ArgumentException("The email channel context is required.", nameof(context));
        }

        var subject = ReportMessageRenderer.BuildSubject(report);
        var body = ReportMessageRenderer.BuildHtmlBody(report);
        var csv = Encoding.UTF8.GetString(ReportCsvSerializer.Serialize(report));
        return
        [
            new OutboundPart(
                0,
                1,
                subject,
                body,
                csv,
                DeliveryPayloadHash.Compute(subject, body, csv)),
        ];
    }

    public async Task<ChannelSendOutcome> SendPartAsync(
        OutboundPart part,
        ChannelDeliveryContext context,
        CancellationToken cancellationToken)
    {
        if (context.Email is not { } email)
        {
            return ChannelSendOutcome.Fail("invalid_channel", "The email channel is not configured.");
        }

        var message = BuildMessage(part, email);
        try
        {
            using var client = new SmtpClient();
            client.Timeout = SmtpTimeoutMilliseconds;
            var secureSocket = email.Security switch
            {
                SmtpSecurityMode.ImplicitTls => SecureSocketOptions.SslOnConnect,
                SmtpSecurityMode.None => SecureSocketOptions.None,
                _ => SecureSocketOptions.StartTls,
            };
            await client.ConnectAsync(email.Host, email.Port, secureSocket, cancellationToken);
            try
            {
                if (!string.IsNullOrWhiteSpace(email.Username) && email.Password is { } password)
                {
                    await client.AuthenticateAsync(email.Username, password, cancellationToken);
                }

                await client.SendAsync(message, cancellationToken);
                await client.DisconnectAsync(true, cancellationToken);
                return ChannelSendOutcome.Ok;
            }
            finally
            {
                if (client.IsConnected)
                {
                    await client.DisconnectAsync(true, CancellationToken.None);
                }
            }
        }
        catch (AuthenticationException exception)
        {
            return ChannelSendOutcome.Fail("smtp_auth_failed", exception.Message);
        }
        catch (ServiceNotAuthenticatedException exception)
        {
            return ChannelSendOutcome.Fail("smtp_auth_failed", exception.Message);
        }
        catch (SslHandshakeException exception)
        {
            return ChannelSendOutcome.Fail("smtp_connect_failed", exception.Message);
        }
        catch (ServiceNotConnectedException exception)
        {
            return ChannelSendOutcome.Fail("smtp_connect_failed", exception.Message);
        }
        catch (ProtocolException exception)
        {
            return ChannelSendOutcome.Fail("smtp_send_failed", exception.Message);
        }
        catch (global::System.Net.Sockets.SocketException exception)
        {
            return ChannelSendOutcome.Fail("smtp_connect_failed", exception.Message);
        }
        catch (global::System.IO.IOException exception)
        {
            return ChannelSendOutcome.Fail("smtp_connect_failed", exception.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ChannelSendOutcome.Fail("timeout", "The SMTP operation timed out.");
        }
    }

    public Task<ChannelSendOutcome> SendTestAsync(
        ChannelDeliveryContext context,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var html = string.Create(
            CultureInfo.InvariantCulture,
            $"<p>这是一条 Sub2API Report 渠道测试邮件。</p>"
            + $"<p>发送时间 {now:yyyy-MM-dd HH:mm:ss} UTC。邮件内容为合成示例，不包含真实用量数据。</p>");
        var part = new OutboundPart(
            0,
            1,
            "[Sub2API Report] 渠道测试",
            html,
            null,
            DeliveryPayloadHash.Compute("test", html, null));
        return SendPartAsync(part, context, cancellationToken);
    }

    private static MimeMessage BuildMessage(OutboundPart part, EmailDeliveryOptions email)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(email.FromName ?? email.FromAddress, email.FromAddress));
        foreach (var to in email.ToAddresses)
        {
            message.To.Add(MailboxAddress.Parse(to));
        }

        foreach (var cc in email.CcAddresses)
        {
            message.Cc.Add(MailboxAddress.Parse(cc));
        }

        message.Subject = part.Subject;
        var bodyBuilder = new BodyBuilder { HtmlBody = part.Body };
        if (part.CsvContent is { } csvContent)
        {
            bodyBuilder.Attachments.Add(
                CsvAttachmentName,
                BuildCsvAttachmentBytes(csvContent),
                new ContentType("text", "csv") { Charset = "utf-8" });
        }

        message.Body = bodyBuilder.ToMessageBody();
        return message;
    }

    private static byte[] BuildCsvAttachmentBytes(string csvContent)
    {
        var contentBytes = Encoding.UTF8.GetBytes(csvContent.TrimStart('\uFEFF'));
        var bom = Encoding.UTF8.GetPreamble();
        var result = new byte[contentBytes.Length + bom.Length];
        global::System.Buffer.BlockCopy(bom, 0, result, 0, bom.Length);
        global::System.Buffer.BlockCopy(contentBytes, 0, result, bom.Length, contentBytes.Length);
        return result;
    }
}
