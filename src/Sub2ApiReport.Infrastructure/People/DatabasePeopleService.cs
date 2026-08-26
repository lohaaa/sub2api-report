using Microsoft.EntityFrameworkCore;
using Sub2ApiReport.Application.People;
using Sub2ApiReport.Application.Sub2Api;
using Sub2ApiReport.Domain.People;
using Sub2ApiReport.Infrastructure.Persistence;

namespace Sub2ApiReport.Infrastructure.People;

internal sealed class DatabasePeopleService(
    ReportDbContext dbContext,
    TimeProvider timeProvider) : IPeopleService
{
    private static readonly SemaphoreSlim AssignmentLock = new(1, 1);

    public async Task<IReadOnlyList<PersonSnapshot>> ListAsync(CancellationToken cancellationToken)
    {
        var currentDate = await GetCurrentDateAsync(cancellationToken);
        return await dbContext.People
            .AsNoTracking()
            .OrderByDescending(person => person.IsActive)
            .ThenBy(person => person.DisplayName)
            .Select(person => new PersonSnapshot(
                person.Id,
                person.Code,
                person.DisplayName,
                person.IsActive,
                dbContext.PersonApiKeyAssignments.Count(assignment =>
                    assignment.PersonId == person.Id
                    && assignment.ValidFrom <= currentDate
                    && (assignment.ValidTo == null || assignment.ValidTo >= currentDate)),
                dbContext.PersonApiKeyAssignments.Count(assignment => assignment.PersonId == person.Id),
                person.Revision,
                person.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<PersonSnapshot> GetAsync(
        Guid personId,
        CancellationToken cancellationToken)
    {
        var currentDate = await GetCurrentDateAsync(cancellationToken);
        var person = await dbContext.People
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == personId, cancellationToken)
            ?? throw new PeopleResourceNotFoundException("person");
        var counts = await GetAssignmentCountsAsync(person.Id, currentDate, cancellationToken);
        return Map(person, counts.Current, counts.Total);
    }

    public async Task<PersonSnapshot> CreateAsync(
        CreatePersonCommand command,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var person = Person.Create(command.Code, command.DisplayName, now);
        dbContext.People.Add(person);
        await SavePersonAsync(cancellationToken);
        return Map(person, 0, 0);
    }

    public async Task<PersonSnapshot> UpdateAsync(
        UpdatePersonCommand command,
        CancellationToken cancellationToken)
    {
        var person = await dbContext.People.SingleOrDefaultAsync(
            item => item.Id == command.Id,
            cancellationToken) ?? throw new PeopleResourceNotFoundException("person");
        if (person.Revision != command.ExpectedRevision)
        {
            throw new PeopleConflictException("The person was modified by another request.");
        }

        person.Update(
            command.Code,
            command.DisplayName,
            command.IsActive,
            timeProvider.GetUtcNow());
        await SavePersonAsync(cancellationToken);
        var currentDate = await GetCurrentDateAsync(cancellationToken);
        var counts = await GetAssignmentCountsAsync(person.Id, currentDate, cancellationToken);
        return Map(person, counts.Current, counts.Total);
    }

    public async Task DeactivateAsync(Guid personId, CancellationToken cancellationToken)
    {
        var person = await dbContext.People.SingleOrDefaultAsync(
            item => item.Id == personId,
            cancellationToken) ?? throw new PeopleResourceNotFoundException("person");
        person.Deactivate(timeProvider.GetUtcNow());
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new PeopleConflictException("The person was modified by another request.");
        }
    }

    public async Task<ApiKeyAssignmentSnapshot> GetAssignmentAsync(
        Guid assignmentId,
        CancellationToken cancellationToken)
    {
        var assignment = await dbContext.PersonApiKeyAssignments
            .AsNoTracking()
            .Include(item => item.Person)
            .SingleOrDefaultAsync(item => item.Id == assignmentId, cancellationToken)
            ?? throw new PeopleResourceNotFoundException("assignment");
        return Map(assignment, assignment.Person);
    }

    public async Task<ApiKeyAssignmentSnapshot> CreateAssignmentAsync(
        CreateApiKeyAssignmentCommand command,
        CancellationToken cancellationToken)
    {
        await AssignmentLock.WaitAsync(cancellationToken);
        try
        {
            var person = await dbContext.People
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == command.PersonId, cancellationToken)
                ?? throw new PeopleResourceNotFoundException("person");
            if (!person.IsActive)
            {
                throw new PeopleConflictException("An inactive person cannot receive a new API Key assignment.");
            }

            var keyExists = await dbContext.ExternalApiKeys
                .AsNoTracking()
                .AnyAsync(key => key.Id == command.ExternalApiKeyId, cancellationToken);
            if (!keyExists)
            {
                throw new PeopleResourceNotFoundException("API Key");
            }

            var existingAssignments = await dbContext.PersonApiKeyAssignments
                .AsNoTracking()
                .Where(assignment => assignment.ExternalApiKeyId == command.ExternalApiKeyId)
                .ToListAsync(cancellationToken);
            EnsureNoOverlap(existingAssignments, command.ValidFrom, command.ValidTo);

            var assignment = PersonApiKeyAssignment.Create(
                command.PersonId,
                command.ExternalApiKeyId,
                command.ValidFrom,
                command.ValidTo,
                timeProvider.GetUtcNow());
            dbContext.PersonApiKeyAssignments.Add(assignment);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Map(assignment, person);
        }
        finally
        {
            AssignmentLock.Release();
        }
    }

    public async Task<ApiKeyAssignmentSnapshot> UpdateAssignmentAsync(
        UpdateApiKeyAssignmentCommand command,
        CancellationToken cancellationToken)
    {
        await AssignmentLock.WaitAsync(cancellationToken);
        try
        {
            var assignment = await dbContext.PersonApiKeyAssignments
                .Include(item => item.Person)
                .SingleOrDefaultAsync(item => item.Id == command.AssignmentId, cancellationToken)
                ?? throw new PeopleResourceNotFoundException("assignment");
            if (assignment.Revision != command.ExpectedRevision)
            {
                throw new PeopleConflictException("The assignment was modified by another request.");
            }

            var otherAssignments = await dbContext.PersonApiKeyAssignments
                .AsNoTracking()
                .Where(item =>
                    item.ExternalApiKeyId == assignment.ExternalApiKeyId
                    && item.Id != assignment.Id)
                .ToListAsync(cancellationToken);
            EnsureNoOverlap(otherAssignments, command.ValidFrom, command.ValidTo);
            assignment.Update(command.ValidFrom, command.ValidTo, timeProvider.GetUtcNow());
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new PeopleConflictException("The assignment was modified by another request.");
            }

            return Map(assignment, assignment.Person);
        }
        finally
        {
            AssignmentLock.Release();
        }
    }

    public async Task DeleteAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken)
    {
        var assignment = await dbContext.PersonApiKeyAssignments.SingleOrDefaultAsync(
            item => item.Id == assignmentId,
            cancellationToken) ?? throw new PeopleResourceNotFoundException("assignment");
        dbContext.PersonApiKeyAssignments.Remove(assignment);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new PeopleConflictException("The assignment was modified by another request.");
        }
    }

    private async Task SavePersonAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new PeopleConflictException("The person was modified by another request.");
        }
        catch (DbUpdateException)
        {
            throw new PeopleConflictException("The person code is already in use.");
        }
    }

    private async Task<(int Current, int Total)> GetAssignmentCountsAsync(
        Guid personId,
        DateOnly currentDate,
        CancellationToken cancellationToken)
    {
        var current = await dbContext.PersonApiKeyAssignments.CountAsync(
            assignment =>
                assignment.PersonId == personId
                && assignment.ValidFrom <= currentDate
                && (assignment.ValidTo == null || assignment.ValidTo >= currentDate),
            cancellationToken);
        var total = await dbContext.PersonApiKeyAssignments.CountAsync(
            assignment => assignment.PersonId == personId,
            cancellationToken);
        return (current, total);
    }

    private async Task<DateOnly> GetCurrentDateAsync(CancellationToken cancellationToken)
    {
        var timezone = await dbContext.SystemSettings
            .AsNoTracking()
            .Where(setting => setting.Id == Domain.System.SystemSetting.SingletonId)
            .Select(setting => setting.Timezone)
            .SingleAsync(cancellationToken);
        var localNow = TimeZoneInfo.ConvertTime(
            timeProvider.GetUtcNow(),
            TimeZoneInfo.FindSystemTimeZoneById(timezone));
        return DateOnly.FromDateTime(localNow.DateTime);
    }

    private static void EnsureNoOverlap(
        IEnumerable<PersonApiKeyAssignment> existingAssignments,
        DateOnly validFrom,
        DateOnly? validTo)
    {
        if (existingAssignments.Any(assignment => assignment.Overlaps(validFrom, validTo)))
        {
            throw new PeopleConflictException("The API Key already has an overlapping person assignment.");
        }
    }

    private static PersonSnapshot Map(Person person, int currentApiKeyCount, int assignmentCount) => new(
        person.Id,
        person.Code,
        person.DisplayName,
        person.IsActive,
        currentApiKeyCount,
        assignmentCount,
        person.Revision,
        person.UpdatedAt);

    private static ApiKeyAssignmentSnapshot Map(
        PersonApiKeyAssignment assignment,
        Person person) => new(
            assignment.Id,
            person.Id,
            person.Code,
            person.DisplayName,
            assignment.ValidFrom,
            assignment.ValidTo,
            assignment.Revision);
}
