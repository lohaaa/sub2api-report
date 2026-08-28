namespace Sub2ApiReport.Updater;

/// <summary>
/// 升级流程中可向调用方暴露的受控错误。消息必须是脱敏摘要，禁止包含令牌或内部路径细节。
/// </summary>
public sealed class UpdateOperationException : Exception
{
    public UpdateOperationException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
