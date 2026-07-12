using System.Text;

namespace LinkRoom.Core;

/// <summary>
/// Encodes/decodes linkroom:// share links and clipboard codes.
/// </summary>
public static class LinkCodeService
{
    public const string Scheme = "linkroom";

    public static string Encode(string roomId, string? password = null, int? port = null)
    {
        var sb = new StringBuilder($"{Scheme}://{Uri.EscapeDataString(roomId.Trim())}");
        var query = new List<string>();
        if (!string.IsNullOrEmpty(password)) query.Add($"pass={Uri.EscapeDataString(password)}");
        if (port is > 0) query.Add($"port={port}");
        if (query.Count > 0) sb.Append('?').Append(string.Join('&', query));
        return sb.ToString();
    }

    public static (string RoomId, string? Password, int? Port) Decode(string input)
    {
        input = input.Trim();
        if (input.StartsWith($"{Scheme}://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(input);
            var room = Uri.UnescapeDataString(uri.Host + uri.AbsolutePath).Trim('/');
            if (string.IsNullOrEmpty(room) && uri.Segments.Length > 1)
                room = Uri.UnescapeDataString(uri.Segments[^1].Trim('/'));
            var pass = GetQuery(uri.Query, "pass");
            int? port = int.TryParse(GetQuery(uri.Query, "port"), out var p) ? p : null;
            return (room, pass, port);
        }

        // Plain: ROOMID or ROOMID:pass or ROOMID:pass:port
        var parts = input.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length >= 1 && parts[0].Length >= 3)
        {
            var pass = parts.Length >= 2 ? parts[1] : null;
            int? port = parts.Length >= 3 && int.TryParse(parts[2], out var pt) ? pt : null;
            return (parts[0], pass, port);
        }

        return (input, null, null);
    }

    static string? GetQuery(string query, string key)
    {
        if (string.IsNullOrEmpty(query)) return null;
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals(key, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(kv[1]);
        }
        return null;
    }

    public static string ToClipboardText(string roomId, string? password, int? port = null)
    {
        var link = Encode(roomId, password, port);
        return $"LinkRoom 联机\n房间号: {roomId}\n{(string.IsNullOrEmpty(password) ? "" : $"密码: {password}\n")}链接: {link}";
    }
}
