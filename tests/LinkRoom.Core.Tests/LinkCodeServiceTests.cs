namespace LinkRoom.Core.Tests;

public class LinkCodeServiceTests
{
    [Fact]
    public void Encode_RoomOnly_ProducesLinkroomUrl()
    {
        Assert.Equal("linkroom://ROOM123", LinkCodeService.Encode("ROOM123"));
    }

    [Fact]
    public void Encode_WithPassAndPort_AppendsQuery()
    {
        Assert.Equal(
            "linkroom://ROOM123?pass=secret&port=12345",
            LinkCodeService.Encode("ROOM123", "secret", 12345));
    }

    [Fact]
    public void Encode_TrimsRoomId_AndEscapesSpecialChars()
    {
        Assert.Equal("linkroom://ROOM%20123", LinkCodeService.Encode("  ROOM 123  "));
        Assert.Equal("linkroom://ROOM%2F1", LinkCodeService.Encode("ROOM/1"));
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
