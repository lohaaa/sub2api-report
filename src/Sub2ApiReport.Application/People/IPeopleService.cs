using Sub2ApiReport.Application.Sub2Api;

namespace Sub2ApiReport.Application.People;

public interface IPeopleService
{
    Task<IReadOnlyList<PersonSnapshot>> ListAsync(CancellationToken cancellationToken);

    Task<PersonSnapshot> GetAsync(Guid personId, CancellationToken cancellationToken);

    Task<PersonSnapshot> CreateAsync(
        CreatePersonCommand command,
        CancellationToken cancellationToken);

    Task<PersonSnapshot> UpdateAsync(
        UpdatePersonCommand command,
        CancellationToken cancellationToken);

    Task DeactivateAsync(
        Guid personId,
        CancellationToken cancellationToken);

    Task<ApiKeyAssignmentSnapshot> GetAssignmentAsync(
        Guid assignmentId,
        CancellationToken cancellationToken);

    Task<ApiKeyAssignmentSnapshot> CreateAssignmentAsync(
        CreateApiKeyAssignmentCommand command,
        CancellationToken cancellationToken);

    Task<ApiKeyAssignmentSnapshot> UpdateAssignmentAsync(
        UpdateApiKeyAssignmentCommand command,
        CancellationToken cancellationToken);

    Task DeleteAssignmentAsync(
        Guid assignmentId,
        CancellationToken cancellationToken);
}

public sealed record PersonSnapshot(
    Guid Id,
    string Code,
    string DisplayName,
    bool IsActive,
    int CurrentApiKeyCount,
    int AssignmentCount,
    long Revision,
    DateTimeOffset UpdatedAt);

public sealed record CreatePersonCommand(string Code, string DisplayName);

public sealed record UpdatePersonCommand(
    Guid Id,
    string Code,
    string DisplayName,
    bool IsActive,
    long ExpectedRevision);

public sealed record CreateApiKeyAssignmentCommand(
    Guid PersonId,
    Guid ExternalApiKeyId,
    DateOnly ValidFrom,
    DateOnly? ValidTo);

public sealed record UpdateApiKeyAssignmentCommand(
    Guid AssignmentId,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    long ExpectedRevision);

public sealed class PeopleResourceNotFoundException(string resource)
    : KeyNotFoundException($"The {resource} was not found.");

public sealed class PeopleConflictException(string message) : InvalidOperationException(message);
