namespace Sub2ApiReport.Domain.Sub2Api;

public sealed class Sub2ApiConnection
{
    public const int SingletonId = 1;

    private Sub2ApiConnection()
    {
    }

    public int Id { get; private init; } = SingletonId;

    public string BaseUrl { get; private set; } = string.Empty;

    public string? AdminApiKeyCiphertext { get; private set; }

    public string? AdminApiKeySuffix { get; private set; }

    public long? LegacyUserId { get; private init; }

    public Sub2ApiUserScopeMode UserScopeMode { get; private set; } = Sub2ApiUserScopeMode.SelectedUsers;

    public long? CodexGroupId { get; private set; }

    public DateTimeOffset? LastUsersSynchronizedAt { get; private set; }

    public int? LastSynchronizedUserCount { get; private set; }

    public long Revision { get; private set; } = 1;

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? LastTestedAt { get; private set; }

    public bool? LastTestSucceeded { get; private set; }

    public string? LastTestCode { get; private set; }

    public DateTimeOffset? LastSynchronizedAt { get; private set; }

    public int? LastSynchronizedKeyCount { get; private set; }

    public static Sub2ApiConnection Create(
        string baseUrl,
        string adminApiKeyCiphertext,
        string adminApiKeySuffix,
        long? codexGroupId,
        DateTimeOffset createdAt) => new()
        {
            BaseUrl = ValidateBaseUrl(baseUrl),
            AdminApiKeyCiphertext = ValidateCiphertext(adminApiKeyCiphertext),
            AdminApiKeySuffix = ValidateSuffix(adminApiKeySuffix),
            CodexGroupId = ValidateOptionalPositiveId(codexGroupId, nameof(codexGroupId)),
            UpdatedAt = createdAt,
        };

    public void Update(
        string baseUrl,
        string? adminApiKeyCiphertext,
        string? adminApiKeySuffix,
        bool clearAdminApiKey,
        long? codexGroupId,
        DateTimeOffset updatedAt)
    {
        if (clearAdminApiKey && adminApiKeyCiphertext is not null)
        {
            throw new ArgumentException("A secret cannot be replaced and cleared at the same time.");
        }

        BaseUrl = ValidateBaseUrl(baseUrl);
        CodexGroupId = ValidateOptionalPositiveId(codexGroupId, nameof(codexGroupId));
        if (clearAdminApiKey)
        {
            AdminApiKeyCiphertext = null;
            AdminApiKeySuffix = null;
        }
        else if (adminApiKeyCiphertext is not null)
        {
            AdminApiKeyCiphertext = ValidateCiphertext(adminApiKeyCiphertext);
            AdminApiKeySuffix = ValidateSuffix(adminApiKeySuffix);
        }

        UpdatedAt = updatedAt;
        Revision++;
    }

    public void RecordConnectionTest(bool succeeded, string code, DateTimeOffset testedAt)
    {
        LastTestSucceeded = succeeded;
        LastTestCode = ValidateText(code, 64, nameof(code));
        LastTestedAt = testedAt;
    }

    public void UpdateUserScope(Sub2ApiUserScopeMode mode, DateTimeOffset updatedAt)
    {
        UserScopeMode = mode;
        UpdatedAt = updatedAt;
        Revision++;
    }

    public void RecordUserSynchronization(int userCount, DateTimeOffset synchronizedAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(userCount);
        LastSynchronizedUserCount = userCount;
        LastUsersSynchronizedAt = synchronizedAt;
    }

    public void RecordSynchronization(int keyCount, DateTimeOffset synchronizedAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(keyCount);

        LastSynchronizedKeyCount = keyCount;
        LastSynchronizedAt = synchronizedAt;
    }

    private static string ValidateBaseUrl(string value) => ValidateText(value, 2048, nameof(value));

    private static string ValidateCiphertext(string value) => ValidateText(value, 16384, nameof(value));

    private static string ValidateSuffix(string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var suffix = value.Trim();
        return suffix.Length <= 8
            ? suffix
            : throw new ArgumentException("The secret suffix cannot exceed 8 characters.", nameof(value));
    }

    private static long? ValidateOptionalPositiveId(long? value, string parameterName) => value is null
        ? null
        : value.Value > 0
            ? value
            : throw new ArgumentOutOfRangeException(parameterName, "The identifier must be positive.");

    private static string ValidateText(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
    }
}
