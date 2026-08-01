namespace LinkRoom.Core.Tests;

public class LinkCodeServiceTests
{
    [Fact]
    public void Encode_RoomOnly_ProducesLinkroomUrl()
    {
        Assert.Equal("linkroom://link/ROOM123", LinkCodeService.Encode("ROOM123"));
    }

    [Fact]
    public void Encode_WithPassAndPort_AppendsQuery()
    {
        Assert.Equal(
            "linkroom://link/ROOM123?pass=secret&port=12345",
            LinkCodeService.Encode("ROOM123", "secret", 12345));
    }

    [Fact]
    public void Encode_TrimsRoomId_AndEscapesSpecialChars()
    {
        Assert.Equal("linkroom://link/ROOM%20123", LinkCodeService.Encode("  ROOM 123  "));
        Assert.Equal("linkroom://link/ROOM%2F1", LinkCodeService.Encode("ROOM/1"));
    }

    [Fact]
    public void Encode_WithLockSecret_AppendsLockQuery()
    {
        Assert.Equal(
            "linkroom://link/ROOM123?lock=abc%2Bdef%3D",
            LinkCodeService.Encode("ROOM123", lockSecret: "abc+def="));
    }

    [Fact]
    public void Encode_WithAllQuery_AppendsInOrder()
    {
        Assert.Equal(
            "linkroom://link/ROOM123?pass=secret&port=12345&lock=locksec",
            LinkCodeService.Encode("ROOM123", "secret", 12345, "locksec"));
    }

    [Fact]
    public void Encode_EmptyLockSecret_OmitsLockQuery()
    {
        Assert.Equal(
            "linkroom://link/ROOM123?pass=secret",
            LinkCodeService.Encode("ROOM123", "secret", null, ""));
    }

    [Fact]
    public void Decode_LinkUrl_RoundTrips()
    {
        const string roomId = "room123"; // 小写：Uri.Host 会规范化大小写
        const string password = "p@ss w0rd!";
        const int port = 12345;
        const string lockSecret = "abc+def=";

        var (decodedRoom, decodedPass, decodedPort, decodedLock) =
            LinkCodeService.Decode(LinkCodeService.Encode(roomId, password, port, lockSecret));

        Assert.Equal(roomId, decodedRoom);
        Assert.Equal(password, decodedPass);
        Assert.Equal(port, decodedPort);
        Assert.Equal(lockSecret, decodedLock);
    }

    [Fact]
    public void Decode_LinkUrl_UppercaseRoom_PreservesCase()
    {
        // Room ids live in the path now, so no host case-normalization (BUG-17).
        var (decodedRoom, decodedPass, decodedPort, decodedLock) =
            LinkCodeService.Decode(LinkCodeService.Encode("ABCD1234"));

        Assert.Equal("ABCD1234", decodedRoom);
        Assert.Null(decodedPass);
        Assert.Null(decodedPort);
        Assert.Null(decodedLock);
    }

    [Fact]
    public void Decode_LegacyHostFormat_PreservesCase()
    {
        // Old links put the raw room id in the host — read it back case-sensitively.
        var (room, pass, port, lockSecret) = LinkCodeService.Decode("linkroom://ABCD1234");

        Assert.Equal("ABCD1234", room);
        Assert.Null(pass);
        Assert.Null(port);
        Assert.Null(lockSecret);
    }

    [Fact]
    public void Decode_LegacyHostFormat_WithQuery()
    {
        var (room, pass, port, lockSecret) = LinkCodeService.Decode("linkroom://ABCD1234?pass=secret&port=8080");

        Assert.Equal("ABCD1234", room);
        Assert.Equal("secret", pass);
        Assert.Equal(8080, port);
        Assert.Null(lockSecret);
    }

    [Fact]
    public void Decode_LinkUrl_NoPass()
    {
        var (room, pass, port, lockSecret) = LinkCodeService.Decode("linkroom://room123");

        Assert.Equal("room123", room);
        Assert.Null(pass);
        Assert.Null(port);
        Assert.Null(lockSecret);
    }

    [Fact]
    public void Decode_LinkUrl_InvalidPort_YieldsNull()
    {
        var (_, _, port, _) = LinkCodeService.Decode("linkroom://room123?port=abc");

        Assert.Null(port);
    }

    [Fact]
    public void Decode_PlainFormat_RoomPassPort()
    {
        var (room, pass, port, lockSecret) = LinkCodeService.Decode("ROOMID:pass:8080");

        Assert.Equal("ROOMID", room);
        Assert.Equal("pass", pass);
        Assert.Equal(8080, port);
        Assert.Null(lockSecret);
    }

    [Fact]
    public void Decode_PlainFormat_RoomPassOnly()
    {
        var (room, pass, port, lockSecret) = LinkCodeService.Decode("ROOMID:pass");

        Assert.Equal("ROOMID", room);
        Assert.Equal("pass", pass);
        Assert.Null(port);
        Assert.Null(lockSecret);
    }

    [Fact]
    public void Decode_PlainFormat_RoomOnly()
    {
        var (room, pass, port, lockSecret) = LinkCodeService.Decode("ROOMID");

        Assert.Equal("ROOMID", room);
        Assert.Null(pass);
        Assert.Null(port);
        Assert.Null(lockSecret);
    }
}
