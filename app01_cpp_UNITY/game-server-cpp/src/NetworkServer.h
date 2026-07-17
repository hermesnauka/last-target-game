#pragma once

#include <cstddef>
#include <cstdint>

constexpr size_t MAX_PACKET_SIZE = 512;

struct PlayerInputPacket {
    uint32_t sequenceNumber;
    float movementX;
    float movementY;
    bool isShooting;
};

class NetworkServer {
public:
    void HandleIncomingPacket(const uint8_t* rawBuffer, size_t bufferSize, int clientId);

private:
    void ProcessPlayerLogic(int clientId, const PlayerInputPacket& packet);
    void DropClient(int clientId);
    void FlagCheater(int clientId);
};
