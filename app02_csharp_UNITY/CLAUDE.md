# app02_csharp_UNITY

C# port of app01: Unity client (C#) + authoritative .NET dedicated server.
Same design as `../app01_cpp_UNITY/SSDLC_Unity_CPP_Shooter_Plan.md`; same wire protocol — the two servers are interchangeable behind the same Python UDP tests.

## Layout
- `game-server-csharp/` — .NET 10 console app (`GameServer.csproj`). Authoritative game state (positions, health, hit detection, FR-3 broadcast).
- `game-client-unity/Assets/Scripts/` — Unity MonoBehaviours, identical to app01's (inputs only, never authoritative state).

## Invariants
- Same as app01: server is sole authority (SR-1); wire formats are 13-byte `PlayerInputPacket` and 20-byte `PlayerStatePacket`, little-endian, no padding. Here they're read/written explicitly via `BinaryPrimitives` in `NetworkServer.cs` — keep field order in sync with `NetworkClient.cs` and with app01's `NetworkServer.h`.
- `TreatWarningsAsErrors` + `latest-recommended` analyzers are the Phase 4 SAST gate (parity with app01's `-Werror -fanalyzer`). Don't turn them off to make a build pass.

## Build & test
```
export DOTNET_ROOT=$HOME/.dotnet   # SDK is user-local, not on PATH
~/.dotnet/dotnet build game-server-csharp
~/.dotnet/dotnet run --project game-server-csharp                    # offline self-test
~/.dotnet/dotnet run --project game-server-csharp -- --listen 47777  # UDP loop (loopback-only, SR-2)
```
