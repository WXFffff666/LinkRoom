namespace LinkRoom.Core;

public static class ConfigValidator
{
    public static (bool Ok, string? Error) ValidateRoomId(string? roomId)
    {
        if (string.IsNullOrWhiteSpace(roomId)) return (false, "房间号不能为空");
        if (roomId.Length is < 3 or > 64) return (false, "房间号长度需 3-64 字符");
        if (roomId.Any(char.IsWhiteSpace)) return (false, "房间号不能含空格");
        return (true, null);
    }

    public static (bool Ok, string? Error) ValidatePort(int port)
    {
        if (port is < 1024 or > 65535) return (false, "端口范围 1024-65535");
        return (true, null);
    }

    public static (bool Ok, string? Error) ValidateMtu(int mtu)
    {
        if (mtu is < 576 or > 1500) return (false, "MTU 范围 576-1500");
        return (true, null);
    }

    public static (bool Ok, string? Error) ValidatePassword(string? pw)
    {
        if (pw != null && pw.Length > 128) return (false, "密码最长 128 字符");
        return (true, null);
    }

    public static (bool Ok, string? Error) ValidateAll(string? roomId, int port, int mtu, string? password)
    {
        foreach (var (ok, err) in new[] { ValidateRoomId(roomId), ValidatePort(port), ValidateMtu(mtu), ValidatePassword(password) })
            if (!ok) return (ok, err);
        return (true, null);
    }
}
