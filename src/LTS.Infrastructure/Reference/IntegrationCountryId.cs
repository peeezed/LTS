namespace LTS.Infrastructure.Reference;

/// <summary>
/// Countries sourced from LTS_Integration are exposed to the rest of the app with their row id
/// offset by this amount (see ReferenceDataService), so they can never collide with the app's
/// own, much smaller, Country ids from the old database. Anything that stores or looks up a
/// country id against LTS_Integration's own tables (e.g. LTS_UserCountryAccess, which has a real
/// foreign key to LTS_Countries) has to convert back to the raw id first - the offset only makes
/// sense at the boundary where a bare int id could otherwise be compared against the old
/// database's ids.
/// </summary>
public static class IntegrationCountryId
{
    public const int Offset = 1_000_000;

    public static int ToAppId(int rawId) => rawId + Offset;

    public static int ToRawId(int appId) => appId - Offset;
}
