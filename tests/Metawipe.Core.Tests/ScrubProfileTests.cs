using Metawipe.Core.Models;

namespace Metawipe.Core.Tests;

public class ScrubProfileTests
{
    [Fact]
    public void StripAll_RemovesAllRemovableFields_AndKeepsNonRemovable()
    {
        var profile = ScrubProfile.CreateStripAll();

        var removable = new MetadataField("EXIF", "CameraModel", "X100", false, true);
        var nonRemovable = new MetadataField("CORE", "FileName", "photo.jpg", false, false);

        Assert.True(profile.ShouldRemove(removable));
        Assert.False(profile.ShouldRemove(nonRemovable));
    }

    [Fact]
    public void KeepWhitelist_RetainsWhitelistedFields_CaseInsensitively()
    {
        var profile = ScrubProfile.CreateKeepWhitelist(
        [
            "exif/orientation",
            "icc/profile",
        ]);

        var keep = new MetadataField("EXIF", "Orientation", "TopLeft", false, true);
        var remove = new MetadataField("EXIF", "GPSLatitude", "47.6205", true, true);

        Assert.False(profile.ShouldRemove(keep));
        Assert.True(profile.ShouldRemove(remove));
    }
}
