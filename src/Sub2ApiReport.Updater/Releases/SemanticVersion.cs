using System.Globalization;

namespace Sub2ApiReport.Updater.Releases;

/// <summary>
/// 严格 SemVer 2.0.0 解析与比较，遵循 semver.org 规范，禁止前导零等宽松格式。
/// </summary>
public sealed record SemanticVersion : IComparable<SemanticVersion>
{
    private SemanticVersion(int major, int minor, int patch, string? prerelease, string? buildMetadata)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
        BuildMetadata = buildMetadata;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public string? Prerelease { get; }

    public string? BuildMetadata { get; }

    public bool HasPrerelease => Prerelease is not null;

    public static SemanticVersion Parse(string value) =>
        TryParse(value, out var version)
            ? version!
            : throw new FormatException($"'{value}' is not a valid SemVer version.");

    public static bool operator <(SemanticVersion? left, SemanticVersion? right) =>
        left is null ? right is not null : left.CompareTo(right) < 0;

    public static bool operator <=(SemanticVersion? left, SemanticVersion? right) =>
        left is null || left.CompareTo(right) <= 0;

    public static bool operator >(SemanticVersion? left, SemanticVersion? right) =>
        left is not null && left.CompareTo(right) > 0;

    public static bool operator >=(SemanticVersion? left, SemanticVersion? right) =>
        left is null ? right is null : left.CompareTo(right) >= 0;

    public override string ToString()
    {
        var core = string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");
        if (Prerelease is not null)
        {
            core += $"-{Prerelease}";
        }

        if (BuildMetadata is not null)
        {
            core += $"+{BuildMetadata}";
        }

        return core;
    }

    public static bool TryParse(string? value, out SemanticVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var core = value;
        string? buildMetadata = null;
        var plusIndex = core.IndexOf('+', StringComparison.Ordinal);
        if (plusIndex >= 0)
        {
            buildMetadata = core[(plusIndex + 1)..];
            core = core[..plusIndex];
        }

        if (buildMetadata is not null && !IsValidIdentifiers(buildMetadata, allowLeadingZeros: true))
        {
            return false;
        }

        string? prerelease = null;
        var dashIndex = core.IndexOf('-', StringComparison.Ordinal);
        if (dashIndex >= 0)
        {
            prerelease = core[(dashIndex + 1)..];
            core = core[..dashIndex];
        }

        if (prerelease is not null && !IsValidIdentifiers(prerelease, allowLeadingZeros: false))
        {
            return false;
        }

        var parts = core.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!TryParseCoreNumber(parts[i], out numbers[i]))
            {
                return false;
            }
        }

        version = new SemanticVersion(numbers[0], numbers[1], numbers[2], prerelease, buildMetadata);
        return true;
    }

    public int CompareTo(SemanticVersion? other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var coreComparison = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (coreComparison != 0)
        {
            return coreComparison;
        }

        if (Prerelease is null)
        {
            return other.Prerelease is null ? 0 : 1;
        }

        if (other.Prerelease is null)
        {
            return -1;
        }

        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    private static int ComparePrerelease(string left, string right)
    {
        var leftIdentifiers = left.Split('.');
        var rightIdentifiers = right.Split('.');
        var count = Math.Min(leftIdentifiers.Length, rightIdentifiers.Length);
        for (var i = 0; i < count; i++)
        {
            var comparison = ComparePrereleaseIdentifier(leftIdentifiers[i], rightIdentifiers[i]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return leftIdentifiers.Length.CompareTo(rightIdentifiers.Length);
    }

    private static int ComparePrereleaseIdentifier(string left, string right)
    {
        var leftIsNumeric = IsNumericIdentifier(left);
        var rightIsNumeric = IsNumericIdentifier(right);
        if (leftIsNumeric && rightIsNumeric)
        {
            var leftValue = int.Parse(left, CultureInfo.InvariantCulture);
            var rightValue = int.Parse(right, CultureInfo.InvariantCulture);
            return leftValue.CompareTo(rightValue);
        }

        if (leftIsNumeric)
        {
            return -1;
        }

        if (rightIsNumeric)
        {
            return 1;
        }

        return string.CompareOrdinal(left, right);
    }

    private static bool IsNumericIdentifier(string identifier) =>
        identifier.Length > 0 && identifier.All(char.IsAsciiDigit);

    private static bool IsValidIdentifiers(string value, bool allowLeadingZeros)
    {
        if (value.Length == 0)
        {
            return false;
        }

        foreach (var identifier in value.Split('.'))
        {
            if (identifier.Length == 0 || !identifier.All(IsIdentifierCharacter))
            {
                return false;
            }

            if (!allowLeadingZeros
                && IsNumericIdentifier(identifier)
                && identifier.Length > 1
                && identifier[0] == '0')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsIdentifierCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character == '-';

    private static bool TryParseCoreNumber(string value, out int number)
    {
        number = 0;
        if (value.Length == 0 || !value.All(char.IsAsciiDigit))
        {
            return false;
        }

        if (value.Length > 1 && value[0] == '0')
        {
            return false;
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }
}
