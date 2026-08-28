namespace Sub2ApiReport.Infrastructure.Persistence;

public sealed class UnixTimeMigrationState
{
    public const int SingletonId = 1;

    private UnixTimeMigrationState()
    {
    }

    public int Id { get; private init; } = SingletonId;

    public bool Completed { get; private set; }

    public static UnixTimeMigrationState CreatePending() => new();

    public static UnixTimeMigrationState CreateCompleted() => new() { Completed = true };
}
