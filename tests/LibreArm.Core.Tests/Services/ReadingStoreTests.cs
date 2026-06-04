namespace LibreArm.Core.Tests.Services;

using LibreArm.Core.Models;
using LibreArm.Core.Services;

[TestClass]
public sealed class ReadingStoreTests
{
    private string? _databasePath;

    [TestCleanup]
    public void Cleanup()
    {
        if (_databasePath is not null && File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [TestMethod]
    public async Task LoadRecentSessionsAsync_ReturnsOnlyRequestedProfileSessions()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        var james = await store.CreateProfileAsync("James", new DateOnly(1984, 1, 1), BiologicalSex.Male);
        var angela = await store.CreateProfileAsync("Angela", new DateOnly(1986, 1, 1), BiologicalSex.Female);

        await store.SaveSessionAsync(james.Id, Reading(120, 80, 93), Reading(122, 82, 95), Reading(121, 81, 94), "QardioArm", "AABBCCDDEEFF");
        await store.SaveSessionAsync(angela.Id, Reading(110, 70, 83), Reading(112, 72, 85), Reading(111, 71, 84), "QardioArm", "AABBCCDDEEFF");

        var jamesSessions = await store.LoadRecentSessionsAsync(james.Id);

        Assert.HasCount(1, jamesSessions);
        Assert.AreEqual(james.Id, jamesSessions[0].ProfileId);
        Assert.AreEqual(121, jamesSessions[0].Average.Systolic);
    }

    [TestMethod]
    public async Task ClearProfileDataAsync_RemovesOnlyRequestedProfileSessions()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        var james = await store.CreateProfileAsync("James", new DateOnly(1984, 1, 1), BiologicalSex.Male);
        var angela = await store.CreateProfileAsync("Angela", new DateOnly(1986, 1, 1), BiologicalSex.Female);
        await store.SaveSessionAsync(james.Id, Reading(120, 80, 93), Reading(122, 82, 95), Reading(121, 81, 94), "QardioArm", "AABBCCDDEEFF");
        await store.SaveSessionAsync(angela.Id, Reading(110, 70, 83), Reading(112, 72, 85), Reading(111, 71, 84), "QardioArm", "AABBCCDDEEFF");

        await store.ClearProfileDataAsync(james.Id);

        Assert.IsEmpty(await store.LoadRecentSessionsAsync(james.Id));
        Assert.HasCount(1, await store.LoadRecentSessionsAsync(angela.Id));
    }

    [TestMethod]
    public async Task Profiles_RoundTripAndUpdateDemographics()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        var profile = await store.CreateProfileAsync("James", new DateOnly(1984, 2, 3), BiologicalSex.Male);

        var updated = await store.UpdateProfileAsync(profile, "Jamie", new DateOnly(1985, 3, 4), BiologicalSex.Unspecified);
        updated = await store.UpdateProfilePhotoAsync(updated, @"C:\photos\jamie.png");
        var profiles = await store.LoadProfilesAsync();

        Assert.HasCount(1, profiles);
        Assert.AreEqual(updated.Id, profiles[0].Id);
        Assert.AreEqual("Jamie", profiles[0].DisplayName);
        Assert.AreEqual(new DateOnly(1985, 3, 4), profiles[0].BirthDate);
        Assert.AreEqual(BiologicalSex.Unspecified, profiles[0].BiologicalSex);
        Assert.AreEqual(@"C:\photos\jamie.png", profiles[0].PhotoPath);
    }

    [TestMethod]
    public async Task LoadWeeklySummariesAsync_GroupsSessionsByMondayWeekAndProfile()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        var james = await store.CreateProfileAsync("James", new DateOnly(1984, 1, 1), BiologicalSex.Male);
        var angela = await store.CreateProfileAsync("Angela", new DateOnly(1986, 1, 1), BiologicalSex.Female);
        await store.SaveSessionAsync(james.Id, Reading(120, 80, 93), Reading(122, 82, 95), Reading(121, 81, 94), "QardioArm", "AABBCCDDEEFF");
        await store.SaveSessionAsync(james.Id, Reading(124, 82, 96), Reading(126, 84, 98), Reading(125, 83, 97), "QardioArm", "AABBCCDDEEFF");
        await store.SaveSessionAsync(angela.Id, Reading(140, 90, 107), Reading(142, 92, 109), Reading(141, 91, 108), "QardioArm", "AABBCCDDEEFF");

        var summaries = await store.LoadWeeklySummariesAsync(james.Id, 12);
        var currentWeek = summaries.Last();

        Assert.HasCount(12, summaries);
        Assert.AreEqual(2, currentWeek.SessionCount);
        Assert.AreEqual(123, currentWeek.AverageSystolic);
        Assert.AreEqual(82, currentWeek.AverageDiastolic);
    }

    private ReadingStore CreateStore()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        return new ReadingStore(_databasePath);
    }

    private static BloodPressureReading Reading(double systolic, double diastolic, double map)
    {
        return new BloodPressureReading(
            systolic,
            diastolic,
            map,
            BloodPressureUnit.MillimetersOfMercury,
            72,
            new DateTime(2026, 6, 4, 8, 0, 0),
            null,
            0,
            new DateTimeOffset(2026, 6, 4, 8, 0, 0, TimeSpan.Zero),
            [0x00, 0x78]);
    }
}
