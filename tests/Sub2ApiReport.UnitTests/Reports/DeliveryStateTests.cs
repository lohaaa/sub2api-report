using System.Security.Cryptography;
using Sub2ApiReport.Application.Notifications;
using Sub2ApiReport.Domain.Notifications;
using Sub2ApiReport.Domain.Reports;

namespace Sub2ApiReport.UnitTests.Reports;

public sealed class DeliveryStateTests
{
    [Fact]
    public void DeliveryPayloadHashIsDeterministicAndCaseInsensitiveToLowerHex()
    {
        var first = DeliveryPayloadHash.Compute("主题", "正文", "csv");
        var second = DeliveryPayloadHash.Compute("主题", "正文", "csv");

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.Equal(first, first.ToLowerInvariant());
        Assert.True(first.All(character => Uri.IsHexDigit(character)));
        Assert.NotEqual(first, DeliveryPayloadHash.Compute("主题", "其他内容", "csv"));
    }

    [Fact]
    public void DeliveryPayloadHashMatchesManualSha256()
    {
        var expected = Convert.ToHexString(
            SHA256.HashData("subject\n\nbody\n\ncsv"u8))
            .ToLowerInvariant();

        Assert.Equal(expected, DeliveryPayloadHash.Compute("subject", "body", "csv"));
    }

    [Fact]
    public void DeliveryRecordTracksAttemptsAndTerminalStates()
    {
        var record = DeliveryRecord.Create(
            Guid.NewGuid(),
            NotificationChannelType.DingTalk,
            "合成钉钉渠道",
            "aggregate-hash",
            [DeliveryPart.Create(0, 1, "part-hash")]);

        Assert.Equal(DeliveryStatus.Pending, record.Status);

        record.MarkSending();
        Assert.Equal(DeliveryStatus.Sending, record.Status);
        Assert.Equal(1, record.Attempts);

        record.MarkSucceeded(DateTimeOffset.UtcNow);
        Assert.Equal(DeliveryStatus.Succeeded, record.Status);
        Assert.NotNull(record.SentAt);
        Assert.Null(record.ErrorCode);

        Assert.Throws<InvalidOperationException>(record.MarkSending);
        Assert.Throws<InvalidOperationException>(() =>
            record.ResetForRetry("aggregate-hash-2"));
    }

    [Fact]
    public void DeliveryRecordFailureStoresSanitizedErrorAndAllowsRetry()
    {
        var record = DeliveryRecord.Create(
            Guid.NewGuid(),
            NotificationChannelType.Feishu,
            "合成飞书渠道",
            "aggregate-hash",
            [DeliveryPart.Create(0, 1, "part-hash")]);

        record.MarkSending();
        record.MarkFailed("business_error", "飞书返回错误码 9999（无描述）");

        Assert.Equal(DeliveryStatus.Failed, record.Status);
        Assert.Equal("business_error", record.ErrorCode);
        Assert.Null(record.SentAt);
        Assert.True(record.ErrorMessage?.Length <= DeliveryRecord.ErrorMessageMaxLength);

        record.ResetForRetry("aggregate-hash-2");
        Assert.Equal(DeliveryStatus.Pending, record.Status);
        Assert.Equal("aggregate-hash-2", record.PayloadHash);
        Assert.Null(record.ErrorCode);
        Assert.Equal(1, record.Attempts);
    }

    [Fact]
    public void DeliveryRecordRequiresAtLeastOnePart()
    {
        Assert.Throws<ArgumentException>(() => DeliveryRecord.Create(
            Guid.NewGuid(),
            NotificationChannelType.Email,
            "合成邮件渠道",
            "hash",
            []));
    }

    [Fact]
    public void DeliveryPartValidatesIndexAndCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DeliveryPart.Create(2, 2, "hash"));
        Assert.Throws<ArgumentOutOfRangeException>(() => DeliveryPart.Create(0, 0, "hash"));

        var part = DeliveryPart.Create(1, 3, "hash");
        part.MarkSucceeded(DateTimeOffset.UtcNow);
        Assert.Equal(DeliveryPartStatus.Succeeded, part.Status);
        Assert.Throws<InvalidOperationException>(() => part.MarkFailed("failed", null));
    }

    [Fact]
    public void ReportRunCompletesOnceThenSupportsRetryResults()
    {
        var run = ReportRun.StartManual(Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(ReportRunStatus.Running, run.Status);
        Assert.False(run.IsRetryable);

        run.Complete(ReportRunStatus.PartialFailed, DateTimeOffset.UtcNow);
        Assert.Equal(ReportRunStatus.PartialFailed, run.Status);
        Assert.NotNull(run.CompletedAt);
        Assert.True(run.IsRetryable);

        Assert.Throws<InvalidOperationException>(() =>
            run.Complete(ReportRunStatus.Succeeded, DateTimeOffset.UtcNow));

        run.RecordRetryResult(ReportRunStatus.Succeeded, DateTimeOffset.UtcNow);
        Assert.Equal(ReportRunStatus.Succeeded, run.Status);
        Assert.False(run.IsRetryable);

        Assert.Throws<InvalidOperationException>(() =>
            run.RecordRetryResult(ReportRunStatus.Succeeded, DateTimeOffset.UtcNow));
    }
}
