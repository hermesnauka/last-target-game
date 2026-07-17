#pragma once

#include <netinet/in.h>

#include <cstddef>
#include <cstdint>
#include <unordered_map>

constexpr size_t MAX_PACKET_SIZE = 512;

// SR-4: per-IP rate limit — max packets per fixed 1-second window.
constexpr uint32_t MAX_PACKETS_PER_SECOND = 60;

// SR-1: all movement and combat math lives server-side. Units per simulated tick.
constexpr float PLAYER_SPEED = 5.0f;
constexpr float TICK_SECONDS = 1.0f / 60.0f;
constexpr float ARENA_HALF_EXTENT = 100.0f; // arena spans [-100, 100] on both axes

constexpr float SHOT_RANGE = 50.0f;
constexpr float HITBOX_RADIUS = 0.5f;
constexpr int32_t SHOT_DAMAGE = 25;
constexpr int32_t MAX_HEALTH = 100;

// Wire formats shared with the Unity client (NetworkClient.cs, Pack = 1).
// Must stay byte-identical: little-endian, no padding.
#pragma pack(push, 1)
struct PlayerInputPacket {
    uint32_t sequenceNumber;
    float movementX;
    float movementY;
    bool isShooting;
};

// Authoritative snapshot broadcast to every client (FR-3, User Story 2).
struct PlayerStatePacket {
    uint32_t playerId;
    uint32_t lastProcessedSequence;
    float positionX;
    float positionY;
    int32_t health;
};
#pragma pack(pop)
static_assert(sizeof(PlayerInputPacket) == 13, "wire format must match the C# Pack=1 layout");
static_assert(sizeof(PlayerStatePacket) == 20, "wire format must match the C# Pack=1 layout");

struct PlayerState {
    float positionX = 0.0f;
    float positionY = 0.0f;
    float facingX = 1.0f; // last non-zero movement direction, normalized
    float facingY = 0.0f;
    int32_t health = MAX_HEALTH;
    uint32_t lastProcessedSequence = 0;
};

class NetworkServer {
public:
    void HandleIncomingPacket(const uint8_t* rawBuffer, size_t bufferSize, int clientId);

    // Blocking UDP receive loop; returns only on socket error.
    int Run(uint16_t port);

    // Authoritative state lookup (used by the self-test).
    const PlayerState* GetPlayerState(int clientId) const;

private:
    void ProcessPlayerLogic(int clientId, const PlayerInputPacket& packet);

    // Authoritative hit-scan from the shooter's position along its facing.
    // Returns the victim's clientId, or -1 on miss.
    int ResolveShot(int shooterId);

    void DropClient(int clientId);
    void FlagCheater(int clientId);

    // SR-4: returns false when the sender exceeded its per-second budget.
    bool AllowPacket(uint32_t senderIp, int64_t nowSeconds);

    struct RateWindow {
        int64_t windowStart = 0;
        uint32_t count = 0;
        bool alerted = false;
    };
    std::unordered_map<uint32_t, RateWindow> rateWindows_;
    std::unordered_map<int, PlayerState> players_;
    std::unordered_map<int, sockaddr_in> clientAddrs_; // for FR-3 broadcast

    // Victim of the most recent processed packet's shot (-1 if none); consumed by Run.
    int lastShotVictim_ = -1;
};
