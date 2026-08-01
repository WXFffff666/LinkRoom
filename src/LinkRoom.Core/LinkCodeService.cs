using System.Text;

namespace LinkRoom.Core;

/// <summary>
/// Encodes/decodes linkroom:// share links and clipboard codes.
/// </summary>
public static class LinkCodeService
{
    public const string Scheme = "linkroom";

    public static string Encode(string roomId, string? password = null, int? port = null, string? lockSecret = null)
    {
        // Room id goes in the path, not the host: URI hosts are case-normalized
        // (lowercased) by parsers, but EasyTier network names are case-sensitive (BUG-17).
        var sb = new StringBuilder($"{Scheme}://link/{Uri.EscapeDataString(roomId.Trim())}");
        var query = new List<string>();
        if (!string.IsNullOrEmpty(password)) query.Add($"pass={Uri.EscapeDataString(password)}");
        if (port is > 0) query.Add($"port={port}");
        if (!string.IsNullOrEmpty(lockSecret)) query.Add($"lock={Uri.EscapeDataString(lockSecret)}");
        if (query.Count > 0) sb.Append('?').Append(string.Join('&', query));
        return sb.ToString();
    }

    public static (string RoomId, string? Password, int? Port, string? LockSecret) Decode(string input)
    {
        input = input.Trim();
        if (input.StartsWith($"{Scheme}://", StringComparison.OrdinalIgnoreCase))
        {
            // Parse manually instead of via new Uri(...).Host, which would
            // lowercase the room id (BUG-17). Legacy links put the raw room id
            // in the host; new links put it after "link/".
            var rest = input.Substring($"{Scheme}://".Length);
            var frag = rest.IndexOf('#');
            if (frag >= 0) rest = rest[..frag];
            var qIdx = rest.IndexOf('?');
            var path = (qIdx >= 0 ? rest[..qIdx] : rest).Trim('/');
            var query = qIdx >= 0 ? rest[qIdx..] : "";

            var room = path.StartsWith("link/", StringComparison.OrdinalIgnoreCase)
                ? Uri.UnescapeDataString(path["link/".Length..].Trim('/'))
                : Uri.UnescapeDataString(path); // legacy: host was the room id

            var pass = GetQuery(query, "pass");
            int? port = int.TryParse(GetQuery(query, "port"), out var p) ? p : null;
            var lockSecret = GetQuery(query, "lock");
            return (room, pass, port, lockSecret);
        }

        // Plain: ROOMID or ROOMID:pass or ROOMID:pass:port
        var parts = input.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length >= 1 && parts[0].Length >= 3)
        {
            var pass = parts.Length >= 2 ? parts[1] : null;
            int? port = parts.Length >= 3 && int.TryParse(parts[2], out var pt) ? pt : null;
            return (parts[0], pass, port, null);
        }

        return (input, null, null, null);
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

    public static string ToClipboardText(string roomId, string? password, int? port = null, string? lockSecret = null)
    {
        var link = Encode(roomId, password, port, lockSecret);
        return $"LinkRoom 联机\n房间号: {roomId}\n{(string.IsNullOrEmpty(password) ? "" : $"密码: {password}\n")}链接: {link}";
    }
}
