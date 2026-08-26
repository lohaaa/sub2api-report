using Microsoft.AspNetCore.Identity;

namespace Sub2ApiReport.Infrastructure.Identity;

public sealed class Administrator : IdentityUser<Guid>
{
    public const int SingletonKeyValue = 1;

    public int SingletonKey { get; private init; } = SingletonKeyValue;

    public DateTimeOffset CreatedAt { get; private init; }

    public static Administrator Create(string username, DateTimeOffset createdAt) => new()
    {
        Id = Guid.NewGuid(),
        UserName = username,
        CreatedAt = createdAt,
        SecurityStamp = Guid.NewGuid().ToString("N"),
    };
}
