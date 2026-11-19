using Xunit;
using MineLauncher.Services.Implementations;

namespace MineLauncher.Tests;

public sealed class JavaManagerTests
{
    private static JavaManager Create() => new(new DownloadService(new HttpClient()), new HttpClient());

    [Theory]
    [InlineData("1.16.5", 8)]
    [InlineData("1.17", 17)]
    [InlineData("1.18.2", 17)]
    [InlineData("1.20.4", 17)]
    [InlineData("1.20.5", 21)]
    [InlineData("1.20.6", 21)]
    [InlineData("1.21.1", 21)]
    public void MapsReleaseToJava(string version, int expected)
        => Assert.Equal(expected, Create().GetRequiredJavaMajorVersion(version));

    [Theory]
    [InlineData("20w14a", 8)]
    [InlineData("21w10a", 8)]
    [InlineData("21w11a", 16)]
    [InlineData("22w12a", 17)]
    [InlineData("23w13a", 17)]
    [InlineData("23w31a", 17)]
    [InlineData("24w03a", 21)]
    [InlineData("25w01a", 21)]
    public void MapsSnapshotToJava(string version, int expected)
        => Assert.Equal(expected, Create().GetRequiredJavaMajorVersion(version));

    [Theory]
    [InlineData("not-a-version")]
    [InlineData("1.x")]
    [InlineData("24w3a")]
    [InlineData("24w03")]
    public void UnknownVersionUsesSafeDefault(string version)
        => Assert.Equal(21, Create().GetRequiredJavaMajorVersion(version));
}
