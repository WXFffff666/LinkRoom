# EasyTier Secure Mode Spike — Evidence & Decision

**Task:** Add EasyTier `--secure-mode` / `[secure_mode]` support to LinkRoom
(E2EE + shared-node public-key pinning).

**Date:** 2026-08-02
**easytier-core version:** bundled at `src/LinkRoom.Core/Assets/easytier/easytier-core.exe` — `easytier-core 2.6.4-8428a89d`
**Spike host:** Windows PowerShell 5.1 (no admin / no TUN — `no_tun = true` everywhere)
**Author:** omo-opencode

## Conclusion (TL;DR)

**✅ Secure mode works via TOML config** — `[secure_mode] enabled = true` is
parsed and E2EE peer connections are established (Noise handshake).

**✅ `[[peer]] peer_public_key` pinning is enforced** — a wrong pinned key
causes the handshake to fail and the connection to be rejected (verified
empirically).

**✅ Non-secure nodes are rejected by secure networks** — a node without
`[secure_mode]` cannot hold a stable connection against a secure node
(verified empirically: TCP connects, then the peer is dropped and re-added in
a loop; it never appears in `peer list`).

**⚠️ Critical 2.6.4 finding — config-file path requires BOTH keys.**
When loading via `--config-file`, easytier-core does **NOT** auto-generate the
secure-mode keypair (the `normalize_secure_mode_config` auto-derivation only
runs on the CLI `--secure-mode` flag path and the credential path). With only
`enabled = true`, every outbound peer connection fails with
`"local private key is not set"`; with only a private key it fails with
`"local public key is not set"`. **LinkRoom must generate and persist an
X25519 keypair and write both `local_private_key` and `local_public_key` into
`[secure_mode]`.**

**ℹ️ Official public shared node has NO published pin key.**
`tcp://public.easytier.top:11010` does not publish a fixed public key (checked
EasyTier repo + docs + community clients). With an empty
`SharedNodePublicKey` LinkRoom ships "encrypted, not identity-verified"
(per the official docs, still far better than plaintext and immune to the
"knows the password can decrypt everything" problem of the old mode).

**Decision: proceed with Phase B.** Builder emits `[secure_mode]` (with the
persisted keypair) + optional `peer_public_key` on shared-node `[[peer]]`
entries. Default `EnableSecureMode = true`; docs note that all nodes in a
network must run the same version and all enable secure mode.

## Background — what secure mode changes

Per the official guide (https://easytier.cn/guide/network/secure-mode.html):

1. **E2EE** — each node pair negotiates independent encryption keys; even a
   shared relay cannot decrypt traffic (old mode: one shared `network_secret`
   encrypts everything, leak = full compromise).
2. **Noise protocol handshake** (WireGuard-style) with replay protection and
   session-key rotation.
3. **Shared-node identity verification** — pin the relay's public key so a MITM
   node cannot impersonate it.
4. Credentials for temporary guests (out of scope for LinkRoom).

TOML shape (confirmed by docs AND by easytier-core's own parsed-config dump):

```toml
[network_identity]
network_name = "office"
network_secret = "replace-with-a-strong-secret"

[secure_mode]
enabled = true
local_private_key = "<base64 X25519 private key>"
local_public_key = "<base64 X25519 public key>"

[[peer]]
uri = "tcp://relay.example.com:11010"
peer_public_key = "<relay-public-key-base64>"
```

`[secure_mode]` is a sibling of `[network_identity]`; `peer_public_key` lives
inside each `[[peer]]` table. Pin verification happens after the Noise
handshake: `PeerConfig.peer_public_key` (base64, 32 B) must match the remote
static public key, otherwise the connection is torn down (design doc:
`easytier/docs/peer_conn_secure_mode_v3.md` §7.6).

## Spike procedure

Workdir: `%TEMP%\opencode\secure-spike\`. All nodes: same `network_name`
(`spike-secure`) + `network_secret`, `no_tun = true`, distinct listeners
(12010/12011/12012) and distinct RPC portals (15888/15889/15890).

### Step 1 — TOML parse of `[secure_mode]`

`node-a.toml` with `[secure_mode] enabled = true` (no keys). easytier-core
startup dump (stdout):

```text
############### TOML ###############
listeners = ["tcp://0.0.0.0:12010"]

[network_identity]
network_name = "spike-secure"
network_secret = "spike-secret-1234"

[secure_mode]
enabled = true

[flags]
no_tun = true
-----------------------------------
INFO CORE::INSTANCE: new listener added listener=tcp://0.0.0.0:12010
```

`[secure_mode] enabled = true` is accepted verbatim. (Side observation: a
`listeners = [...]` key placed inside `[flags]` is silently dropped — it must
sit at the root of the TOML; LinkRoom already emits it inside `[flags]`, which
works only because 11010 is the implicit default port. Out of scope here.)

### Step 2 — `enabled = true` alone breaks outbound connects (2.6.4)

`node-b.toml` (secure enabled, no keys) connecting to node A:

```text
INFO CORE::INSTANCE::CONNECTION: connecting to peer dst=tcp://172.17.80.1:12010
INFO CORE::INSTANCE::CONNECTION: connect to peer error dst="tcp://172.17.80.1:12010" error=AnyhowError(
    "local private key is not set",
)
```

Adding only `local_private_key`:

```text
error=AnyhowError(
    "local public key is not set",
)
```

Root cause (from source): `easytier-core/src/config/mod.rs::normalize_secure_mode_config`
— which generates a random keypair and derives the public key from the private
key — is called **only** from the CLI entry (`easytier/src/core.rs`, the
`--secure-mode` flag) and the credential path (`api_input.rs`). The plain
config-file path never normalizes. Hence **both keys must be present in the
TOML**.

### Step 3 — secure + secure with keys → P2P connection established

Both nodes with a fixed keypair (`local_private_key` +
`local_public_key`), B pins A's public key (`peer_public_key`), B connects to
A on the LAN address:

```text
# node A peer list (port 15888)
| ipv4 | hostname        | cost | lat(ms) | loss | rx      | tx      | tunnel | NAT       | version        |
|------|-----------------|------|---------|------|---------|---------|--------|-----------|----------------|
|      | DESKTOP-BI0HNO7 | Local| -       | -    | -       | -       | -      | Symmetric | 2.6.4-8428a89d |
|      | DESKTOP-BI0HNO7 | p2p  | 0.29    | 0.0% | 7.51 kB | 8.16 kB | tcp    | Symmetric | 2.6.4-8428a89d |

# node B peer list (port 15889) — same, p2p / 0.24ms / tcp
```

Node A's log: `new peer connection added conn_info=PeerConnInfo { ... tunnel_type: "tcp", remote_addr: tcp://172.17.80.1:52036 }`,
`new peer added peer_id=...`. Real traffic flows (rx/tx kB) — the Noise
handshake completed and the pinned key matched.

### Step 4 — wrong pin → rejected

B's `peer_public_key` set to a bogus value (same URI). Result: B's peer list
never shows a peer; every connect attempt fails:

```text
INFO CORE::INSTANCE::CONNECTION: connect to peer error dst="tcp://172.17.80.1:12010" error=WaitRespError(
```

The Noise handshake never completes because the remote static public key does
not match the pin. **Pinning is enforced server-side-of-the-handshake.**

### Step 5 — node without secure mode → rejected

`node-c.toml` (no `[secure_mode]`) connecting to secure node A:

```text
INFO CORE::INSTANCE::CONNECTION: connecting to peer dst=tcp://172.17.80.1:12010
INFO CORE::INSTANCE::CONNECTION: new peer connection added ... tunnel_type: "tcp" ...
INFO CORE::INSTANCE::CONNECTION: peer connection removed conn_info=...
INFO CORE::INSTANCE::CONNECTION: new peer added peer_id=...
INFO CORE::INSTANCE::CONNECTION: peer connection removed conn_info=...
```

…repeating every second. C never stabilizes: TCP connects, the handshake
fails, and the peer is torn down and retried forever. `peer list` on A shows
only `Local`. **A non-secure node cannot join a secure network** (same-version
2.6.4). This matches the docs' upgrade note: "开启安全模式的客户端连不上旧服务端" —
all nodes in a network must consistently enable secure mode.

### Step 6 — C#-generated keypair (final end-to-end validation)

The first spike runs used a hand-rolled Python ladder that was later found to
be WRONG (it computed X25519(k, 1) instead of X25519(k, 9) — the config-file
path uses the explicit `local_public_key` as-is, so the wrong but
self-consistent pair still passed the pin check). The production C#
implementation in `SecureModeKeys` was therefore rewritten and verified
against RFC 7748 §6.1 (Alice/Bob, base point 9). Final validation run with
the C#-generated pair (`9lDmRk+...` / `Lmzd/Mdn...`) + pin:

```text
# node A peer list — p2p over tcp, real traffic
| ipv4 | hostname        | cost | lat(ms) | loss | rx      | tx      | tunnel | NAT       | version        |
|------|-----------------|------|---------|------|---------|---------|--------|-----------|----------------|
|      | DESKTOP-BI0HNO7 | Local| -       | -    | -       | -       | -      | Symmetric | 2.6.4-8428a89d |
|      | DESKTOP-BI0HNO7 | p2p  | 0.23    | 0.0% | 6.99 kB | 7.64 kB | tcp    | Symmetric | 2.6.4-8428a89d |
```

C# derivation also matched an independent Python oracle byte-for-byte.
`SecureModeKeysTests` locks the derivation with the RFC 7748 §6.1 vectors.

## Public shared node pin key

Searched: EasyTier repo (code + issues), docs, community clients
(PCL-CE, Terracotta, EasytierGame, luci-app-easytier, install scripts).
`public.easytier.top:11010` is referenced everywhere as a bare URI — **no
official fixed public key is published anywhere**. The official docs
explicitly say the key must be distributed over a trusted channel by the node
operator ("把对应的公钥通过可信渠道发给客户端").

→ LinkRoom's `SharedNodePublicKey` is optional; empty = encrypted but not
identity-verified. When the EasyTier team publishes an official key (or the
user runs their own shared node), they can paste it in.

## Implementation requirements derived from the spike

1. `[secure_mode]` must contain `enabled = true` **plus both keys** —
   otherwise every outbound connection fails on easytier-core 2.6.4.
2. LinkRoom generates an X25519 keypair once, persists the private key
   (`LinkRoomData/config/securemode.key`), derives the public key on demand
   (pure-C# ladder — no BCL X25519 in .NET 10, no NuGet allowed).
   A stable per-install identity is also what the docs recommend for shared
   nodes ("--local-private-key 建议显式固定，避免重启后公钥变化").
3. `[[peer]]` entries gain `peer_public_key` when the user configured one.
4. The private key must never be logged: easytier-core dumps the parsed TOML
   at startup, so `SettingsService.SanitizeLog` must redact
   `local_private_key`.
5. Default `EnableSecureMode = true`; README must state that all nodes in a
   network need consistent secure-mode (same LinkRoom/EasyTier version).
