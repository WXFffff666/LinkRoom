using System.Text.Json;

namespace LinkRoom.Core;

/// <summary>
/// A single chat message as carried over the wire (and shown in the UI).
/// </summary>
public sealed record ChatMessage(string From, DateTimeOffset Ts, string Msg);

/// <summary>
/// Thrown when a chat frame violates the protocol (e.g. message too large).
/// </summary>
public sealed class ChatFrameException(string message) : Exception(message);

/// <summary>
/// Wire protocol for the in-room chat: [2-byte little-endian frame length][UTF-8 JSON {from, ts, msg}].
/// Pure static codec — no I/O, unit-test friendly.
///
/// NOTE: the original spec asked for a 1-byte length prefix, but 1 byte (max 255)
/// cannot carry the required 4 KB message limit. A 2-byte little-endian prefix
/// keeps the frame compact while allowing the full 4 KB payload.
/// </summary>
public static class ChatProtocol
{
    /// <summary>UTF-8 byte limit for the JSON body (task requirement: 4 KB).</summary>
    public const int MaxMessageBytes = 4096;

    /// <summary>Size of the length prefix in bytes.</summary>
    public const int HeaderBytes = 2;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Encodes a message into a full wire frame (length prefix + JSON body).
    /// Throws <see cref="ChatFrameException"/> when the UTF-8 body exceeds
    /// <see cref="MaxMessageBytes"/> (over-limit messages are rejected).
    /// </summary>
    public static byte[] Encode(string from, DateTimeOffset ts, string msg)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new ChatMessage(from, ts, msg), Options);
        if (body.Length > MaxMessageBytes)
            throw new ChatFrameException($"消息超过 {MaxMessageBytes} 字节上限，拒绝发送");
        if (body.Length > ushort.MaxValue)
            throw new ChatFrameException($"消息体超过 {ushort.MaxValue} 字节，无法编码"); // 防御：MaxMessageBytes < ushort.MaxValue，实际不可达

        var frame = new byte[HeaderBytes + body.Length];
        frame[0] = (byte)(body.Length & 0xFF);
        frame[1] = (byte)((body.Length >> 8) & 0xFF);
        body.CopyTo(frame, HeaderBytes);
        return frame;
    }

    /// <summary>
    /// Decodes the JSON body (without the length prefix) into a message.
    /// Returns false for malformed/invalid frames — the caller must treat
    /// those as protocol violations and drop the connection.
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<byte> body, out ChatMessage? message)
    {
        message = null;
        if (body.Length is 0 or > MaxMessageBytes) return false;
        try
        {
            message = JsonSerializer.Deserialize<ChatMessage>(body, Options);
        }
        catch (JsonException)
        {
            return false;
        }
        return message is { From.Length: > 0, Msg.Length: > 0 };
    }
}
