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
    public void Decode_LinkUrl_RoundTrips()
    {
        const string roomId = "room123"; // 小写：Uri.Host 会规范化大小写
        const string password = "p@ss w0rd!";
        const int port = 12345;

        var (decodedRoom, decodedPass, decodedPort) =
            LinkCodeService.Decode(LinkCodeService.Encode(roomId, password, port));

        Assert.Equal(roomId, decodedRoom);
        Assert.Equal(password, decodedPass);
        Assert.Equal(port, decodedPort);
    }

    [Fact]
    public void Decode_LinkUrl_UppercaseRoom_PreservesCase()
    {
        // Room ids live in the path now, so no host case-normalization (BUG-17).
        var (decodedRoom, decodedPass, decodedPort) =
            LinkCodeService.Decode(LinkCodeService.Encode("ABCD1234"));

        Assert.Equal("ABCD1234", decodedRoom);
        Assert.Null(decodedPass);
        Assert.Null(decodedPort);
    }

    [Fact]
    public void Decode_LegacyHostFormat_PreservesCase()
    {
        // Old links put the raw room id in the host — read it back case-sensitively.
        var (room, pass, port) = LinkCodeService.Decode("linkroom://ABCD1234");

        Assert.Equal("ABCD1234", room);
        Assert.Null(pass);
        Assert.Null(port);
    }

    [Fact]
    public void Decode_LegacyHostFormat_WithQuery()
    {
        var (room, pass, port) = LinkCodeService.Decode("linkroom://ABCD1234?pass=secret&port=8080");

        Assert.Equal("ABCD1234", room);
        Assert.Equal("secret", pass);
        Assert.Equal(8080, port);
    }

    [Fact]
    public void Decode_LinkUrl_NoPass()
    {
        var (room, pass, port) = LinkCodeService.Decode("linkroom://room123");

        Assert.Equal("room123", room);
        Assert.Null(pass);
        Assert.Null(port);
    }

    [Fact]
    public void Decode_LinkUrl_InvalidPort_YieldsNull()
    {
        var (_, _, port) = LinkCodeService.Decode("linkroom://room123?port=abc");

        Assert.Null(port);
    }

    [Fact]
    public void Decode_PlainFormat_RoomPassPort()
    {
        var (room, pass, port) = LinkCodeService.Decode("ROOMID:pass:8080");

        Assert.Equal("ROOMID", room);
        Assert.Equal("pass", pass);
        Assert.Equal(8080, port);
    }

    [Fact]
    public void Decode_PlainFormat_RoomPassOnly()
    {
        var (room, pass, port) = LinkCodeService.Decode("ROOMID:pass");

        Assert.Equal("ROOMID", room);
        Assert.Equal("pass", pass);
        Assert.Null(port);
    }

    [Fact]
    public void Decode_PlainFormat_RoomOnly()
    {
        var (room, pass, port) = LinkCodeService.Decode("ROOMID");

        Assert.Equal("ROOMID", room);
        Assert.Null(pass);
        Assert.Null(port);
    }
}
