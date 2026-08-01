using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;

namespace LinkRoom.Core.Tests;

/// <summary>
/// 针对 EasyTierCliClient.RunCliAsync 的子进程输出处理回归测试：
/// BUG-3 stdout 流式 10MB 上限、BUG-4 stderr 并发读取防管道死锁。
/// 通过反射调用私有方法，用真实子进程（批处理 + PowerShell）产生输出。
/// </summary>
public class EasyTierCliClientTests
{
    [Fact]
    public async Task RunCliAsync_StdoutExceeds10Mb_StreamingCapKillsProcessAndThrows()
    {
        // BUG-3：mock 进程持续向 stdout 写超大输出且永不退出。
        // 修复前：先 ReadToEndAsync 全量读入再检查大小 → 内存失控；
        // 修复后：流式累计，超 10MB 立即 kill 进程并抛 InvalidOperationException。
        using var mock = new MockCli("flood-stdout.bat");
        mock.WriteCommand(
            $"powershell -NoProfile -ExecutionPolicy Bypass -Command " +
            $"\"$pid | Out-File -FilePath '{mock.PidFile}' -Encoding ascii; " +
            $"while ($true) {{ [Console]::Out.WriteLine('y' * 600) }}\"");
        var client = CreateClient(mock.BatPath);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeRunCliAsync(client))
            .WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Contains("10 MB", ex.Message);
        await AssertMockProcessKilledAsync(mock.PidFile);
    }

    [Fact]
    public async Task RunCliAsync_ChildFloodsStderr_TimeoutKillsInsteadOfPipeDeadlock()
    {
        // BUG-4：mock 先关闭 stdout（让旧的先读 stdout 逻辑通过），随后持续写
        // stderr（管道缓冲仅 ~4KB）。修复前：WaitForExitAsync 无超时保护，
        // 子进程被 stderr 管道堵死 → 永久死锁；修复后：stderr 并发读取，
        // 15s 超时 kill 抛 TimeoutException 而非死锁。
        using var mock = new MockCli("flood-stderr.bat");
        mock.WriteCommand(
            $"powershell -NoProfile -ExecutionPolicy Bypass -Command " +
            $"\"$pid | Out-File -FilePath '{mock.PidFile}' -Encoding ascii; " +
            $"[Console]::Out.Close(); while ($true) {{ [Console]::Error.WriteLine('e' * 500) }}\"");
        var client = CreateClient(mock.BatPath);

        var sw = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<TimeoutException>(() => InvokeRunCliAsync(client))
            .WaitAsync(TimeSpan.FromSeconds(35));
        sw.Stop();

        // 若回归为死锁（修复前行为），外层 WaitAsync(35s) 抛的"操作超时"
        // 与 RunCliAsync 自身的 15s 超时消息不同，此处断言可区分二者。
        Assert.Contains("timed out after 15 seconds", ex.Message);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"应 15s 内超时 kill，实际 {sw.Elapsed}");
        await AssertMockProcessKilledAsync(mock.PidFile);
    }

    [Fact]
    public async Task RunCliAsync_NormalExit_ReturnsCapturedStdout()
    {
        // 正常路径：流式改造后仍能完整返回 stdout。
        using var mock = new MockCli("ok.bat");
        mock.WriteCommand("echo []");
        var client = CreateClient(mock.BatPath);

        var output = await InvokeRunCliAsync(client).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal("[]\n", output);
    }

    [Fact]
    public async Task GetPeersAsync_ParsesRxTxAndLenientDashFields()
    {
        // spike（SPIKE-TRAFFIC.md §2.1）：真实 peer 输出中 lat_ms/loss_rate/
        // rx_bytes/tx_bytes 是字符串（"-"、"-"、"1.5 KB"），Local 行全为 "-"。
        // 验证 LenientNumberConverter 容忍 "-" 字符串，且 rx/tx 原始值被保留。
        var json = """
            [
              { "hostname": "DESKTOP-BI0HNO7", "cost": "Local", "lat_ms": "-", "loss_rate": "-", "rx_bytes": "-", "tx_bytes": "-", "nat_type": "Symmetric", "id": "123" },
              { "hostname": "PC2", "cost": "p2p", "lat_ms": "12.34", "loss_rate": "0.5%", "rx_bytes": "1.5 KB", "tx_bytes": "2.4 MB", "nat_type": "Open", "id": "456" }
            ]
            """;
        using var mock = new MockCli("peers.bat");
        mock.WriteStdout(json);
        var client = CreateClient(mock.BatPath);

        var peers = await client.GetPeersAsync().WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(2, peers.Length);
        // Local 行："-" 字符串 → 数字字段为 null，rx/tx 原始字符串保留
        Assert.Null(peers[0].LatencyMs);
        Assert.Null(peers[0].LossRate);
        Assert.Equal("-", peers[0].RxBytes);
        Assert.Equal("-", peers[0].TxBytes);
        // 远端行：字符串数字被解析；"0.5%" → 0.005（比率）；rx/tx 保留格式化字符串
        Assert.Equal(12.34, peers[1].LatencyMs!.Value, 2);
        Assert.Equal(0.005, peers[1].LossRate!.Value, 5);
        Assert.Equal("1.5 KB", peers[1].RxBytes);
        Assert.Equal("2.4 MB", peers[1].TxBytes);
    }

    [Fact]
    public async Task GetTrafficStatsAsync_ParsesRawByteCounters()
    {
        // spike（SPIKE-TRAFFIC.md §2.4）：stats show --output json 的 value 是
        // 原始 u64 字节计数，取 traffic_bytes_self_rx/self_tx。
        var json = """
            [
              { "name": "compression_bytes_rx_after", "value": 0, "labels": { "network_name": "n" } },
              { "name": "traffic_bytes_self_rx", "value": 1048576, "labels": { "network_name": "n" } },
              { "name": "traffic_bytes_self_tx", "value": 2048, "labels": { "network_name": "n" } },
              { "name": "traffic_bytes_rx", "value": 999, "labels": { "network_name": "n" } }
            ]
            """;
        using var mock = new MockCli("stats.bat");
        mock.WriteStdout(json);
        var client = CreateClient(mock.BatPath);

        var stats = await client.GetTrafficStatsAsync().WaitAsync(TimeSpan.FromSeconds(30));

        Assert.NotNull(stats);
        Assert.Equal(1048576UL, stats!.RxBytes);
        Assert.Equal(2048UL, stats.TxBytes);
    }

    [Fact]
    public async Task GetTrafficStatsAsync_MissingCounters_ReturnsNull()
    {
        // 老版本核心无 self_* 计数器且无 rx/tx 时 → null（降级，不虚构数据）。
        var json = """
            [
              { "name": "traffic_packets_rx", "value": 7, "labels": { "network_name": "n" } }
            ]
            """;
        using var mock = new MockCli("stats-empty.bat");
        mock.WriteStdout(json);
        var client = CreateClient(mock.BatPath);

        var stats = await client.GetTrafficStatsAsync().WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Null(stats);
    }

    [Fact]
    public void TrafficRateCalculator_ComputesDiffOverElapsed()
    {
        // (cur - prev) / elapsed：3s 轮询差值法
        Assert.Equal(341.3, TrafficRateCalculator.ComputeRate(1000, 2024, 3.0), 1);
    }

    [Fact]
    public void TrafficRateCalculator_FirstSampleOrReset_ReturnsZero()
    {
        // 首次采样无 prev → 0；计数器回绕（core 重启 cur < prev）→ 0，不闪错值
        Assert.Equal(0, TrafficRateCalculator.ComputeRate(null, 1000, 3.0));
        Assert.Equal(0, TrafficRateCalculator.ComputeRate(1000, 900, 3.0));
        Assert.Equal(0, TrafficRateCalculator.ComputeRate(1000, 2000, 0));
    }

    [Fact]
    public void TrafficRateCalculator_FormatsBelowKiloAsBytes()
    {
        Assert.Equal("512 B/s", TrafficRateCalculator.FormatRate(512));
        Assert.Equal("1.5 KB/s", TrafficRateCalculator.FormatRate(1536));
        Assert.Equal("0 B/s", TrafficRateCalculator.FormatRate(0));
    }

    private static EasyTierCliClient CreateClient(string mockCliPath)
        => new(mockCliPath, "127.0.0.1:15888", NullLogger<EasyTierCliClient>.Instance);

    private static Task<string> InvokeRunCliAsync(EasyTierCliClient client)
    {
        var method = typeof(EasyTierCliClient).GetMethod("RunCliAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("EasyTierCliClient.RunCliAsync 未找到");
        return (Task<string>)method.Invoke(client, new object[] { "peer", CancellationToken.None })!;
    }

    private static async Task AssertMockProcessKilledAsync(string pidFile)
    {
        // 等待 mock 启动并写入自己的 PID
        var deadline = DateTime.UtcNow.AddSeconds(15);
        int pid = 0;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(pidFile) && int.TryParse(File.ReadAllText(pidFile).Trim(), out pid) && pid > 0)
                break;
            await Task.Delay(100);
        }
        Assert.True(pid > 0, "mock 进程未写入 PID 文件");

        // 进程被 kill（entireProcessTree）后，按 PID 查询应失败或 HasExited
        deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (p.HasExited) return;
            }
            catch (ArgumentException) { return; }        // 进程已不存在
            catch (InvalidOperationException) { return; } // 进程已退出
            await Task.Delay(100);
        }
        Assert.Fail($"mock 进程 (PID {pid}) 未被 kill");
    }

    private sealed class MockCli : IDisposable
    {
        public MockCli(string fileName)
        {
            var dir = Path.Combine(Path.GetTempPath(), "LinkRoomCliTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            PidFile = Path.Combine(dir, "pid.txt");
            BatPath = Path.Combine(dir, fileName);
        }

        public string BatPath { get; }

        public string PidFile { get; }

        public void WriteCommand(string command) => File.WriteAllText(BatPath, "@echo off\r\n" + command + "\r\n");

        /// <summary>
        /// Emits arbitrary text from the mock CLI by writing it to a payload
        /// file and `type`-ing it — avoids batch echo escaping issues with
        /// quotes/percent in realistic JSON (e.g. "0.5%").
        /// </summary>
        public void WriteStdout(string content)
        {
            var payload = BatPath + ".out";
            File.WriteAllText(payload, content);
            WriteCommand($"type \"{payload}\"");
        }

        public void Dispose()
        {
            var dir = Path.GetDirectoryName(BatPath);
            if (dir != null && Directory.Exists(dir))
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
            }
        }
    }
}
