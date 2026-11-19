using MineLauncher.Services.Implementations;
using Xunit;

namespace MineLauncher.Tests;

public sealed class OfflineAuthServiceTests
{
    [Fact]
    public async Task LoginUsesNicknameProvidedByLauncher()
    {
        var auth = new OfflineAuthService { Nickname = "player2" };

        var result = await auth.LoginAsync();

        Assert.True(result.Success);
        Assert.Equal("player2", result.Session!.Nickname);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("this_nickname_is_too_long")]
    [InlineData("ИмяИгрока")]
    public async Task LoginRejectsInvalidNickname(string nickname)
    {
        var auth = new OfflineAuthService { Nickname = nickname };

        var result = await auth.LoginAsync();

        Assert.False(result.Success);
    }
}
