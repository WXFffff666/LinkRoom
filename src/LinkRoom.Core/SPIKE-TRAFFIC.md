# SPIKE-TRAFFIC — easytier-cli rx/tx 流量字段实测

日期: 2026-08-02
二进制: `src/LinkRoom.Core/Assets/easytier/easytier-cli.exe` v2.6.4-8428a89d（与 fetch-easytier.ps1 默认 v2.6.4 一致）
方法: 本地起 easytier-core（`--no-tun`）双实例，实测各 CLI 子命令 JSON 输出；另核对 EasyTier 源码
`easytier/src/easytier-cli.rs`、`easytier-proto/src/api.rs`（GitHub EasyTier/EasyTier @ main）。

## 1. 结论摘要

| 数据源 | 是否含 rx/tx | 格式 | 单位 |
|---|---|---|---|
| `peer --output json` | ✅ 含 `rx_bytes` / `tx_bytes` | **字符串**（`format_size` 人类可读，如 `"0 B"`/`"1.5 KB"`/`"-"`） | 字节（格式化后） |
| `node info --output json` | ❌ 无 rx/tx 字段 | — | — |
| `-v peer list`（verbose json） | ✅ 含原始 `PeerRoutePair`（嵌套 `peer.conns[].stats.rx_bytes/tx_bytes`，u64） | **JSON 数字** | 字节 |
| `stats show --output json` | ✅ 含 `traffic_bytes_self_rx/self_tx` 等 | **JSON 数字**（`value: u64`） | 字节 |

## 2. 实测证据

### 2.1 `peer --output json`（本机单节点）
```json
[
  {
    "cidr": "",
    "ipv4": "",
    "hostname": "DESKTOP-BI0HNO7",
    "cost": "Local",
    "lat_ms": "-",
    "loss_rate": "-",
    "rx_bytes": "-",
    "tx_bytes": "-",
    "tunnel_proto": "-",
    "nat_type": "Symmetric",
    "id": "2374775084",
    "version": "2.6.4-8428a89d"
  }
]
```
- **`rx_bytes`/`tx_bytes` 字段存在**，但值为**格式化字符串**：`"-"`（Local 行/无流量时）。
- 源码佐证（easytier-cli.rs `PeerTableItem::from(PeerRoutePair)`）：
  `rx_bytes: format_size(p.get_rx_bytes().unwrap_or(0), humansize::DECIMAL)` —
  即 `humansize` 的十进制人类可读格式（`"1.5 KB"`、`"2.4 MB"`、`"0 B"`）。
- 双节点连接实测未达成（本机无 TUN + 直连被 WSAE10049 拒绝，见 §4），但字段命名与
  源码确认一致；源码中远端 peer 行的 `rx_bytes` 即 `format_size(...)` 字符串。

### 2.2 `node info --output json`
```json
{
  "peer_id": 2374775084,
  "ipv4_addr": "",
  "proxy_cidrs": [],
  "hostname": "DESKTOP-BI0HNO7",
  "stun_info": { ... },
  "inst_id": "f9e95acc-...",
  "listeners": [...],
  "config": "...",
  "version": "2.6.4-8428a89d",
  "feature_flag": { ... },
  "ip_list": { ... },
  ...
}
```
- **NodeInfo 无 rx/tx 字段**。协议层 `NodeInfo`（api.proto → api.rs）只有
  peer_id/ipv4_addr/hostname/stun_info/inst_id/listeners/config/version/feature_flag/ip_list。
- 结论：**NodeInfo 不需要也不应加 RxBytes/TxBytes**。

### 2.3 `-v peer list`（verbose json）
```json
[]
```
- verbose 路径序列化原始 `Vec<PeerRoutePair>`（`PeerRoutePair{ route, peer }`），peer 内
  `conns[].stats.rx_bytes/tx_bytes` 为 **u64 原始字节**（源码：`get_rx_bytes() -> Option<u64>`，
  `ret += stats.rx_bytes`）。
- 结构深嵌套且无 peer 连接时为空数组；可作备选，但解析复杂。

### 2.4 `stats show --output json`（推荐数据源）
```json
[
  { "name": "traffic_bytes_self_rx",  "value": 0, "labels": { "network_name": "spike-traffic" } },
  { "name": "traffic_bytes_self_tx",  "value": 0, "labels": { "network_name": "spike-traffic" } },
  { "name": "traffic_bytes_rx",       "value": 0, "labels": { ... } },
  { "name": "traffic_bytes_tx",       "value": 0, "labels": { ... } },
  { "name": "traffic_packets_self_rx","value": 0, "labels": { ... } },
  ...
]
```
- **`value` 为 u64 原始字节数**，可直接做差值速率计算。
- 关键指标（源码 `easytier-core/src/foundation/stats.rs` MetricName）：
  - `traffic_bytes_self_rx` / `traffic_bytes_self_tx` — 本实例自身收/发字节
  - `traffic_bytes_rx` / `traffic_bytes_tx` — 实例总收/发字节（含转发）
- 语义：面板展示本机流量用 `traffic_bytes_self_rx/self_tx`（自身收/发），差值法
  `(cur - prev) / 3.0` 即 bytes/s。

## 3. 降级判断

- **不降级**：`stats show --output json` 可用且提供原始字节计数，速率统计可行。
- Peer JSON 的 rx/tx 是格式化字符串（非数字），**不能直接反序列化为 ulong**（会抛
  JsonException）；因此 PeerInfo 上 rx/tx 用 `string?` 保留原值即可，速率计算改用
  `stats show` 的 u64 计数器（更精确且免字符串解析）。
- 若 `stats show` 失败（如老版本核心无此子命令）：MonitorAsync 捕获异常，速率保持 0，
  面板不显示速率段（不虚构数据）。

## 4. 环境限制说明（不影响结论）

- 本机无 TUN 设备且非管理员，双节点直连报 `WSAE10049`（`AddrNotAvailable`），未能
  现场观测到非零流量值；但 `stats show` 的零值输出 + 上游源码（字段名、类型、语义）
  已足够确认字段与格式。
- `-v peer list` 在无 peer 时输出 `[]`，无法现场验证嵌套 u64 结构，但源码已确认。

## 5. 实现落点（对 Phase B 的指引）

1. `EasyTierCliClient`：
   - `PeerInfo` 增加 `[JsonPropertyName("rx_bytes")] public string? RxBytes` +
     `TxBytes`（保留原始格式化字符串，不破坏现有反序列化）。
   - `NodeInfo` 不加 rx/tx（spike 证实无此字段）。
   - 新增 `GetTrafficStatsAsync()`：`stats show` → 解析 `TrafficMetric[]`，
     取 `traffic_bytes_self_rx/self_tx` 的 `value`（u64?）。
2. `ConnectFlowService.MonitorAsync`：每 3s 采样一次统计，`TrafficRateCalculator` 纯函数
   算 `(cur - prev) / elapsed` bytes/s；首次采样 prev 为空 → 0。
3. UI：ConnectionQuality 追加 `↓ x KB/s ↑ y KB/s`（<1024 显示 B/s）。
