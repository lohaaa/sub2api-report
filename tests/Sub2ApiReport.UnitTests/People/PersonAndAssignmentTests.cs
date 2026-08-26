using Sub2ApiReport.Domain.People;

namespace Sub2ApiReport.UnitTests.People;

public sealed class PersonAndAssignmentTests
{
    [Fact]
    public void PersonNormalizesCodeAndIncrementsRevision()
    {
        var now = new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.Zero);
        var person = Person.Create(" Team.A ", "合成人员 A", now);

        person.Update("team-b", "合成人员 B", false, now.AddMinutes(1));

        Assert.Equal("team-b", person.Code);
        Assert.Equal("合成人员 B", person.DisplayName);
        Assert.False(person.IsActive);
        Assert.Equal(2, person.Revision);
    }

    [Theory]
    [InlineData("with space")]
    [InlineData("中文编码")]
    [InlineData("slash/code")]
    public void PersonRejectsUnsafeCodes(string code)
    {
        Assert.Throws<ArgumentException>(() =>
            Person.Create(code, "Synthetic Person", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void AssignmentTreatsDateRangesAsInclusive()
    {
        var assignment = PersonApiKeyAssignment.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 31),
            DateTimeOffset.UtcNow);

        Assert.True(assignment.IsEffectiveOn(new DateOnly(2026, 8, 31)));
        Assert.True(assignment.Overlaps(new DateOnly(2026, 8, 31), null));
        Assert.False(assignment.Overlaps(
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30)));
    }

    [Fact]
    public void AssignmentRejectsReversedDateRange()
    {
        Assert.Throws<ArgumentException>(() => PersonApiKeyAssignment.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 8, 31),
            DateTimeOffset.UtcNow));
    }
}
