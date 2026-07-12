namespace LinkRoom.Core;

public enum PasswordStrengthLevel { Empty, Weak, Fair, Strong }

public static class PasswordStrength
{
    public static PasswordStrengthLevel Evaluate(string? password)
    {
        if (string.IsNullOrEmpty(password)) return PasswordStrengthLevel.Empty;
        var score = 0;
        if (password.Length >= 6) score++;
        if (password.Length >= 10) score++;
        if (password.Any(char.IsUpper)) score++;
        if (password.Any(char.IsLower)) score++;
        if (password.Any(char.IsDigit)) score++;
        if (password.Any(c => !char.IsLetterOrDigit(c))) score++;
        return score switch
        {
            <= 2 => PasswordStrengthLevel.Weak,
            <= 4 => PasswordStrengthLevel.Fair,
            _ => PasswordStrengthLevel.Strong,
        };
    }

    public static string Hint(PasswordStrengthLevel level) => level switch
    {
        PasswordStrengthLevel.Empty => "无密码（公开房间）",
        PasswordStrengthLevel.Weak => "密码较弱，建议增加长度和混合字符",
        PasswordStrengthLevel.Fair => "密码强度一般",
        PasswordStrengthLevel.Strong => "密码强度良好",
        _ => "",
    };
}
