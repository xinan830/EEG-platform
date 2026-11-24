using System.Text.Json;
using BrainPlatform.Desktop.Modules.Montages.Stores;

namespace BrainPlatform.Desktop.Tests.Montages;

public sealed class MontageReferenceMetadataTests
{
    [Fact]
    public void MissingAndBlankSnapshotAreDifferentForStrictValidation()
    {
        var missing = MontageReferenceMetadata.From(new Dictionary<string, string>());
        var blank = MontageReferenceMetadata.From(new Dictionary<string, string>
        {
            ["montage_configuration_snapshot_json"] = "  ",
        });

        Assert.False(missing.HasSnapshot);
        Assert.Null(missing.SnapshotJson);
        Assert.True(blank.HasSnapshot);
        Assert.Equal("  ", blank.SnapshotJson);
        Assert.Empty(blank.ReadIds());
    }

    [Fact]
    public void MatchingIdentityAndSnapshotAreReadWithoutChangingMetadata()
    {
        var profile = MontageAppearanceTests.Profile();
        var metadata = new Dictionary<string, string>
        {
            ["montage_configuration_id"] = profile.Id,
            ["montage_configuration_snapshot_json"] = JsonSerializer.Serialize(profile),
        };

        var parsed = MontageReferenceMetadata.From(metadata);
        var before = JsonSerializer.Serialize(metadata);

        Assert.Equal([profile.Id], parsed.ReadIds());
        Assert.Equal(profile.Id, parsed.ReadSnapshot().Id);
        Assert.Equal(before, JsonSerializer.Serialize(metadata));
    }

    [Fact]
    public void ReadIdsRejectsConflictingSnapshotIdentity()
    {
        var profile = MontageAppearanceTests.Profile();
        var metadata = new Dictionary<string, string>
        {
            ["montage_configuration_id"] = profile.Id,
            ["montage_configuration_snapshot_json"] = JsonSerializer.Serialize(profile with { Id = "other" }),
        };

        var error = Assert.Throws<InvalidOperationException>(() => MontageReferenceMetadata.From(metadata).ReadIds());
        Assert.Contains("不一致", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void BlankValuesDoNotInventReferenceIds(string blank)
    {
        var parsed = MontageReferenceMetadata.From(new Dictionary<string, string>
        {
            ["montage_configuration_id"] = blank,
            ["montage_configuration_snapshot_json"] = blank,
        });
        Assert.Empty(parsed.ReadIds());
        Assert.Throws<JsonException>(() => parsed.ReadSnapshot());
    }

    [Fact]
    public void LegacyIdOnlyAndSnapshotOnlyMetadataRemainSupported()
    {
        var idOnly = MontageReferenceMetadata.From(new Dictionary<string, string>
        {
            ["montage_configuration_id"] = "legacy-id",
        });
        Assert.Equal("legacy-id", Assert.Single(idOnly.ReadIds()));
        Assert.False(idOnly.HasSnapshot);
        var snapshotOnly = MontageReferenceMetadata.From(new Dictionary<string, string>
        {
            ["montage_configuration_snapshot_json"] = "{\"Id\":\"snapshot-id\"}",
        });
        Assert.Equal("snapshot-id", Assert.Single(snapshotOnly.ReadIds()));
    }

    [Fact]
    public void NonblankIdentityIsNotTrimmedOrCaseFolded()
    {
        var parsed = MontageReferenceMetadata.From(new Dictionary<string, string>
        {
            ["montage_configuration_id"] = " ID ",
        });
        Assert.Equal(" ID ", Assert.Single(parsed.ReadIds()));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Id\":\" \"}")]
    public void MissingSnapshotIdentityFailsVerification(string json)
    {
        var parsed = MontageReferenceMetadata.From(new Dictionary<string, string>
        {
            ["montage_configuration_snapshot_json"] = json,
        });
        Assert.Throws<InvalidOperationException>(() => parsed.ReadIds());
    }

    [Fact]
    public void MalformedSnapshotFailsInsteadOfFallingBackToMetadataId()
    {
        var parsed = MontageReferenceMetadata.From(new Dictionary<string, string>
        {
            ["montage_configuration_id"] = "id",
            ["montage_configuration_snapshot_json"] = "{broken",
        });
        Assert.ThrowsAny<JsonException>(() => parsed.ReadIds());
    }
}
