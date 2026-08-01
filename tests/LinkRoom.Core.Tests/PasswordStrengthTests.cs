namespace LinkRoom.Core.Tests;

public class PasswordStrengthTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Evaluate_Empty_ReturnsEmpty(string? password)
    {
        Assert.Equal(PasswordStrengthLevel.Empty, PasswordStrength.Evaluate(password));
    }

    [Theory]
    [InlineData("a")]          // 0 分
    [InlineData("abcde")]      // 仅小写，1 分
    [InlineData("abc1")]       // 小写+数字，2 分
    public void Evaluate_Weak_ReturnsWeak(string password)
    {
        Assert.Equal(PasswordStrengthLevel.Weak, PasswordStrength.Evaluate(password));
    }

    [Theory]
    [InlineData("Abcdef")]     // 长度+大小写，3 分
    [InlineData("Abcdef12")]   // 长度+大小写+数字，4 分
    public void Evaluate_Fair_ReturnsFair(string password)
    {
        Assert.Equal(PasswordStrengthLevel.Fair, PasswordStrength.Evaluate(password));
    }

    [Theory]
    [InlineData("Abcdef12!")]      // 长度+大小写+数字+符号，5 分
    [InlineData("Password123!")]   // 长密码全维度，6 分
    public void Evaluate_Strong_ReturnsStrong(string password)
    {
        Assert.Equal(PasswordStrengthLevel.Strong, PasswordStrength.Evaluate(password));
    }

    [Fact]
    public void Hint_EveryLevel_HasNonEmptyText()
    {
        foreach (var level in Enum.GetValues<PasswordStrengthLevel>())
            Assert.False(string.IsNullOrEmpty(PasswordStrength.Hint(level)));
    }
}
