# app01_cpp_UNITY

2D multiplayer shooter: Unity client (C#) + authoritative C++ dedicated server.
Full PRD/threat model/SDLC phases: see `SSDLC_Unity_CPP_Shooter_Plan.md` in this folder — don't duplicate it here.

## Layout
- `game-server-cpp/` — CMake project, C++17. Authoritative game state (positions, health, hit detection).
- `game-client-unity/Assets/Scripts/` — Unity MonoBehaviours. Sends inputs only, never authoritative state.

## Invariants
- The C++ server is the sole authority on positions/health/hits (SR-1). Client scripts must only ever send input, never coordinates.
- `PlayerInputPacket` in `NetworkServer.h` and `NetworkClient.cs` must stay byte-identical (`Pack = 1` / same field order) — it's memcpy'd directly off the wire.
- Every packet handler validates size and input bounds before touching game state (see `NetworkServer::HandleIncomingPacket`).

## Build & test the server
```
cmake -S game-server-cpp -B game-server-cpp/build
cmake --build game-server-cpp/build
./game-server-cpp/build/game_server_cpp                  # offline self-test of validation gates
./game-server-cpp/build/game_server_cpp --listen 47777   # UDP loop (loopback-only until DTLS, SR-2)
```
