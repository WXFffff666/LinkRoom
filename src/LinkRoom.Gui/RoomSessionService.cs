using System.Security.Cryptography;
using System.Text;
using System.Windows;
using LinkRoom.Core;

namespace LinkRoom.Gui;

/// <summary>
/// Owns room lifecycle: create / join / disconnect / history / join-by-link,
/// plus id & password generation. Pure move out of MainViewModel — no logic
/// changes. Depends on ConnectFlowService for the actual connect and on the
/// MainViewModel reference for shared VM state.
/// </summary>
internal sealed class RoomSessionService
{
    readonly MainViewModel _vm;
    readonly ConnectFlowService _flow;
    readonly EasyTierProcessService _proc;
    readonly ConnectionStateMachine _sm;
    readonly ProcessGuardian _guardian;

    public RoomSessionService(
        MainViewModel vm, ConnectFlowService flow, EasyTierProcessService proc,
        ConnectionStateMachine sm, ProcessGuardian guardian)
    {
        _vm = vm; _flow = flow; _proc = proc; _sm = sm; _guardian = guardian;
    }

    internal async Task CreateRoomAsync()
    {
        try
        {
            var id = GenId();
            var pw = _vm.Window?.GetCreatePassword() ?? "";
            if (string.IsNullOrEmpty(pw)) { pw = GenPw(); _vm.Window?.SetPasswordText(pw); }
            _vm.RoomId = id;
            _vm.Password = pw;
            _vm.L($"创建房间: {id}");
            // Room lock (server-side via EasyTier ACL): generate a per-room
            // group_secret and embed it in the share link so guests can
            // authenticate against the host's inbound ACL chain. The secret is
            // persisted in AppSettings.RoomLockSecrets[RoomId] so the host
            // re-uses the same secret across reconnects (otherwise the lock
            // would rotate and previously-joined guests would be denied).
            var lockSecret = _vm.RoomLocked ? GenLockSecret() : null;
            if (lockSecret != null) _vm.LockSecret = lockSecret;
            var link = LinkCodeService.Encode(id, pw, _vm.GamePortHint, lockSecret);
            _vm.ShortLinkText = ShortLinkService.FormatShare(id, pw, _vm.GamePortHint);
            _vm.Window?.ShowCreatedRoom(id, link, link);

            try
            {
                Clipboard.SetText(LinkCodeService.ToClipboardText(id, pw, _vm.GamePortHint, lockSecret));
                _vm.L("联机信息已复制到剪贴板");
            }
            catch { }

            await _flow.ConnectInternalAsync(new RoomOptions { RoomId = id, Password = pw, AclSecret = lockSecret });
        }
        catch (Exception ex)
        {
            _vm.L($"创建失败: {ex.Message}");
            _vm.StatusText = "创建失败";
            _vm.StatusDetail = ex.Message;
        }
    }

    internal async Task ConnectAsync()
    {
        try
        {
            var decoded = LinkCodeService.Decode(_vm.RoomId.Trim());
            var rid = decoded.RoomId;
            if (!string.IsNullOrEmpty(decoded.Password)) _vm.Password = decoded.Password;
            if (decoded.Port is > 0) _vm.GamePortHint = decoded.Port;
            _vm.RoomId = rid;
            // The share link may carry a `lock=` query param (set by the host
            // when the room is locked). Forward it to EasyTierConfigBuilder so
            // the guest's TOML has the same group_secret as the host.
            _vm.LockSecret = decoded.LockSecret;

            _vm.L($"加入房间: {rid}");
            await _flow.ConnectInternalAsync(new RoomOptions { RoomId = rid, Password = _vm.Password, AclSecret = decoded.LockSecret });
        }
        catch (Exception ex)
        {
            _vm.L($"加入失败: {ex.Message}");
            _vm.StatusText = "加入失败";
            _vm.StatusDetail = ex.Message;
        }
    }

    internal async Task DisconnectAsync()
    {
        _guardian.Stop();
        await _flow.CancelMonitorAsync();
        await _vm.Chat.StopAsync();
        _sm.UserDisconnect();
        await _proc.StopAsync();
        EasyTierProcessService.KillOrphanProcesses();
        _vm.IsRelayMode = false;
        _vm.ConnectionQuality = "";
        _vm.PortForwardHint = "";
        _vm.PathDiagram = "";
        _vm.StatusText = "已断开";
        _vm.StatusDetail = "";
        _vm.ConnState = "Disconnected";
        _vm.L("已断开连接");
        _vm.ConnectCommand.NotifyCanExecuteChanged();
        _vm.DisconnectCommand.NotifyCanExecuteChanged();
    }

    internal async Task JoinHistoryRoomAsync(string? room)
    {
        if (string.IsNullOrWhiteSpace(room)) return;
        _vm.RoomId = room;
        await _vm.ConnectCommand.ExecuteAsync(null);
    }

    static string GenId()
    {
        var b = RandomNumberGenerator.GetBytes(8);
        var sb = new StringBuilder(8);
        const string c = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        for (int i = 0; i < 8; i++) sb.Append(c[b[i] % c.Length]);
        return sb.ToString();
    }

    static string GenPw()
    {
        var sb = new StringBuilder(8);
        const string c = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";
        // Rejection sampling: 256 % c.Length != 0 would bias low indexes via modulo (BUG-16).
        for (int i = 0; i < 8; i++)
        {
            byte b;
            do { b = RandomNumberGenerator.GetBytes(1)[0]; } while (b >= 256 - 256 % c.Length);
            sb.Append(c[b % c.Length]);
        }
        return sb.ToString();
    }

    // 256-bit per-room ACL group_secret, base64-encoded for the TOML/URI.
    // Must be identical on every node in the network — EasyTier uses it to
    // authenticate group membership; mismatched secrets fail validation and
    // the host's default-deny inbound chain drops the peer. Never log this.
    static string GenLockSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
