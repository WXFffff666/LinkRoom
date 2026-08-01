namespace LinkRoom.Core.Tests;

public class ConfigValidatorTests
{
    [Theory]
    [InlineData("ABC12345")]
    [InlineData("abc")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")] // 恰好 64 字符
    public void ValidateRoomId_Valid_ReturnsOk(string roomId)
    {
        var result = ConfigValidator.ValidateRoomId(roomId);

        Assert.True(result.Ok);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateRoomId_NullOrWhitespace_ReturnsError(string? roomId)
    {
        var result = ConfigValidator.ValidateRoomId(roomId);

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData("ab")]                       // 短于 3
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")] // 65 字符，长于 64
    [InlineData("AB C")]                     // 含空格
    [InlineData("AB\tC")]                    // 含制表符
    public void ValidateRoomId_Invalid_ReturnsError(string roomId)
    {
        var result = ConfigValidator.ValidateRoomId(roomId);

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData(1024)]
    [InlineData(65535)]
    public void ValidatePort_Boundaries_ReturnsOk(int port)
    {
        var result = ConfigValidator.ValidatePort(port);

        Assert.True(result.Ok);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1023)]
    [InlineData(65536)]
    public void ValidatePort_OutOfRange_ReturnsError(int port)
    {
        var result = ConfigValidator.ValidatePort(port);

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ValidatePassword_NullOrShort_ReturnsOk()
    {
        Assert.True(ConfigValidator.ValidatePassword(null).Ok);
        Assert.True(ConfigValidator.ValidatePassword("").Ok);
        Assert.True(ConfigValidator.ValidatePassword("pw").Ok);
    }

    [Fact]
    public void ValidatePassword_MaxLength128_ReturnsOk()
    {
        var result = ConfigValidator.ValidatePassword(new string('x', 128));

        Assert.True(result.Ok);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ValidatePassword_TooLong_ReturnsError()
    {
        var result = ConfigValidator.ValidatePassword(new string('x', 129));

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ValidateAll_AllValid_ReturnsOk()
    {
        var result = ConfigValidator.ValidateAll("ABC12345", 25565, 1400, "secret");

        Assert.True(result.Ok);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ValidateAll_FirstInvalidFieldFails()
    {
        // 房间号非法 → 整体失败
        Assert.False(ConfigValidator.ValidateAll("ab", 25565, 1400, "secret").Ok);

        // 端口非法 → 整体失败
        Assert.False(ConfigValidator.ValidateAll("ABC12345", 80, 1400, "secret").Ok);

        // MTU 非法 → 整体失败
        Assert.False(ConfigValidator.ValidateAll("ABC12345", 25565, 2000, "secret").Ok);

        // 密码超长 → 整体失败
        Assert.False(ConfigValidator.ValidateAll("ABC12345", 25565, 1400, new string('x', 129)).Ok);
    }
}
