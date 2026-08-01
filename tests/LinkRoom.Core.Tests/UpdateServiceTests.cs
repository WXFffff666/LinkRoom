using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace LinkRoom.Core.Tests;

/// <summary>
/// UpdateService SHA256 完整性校验测试。
/// 通过 mock HttpMessageHandler 注入 HttpClient，不访问真实网络；
/// 数据目录重定向到测试 bin 下的 LinkRoomData（AppPaths.Configure(true)）。
/// </summary>
public class UpdateServiceTests
{
    const string Tag = "v1.99.1";
    const string ExeName = "LinkRoom-v1.99.1-win-x64.exe";
    const string ExeUrl = "https://github.com/WXFffff666/LinkRoom/releases/download/v1.99.1/LinkRoom-v1.99.1-win-x64.exe";

    static readonly byte[] ExeBytes = Encoding.UTF8.GetBytes("MZ fake exe payload for SHA256 integrity test");
    static readonly string ExeSha = ComputeSha(ExeBytes);

    public UpdateServiceTests() => AppPaths.Configure(portableMode: true);

    [Fact]
    public async Task DownloadAsync_ValidHash_SucceedsAndWritesExe()
    {
        var handler = ReleaseHandler($"{ExeSha}  {ExeName}\n"); // "<hash>  <filename>" (two spaces)
        var (svc, _) = CreateService(handler);
        try
        {
            var dest = await svc.DownloadAsync(Info());

            Assert.True(File.Exists(dest));
            Assert.Equal(ExeSha, ComputeSha(dest));
            Assert.Contains(handler.Requests, r => r.RequestUri!.AbsoluteUri.EndsWith(".exe.sha256"));
        }
        finally { CleanUpdateDir(); }
    }

    [Fact]
    public async Task DownloadAsync_ValidHash_BinaryMarkerFormat_Succeeds()
    {
        var handler = ReleaseHandler($"{ExeSha} *{ExeName}\n"); // "<hash> *<filename>" (binary marker)
        var (svc, _) = CreateService(handler);
        try
        {
            var dest = await svc.DownloadAsync(Info());

            Assert.True(File.Exists(dest));
            Assert.Equal(ExeSha, ComputeSha(dest));
        }
        finally { CleanUpdateDir(); }
    }

    [Fact]
    public async Task DownloadAsync_MismatchedHash_ThrowsAndRemovesExe()
    {
        var handler = ReleaseHandler($"{new string('f', 63)}e  {ExeName}\n"); // valid hex, wrong hash
        var (svc, _) = CreateService(handler);
        var dest = DestPath();
        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DownloadAsync(Info()));

            Assert.Contains("完整性校验失败", ex.Message);
            Assert.False(File.Exists(dest), "校验失败后 exe 必须被删除，绝不允许 fallback");
            Assert.False(File.Exists(dest + ".partial"));
        }
        finally { CleanUpdateDir(); }
    }

    [Fact]
    public async Task DownloadAsync_MissingShaFile_ThrowsAndRemovesExe()
    {
        var handler = new FakeHandler(req =>
            req.RequestUri!.AbsoluteUri.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? BytesResponse(ExeBytes)
                : NotFoundResponse());
        var (svc, _) = CreateService(handler);
        var dest = DestPath();
        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DownloadAsync(Info()));

            Assert.Contains("完整性校验失败", ex.Message);
            Assert.False(File.Exists(dest), ".sha256 缺失时必须明确报错并删除 exe，绝不 fallback");
        }
        finally { CleanUpdateDir(); }
    }

    [Fact]
    public async Task DownloadAsync_InvalidShaFormat_ThrowsAndRemovesExe()
    {
        var handler = ReleaseHandler("not-a-valid-sha256-checksum-file\n");
        var (svc, _) = CreateService(handler);
        var dest = DestPath();
        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DownloadAsync(Info()));

            Assert.Contains("完整性校验失败", ex.Message);
            Assert.Contains("格式非法", ex.Message);
            Assert.False(File.Exists(dest), ".sha256 格式非法时必须明确报错并删除 exe，绝不 fallback");
        }
        finally { CleanUpdateDir(); }
    }

    [Fact]
    public async Task CheckAsync_WithShaAssets_ResolvesExeDownloadUrl()
    {
        var releaseJson = $$"""
            {
              "tag_name": "v1.99.1",
              "body": "release notes",
              "assets": [
                { "name": "LinkRoom-v1.99.1-win-x64.exe",
                  "browser_download_url": "https://github.com/WXFffff666/LinkRoom/releases/download/v1.99.1/LinkRoom-v1.99.1-win-x64.exe",
                  "size": 123456 },
                { "name": "LinkRoom-v1.99.1-win-x64.exe.sha256",
                  "browser_download_url": "https://github.com/WXFffff666/LinkRoom/releases/download/v1.99.1/LinkRoom-v1.99.1-win-x64.exe.sha256",
                  "size": 100 }
              ]
            }
            """;
        var handler = new FakeHandler(_ => TextResponse(releaseJson));
        var (svc, _) = CreateService(handler);

        var result = await svc.CheckAsync();

        Assert.True(result.HasUpdate);
        Assert.NotNull(result.Info);
        Assert.Equal(ExeUrl, result.Info!.DownloadUrl);
        Assert.EndsWith(".exe", result.Info.DownloadUrl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".sha256", result.Info.DownloadUrl, StringComparison.OrdinalIgnoreCase);
    }

    static UpdateInfo Info() => new(Tag, "1.99.1", ExeUrl, ExeBytes.Length, null);

    static string DestPath() => Path.Combine(AppPaths.DataRoot, "update", ExeName);

    static (UpdateService Svc, FakeHandler Handler) CreateService(FakeHandler handler)
        => (new UpdateService(NullLogger<UpdateService>.Instance, new HttpClient(handler)), handler);

    static FakeHandler ReleaseHandler(string shaContent)
        => new(req =>
        {
            var u = req.RequestUri!.AbsoluteUri;
            if (u.EndsWith(".exe.sha256", StringComparison.OrdinalIgnoreCase)) return TextResponse(shaContent);
            if (u.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return BytesResponse(ExeBytes);
            return NotFoundResponse();
        });

    static void CleanUpdateDir()
    {
        try
        {
            var dir = Path.Combine(AppPaths.DataRoot, "update");
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch { /* best effort */ }
    }

    static string ComputeSha(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }

    static string ComputeSha(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs));
    }

    static HttpResponseMessage TextResponse(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    static HttpResponseMessage BytesResponse(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    static HttpResponseMessage NotFoundResponse() => new(HttpStatusCode.NotFound);

    private sealed class FakeHandler : HttpMessageHandler
    {
        readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }
}
