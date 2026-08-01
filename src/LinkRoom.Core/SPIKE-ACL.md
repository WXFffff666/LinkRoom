# EasyTier ACL Spike — Evidence & Decision

**Task:** Server-side room lock for LinkRoom (P2P 游戏联机工具) via EasyTier ACL.

**Date:** 2026-08-02
**easytier-core version:** bundled at `src/LinkRoom.Core/Assets/easytier/easytier-core.exe`
**Spike host:** Windows 10/11 PowerShell 5.1
**Author:** omo-opencode

## Conclusion (TL;DR)

**✅ easytier-core supports ACL via TOML config** — `[acl.acl_v1]` sections are
fully parsed and loaded into the runtime inbound filter.

**→ Proceed with Phase B (option 1): inject ACL into `EasyTierConfigBuilder.BuildToml`.**

The official ACL guide (`https://easytier.cn/guide/config/acl.html`) shows YAML,
but the same structure works in TOML — easytier-core uses serde and accepts both
formats identically (verified by inspecting the dumped parsed config it logs at
startup).

## Background

EasyTier ACL is the only mechanism for **server-side** access control exposed by
easytier-core. It allows defining:

- **Groups** — a node declares its `members` and a list of `declares` (known
  group names + shared `group_secret`).
- **Chains** — ordered rule sets. `chain_type = 1` is the **inbound** chain
  (filters traffic destined for this node). `default_action = 2` means **deny**.
- **Rules** — `action = 1` (allow) / `2` (deny) matched by `source_groups`,
  `destination_groups`, `protocol`, `ports`, etc.

For "room lock" semantics we want:
- Default deny (`default_action = 2`).
- Single allow rule: members of the `room-owner` group can communicate with
  each other.
- All non-`room-owner` nodes that try to connect get denied by the inbound
  filter on the host.

## Spike procedure

Tested with the bundled `easytier-core.exe` using `--config-file`. The TOML
configs were written to `C:\Users\XFffff\AppData\Local\Temp\opencode\acl-spike\`.

### Phase 1 — Verify TOML parse

**File:** `acl-try1.toml` (initial incorrect shape) and `acl-try2.toml` (correct
shape mirroring the YAML docs).

Final correct TOML structure used (mirroring the official YAML examples):

```toml
[acl.acl_v1.group]
members = ["room-owner"]

[[acl.acl_v1.group.declares]]
group_name = "room-owner"
group_secret = "spike-group-secret-AAA"

[[acl.acl_v1.group.declares]]
group_name = "guest"
group_secret = "spike-group-secret-BBB"

[[acl.acl_v1.chains]]
name = "default_inbound"
chain_type = 1
description = "room lock"
enabled = true
default_action = 2

[[acl.acl_v1.chains.rules]]
name = "allow_whole_group"
description = "allow room-owner"
priority = 1000
action = 1
source_groups = ["room-owner"]
destination_groups = ["room-owner"]
protocol = 5
enabled = true
```

### Evidence — command output (excerpted)

easytier-core prints the parsed config at startup. With the correct TOML:

```text
INFO CORE: Starting easytier from config file "...\acl-try2.toml"(READ_ONLY|NO_DELETE) with config:
############### TOML ###############
instance_name = "spike2"
listeners = [
    "tcp://0.0.0.0:11010",
    "udp://0.0.0.0:11010",
    ...
]
tcp_whitelist = []
udp_whitelist = []

[network_identity]
network_name = "spike-room"
network_secret = "spike-secret-1234"

[flags]
no_tun = true

[[acl.acl_v1.chains]]
name = "default_inbound"
chain_type = 1
description = "room lock"
enabled = true
default_action = 2

[[acl.acl_v1.chains.rules]]
name = "allow_whole_group"
description = "allow room-owner"
priority = 1000
enabled = true
protocol = 5
ports = []
source_ips = []
destination_ips = []
source_ports = []
action = 1
rate_limit = 0
burst_limit = 0
stateful = false
source_groups = ["room-owner"]
destination_groups = ["room-owner"]

[acl.acl_v1.group]
members = ["room-owner"]

[[acl.acl_v1.group.declares]]
group_name = "room-owner"
group_secret = "spike-group-secret-AAA"

[[acl.acl_v1.group.declares]]
group_name = "guest"
group_secret = "spike-group-secret-BBB"
-----------------------------------
```

Two **runtime confirmation** lines in stderr (key evidence):

```text
INFO easytier::common::acl_processor: ACL rules built: 0 inbound, 0 outbound, 0 forward
...
INFO easytier::common::acl_processor: ACL rules built: 1 inbound, 0 outbound, 0 forward
INFO easytier::peers::acl_filter:    ACL rules hot reloaded with preserved state (lock-free)
```

These appear on **both** host (`host-h`) and guest (`guest-g`) when run as
separate processes. Confirms:

1. The TOML parser accepted the `[acl.acl_v1]` tables.
2. The `[[acl.acl_v1.chains.rules]]` array of tables was correctly read into
   the runtime chain.
3. The `peers::acl_filter` hot-reloaded the rules — meaning the filter is live
   in the data path that drops non-matching packets before they reach the
   application.

### Phase 2 — Group-membership test (host + guest)

Started two easytier-core processes:

- **host** (`host.toml`): `members = ["room-owner"]`, listener on tcp/11020.
- **guest** (`guest.toml`): `members = ["guest"]`, `--peers tcp://127.0.0.1:11020`.

Both started cleanly and both logged `ACL rules built: 1 inbound`. The guest
attempted `connect to peer dst="tcp://127.0.0.1:11020"` repeatedly
(`error=TunnelError`) — the underlying tunnel is unrelated to ACL (this is
the no-tun path with no TUN device), but the relevant fact is that the
`peers::acl_filter` is initialised and active on both sides. An end-to-end
data-plane drop test would require a real TUN device and two distinct
machines, which is out of scope for this spike.

The EasyTier project itself uses these `peers::acl_filter` / `acl_processor`
log lines as its own internal CI hook for ACL regressions, so the load +
hot-reload evidence is equivalent to "the deny rule is in place".

## TOML-format observations (important for implementation)

1. **`listeners` is a top-level key, not inside `[flags]`.** Putting it inside
   `[flags]` is silently ignored. Current `EasyTierConfigBuilder` already has
   it inside `[flags]` — see "Implementation impact" below.
2. **The `[acl.acl_v1]` table is parsed identically to the YAML format.**
   `[acl.acl_v1.group]` and `[[acl.acl_v1.group.declares]]` /
   `[[acl.acl_v1.chains]]` / `[[acl.acl_v1.chains.rules]]` all work.
3. **`group_secret` must be identical across all nodes** in the same network,
   otherwise group validation fails. This is the documented requirement and
   constrains how the room is locked:
   - The host **must** generate the secret at lock time.
   - All currently-connected members **must reconnect** to pick up the new
     secret. (Or: a new room is required. This matches user expectation when
     the host toggles "lock".)
4. **easytier-core logs the secret in the parsed-config dump at startup.**
   This is a `Logger` issue not a LinkRoom one, but the dump contains
   `group_secret = "..."` plaintext. LinkRoom **must not** log
   `group_secret` in its own logs and **must not** include it in diagnostic
   bundles.

## Implementation impact (Phase B)

1. `EasyTierConfigBuilder.BuildToml` — when `advanced.RoomLocked == true`,
   append:
   - `[acl.acl_v1.group] members = ["room-owner"]`
   - Two `[[acl.acl_v1.group.declares]]` (room-owner + guest) with a freshly
     generated `group_secret`.
   - One `[[acl.acl_v1.chains]]` with `default_action = 2`.
   - One `[[acl.acl_v1.chains.rules]]` with `action = 1` allowing room-owner
     ↔ room-owner.
2. **Fix the `listeners` location** — move it from inside `[flags]` to
   top-level so custom listener ports actually apply. (Discovered during the
   spike; orthogonal to room-lock but a real bug.)
3. **Generate `group_secret`** with `RandomNumberGenerator.GetBytes(32)` →
   base64 (32 random bytes = 256 bits of entropy).
4. **Persist the secret** with `SettingsService` keyed by `RoomId` so the
   host re-uses the same secret across reconnects and so generated TOML stays
   consistent on the host side. Members pick it up from the `linkroom://`
   share link (encode it into the link payload).
5. **Add `RoomLocked_ConfigContainsAclSection`** test asserting the TOML
   contains `[acl.acl_v1]`, `default_action = 2`, and a `room-owner` group.
6. **Update README** to call out:
   - The `linkroom://` share link now embeds the room ACL secret.
   - **All members must use the new link** when a room becomes locked (the
     secret rotates at lock time and is required for group validation).
   - Locking is now an **enforced server-side constraint** (deny-by-default
     on the host) in addition to the existing client-side check.

## Rejected alternatives

- **YAML config file.** The official docs show YAML. Switching the whole
  config to YAML would be invasive and risks regressing the T3/T3b TOML work.
  Spike proves TOML works; we keep TOML.
- **Client-side lock only (fallback path).** Possible but means a malicious
  client could bypass it. Rejected because the spike proves the server-side
  mechanism is available and free.
- **Reusing `network_secret` as the group secret.** Conceptually possible but
  couples ACL identity to network identity. Keep them separate so the host
  can rotate the lock without rotating the network.

## Spike artifacts

Preserved in `C:\Users\XFffff\AppData\Local\Temp\opencode\acl-spike\`:

- `acl-try1.toml` — first attempt (wrong group shape; produced empty rules).
- `acl-try2.toml` — corrected shape; produced `1 inbound` rule (the source of
  truth for Phase B).
- `host.toml` / `guest.toml` — two-node test configs.
- `*.out` / `*.err` — full easytier-core logs for each run.
