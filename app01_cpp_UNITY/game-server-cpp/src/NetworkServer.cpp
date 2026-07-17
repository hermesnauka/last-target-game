#include "NetworkServer.h"

#include <cmath>
#include <cstring>
#include <iostream>

void NetworkServer::HandleIncomingPacket(const uint8_t* rawBuffer, size_t bufferSize, int clientId) {
    // SECURITY GATE: Prevent Buffer Overflow/Underflow
    if (bufferSize != sizeof(PlayerInputPacket)) {
        std::cerr << "Security Alert: Invalid packet size from client " << clientId << std::endl;
        DropClient(clientId);
        return;
    }

    PlayerInputPacket packet;
    std::memcpy(&packet, rawBuffer, sizeof(PlayerInputPacket));

    // SECURITY GATE: Sanitize and Validate Inputs (Prevent speed hacks / invalid vectors)
    if (std::abs(packet.movementX) > 1.0f || std::abs(packet.movementY) > 1.0f) {
        std::cerr << "Security Alert: Out-of-bounds movement input from client " << clientId << std::endl;
        FlagCheater(clientId);
        return;
    }

    ProcessPlayerLogic(clientId, packet);
}

void NetworkServer::ProcessPlayerLogic(int clientId, const PlayerInputPacket& packet) {
    // Authoritative server moves the player based on validated delta physics
    std::cout << "Client " << clientId << " moved securely by X: " << packet.movementX << std::endl;
}

void NetworkServer::DropClient(int clientId) { /* Disconnect logic */ }

void NetworkServer::FlagCheater(int clientId) { /* Logging and banning logic */ }
