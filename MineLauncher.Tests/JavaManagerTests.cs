using MineLauncher.Services.Implementations;
namespace MineLauncher.Tests;
public sealed class JavaManagerTests
{
 private static JavaManager Create() => new(new DownloadService(new HttpClient()), new HttpClient());
 [Theory]
 [InlineData("1.16.5", 8)][InlineData("1.17.1", 17)][InlineData("1.18.2", 17)]
 [InlineData("1.20.4", 17)][InlineData("1.20.5", 21)][InlineData("1.20.6", 21)]
 [InlineData("1.21.1", 21)][InlineData("24w03a", 21)][InlineData("23w13a", 17)]
 public void MapsVersionToJava(string version, int expected) => Assert.Equal(expected, Create().GetRequiredJavaMajorVersion(version));
}