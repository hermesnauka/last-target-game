#include "NetworkServer.h"

#include <arpa/inet.h>
#include <sys/socket.h>
#include <unistd.h>

#include <algorithm>
#include <cmath>
#include <cstring>
#include <ctime>
#include <iostream>

void NetworkServer::HandleIncomingPacket(const uint8_t* rawBuffer, size_t bufferSize, int clientId) {
    lastShotVictim_ = -1;

    // SECURITY GATE: Prevent Buffer Overflow/Underflow
    if (bufferSize != sizeof(PlayerInputPacket)) {
        std::cerr << "Security Alert: Invalid packet size from client " << clientId << std::endl;
        DropClient(clientId);
        return;
    }

    PlayerInputPacket packet;
    std::memcpy(&packet, rawBuffer, sizeof(PlayerInputPacket));

    // SECURITY GATE: Reject NaN/Inf and out-of-range vectors (speed hacks)
    if (!std::isfinite(packet.movementX) || !std::isfinite(packet.movementY) ||
        std::abs(packet.movementX) > 1.0f || std::abs(packet.movementY) > 1.0f) {
        std::cerr << "Security Alert: Out-of-bounds movement input from client " << clientId << std::endl;
        FlagCheater(clientId);
        return;
    }

    ProcessPlayerLogic(clientId, packet);
}

const PlayerState* NetworkServer::GetPlayerState(int clientId) const {
    auto it = players_.find(clientId);
    return it == players_.end() ? nullptr : &it->second;
}

bool NetworkServer::AllowPacket(uint32_t senderIp, int64_t nowSeconds) {
    RateWindow& window = rateWindows_[senderIp];
    if (window.windowStart != nowSeconds) {
        window.windowStart = nowSeconds;
        window.count = 0;
        window.alerted = false;
    }
    if (++window.count > MAX_PACKETS_PER_SECOND) {
        if (!window.alerted) {
            window.alerted = true;
            struct in_addr addr{};
            addr.s_addr = senderIp;
            std::cerr << "Security Alert: Rate limit exceeded by " << inet_ntoa(addr) << std::endl;
        }
        return false;
    }
    return true;
}

int NetworkServer::Run(uint16_t port) {
    int sock = socket(AF_INET, SOCK_DGRAM, 0);
    if (sock < 0) {
        std::cerr << "socket() failed: " << std::strerror(errno) << std::endl;
        return 1;
    }

    sockaddr_in bindAddr{};
    bindAddr.sin_family = AF_INET;
    bindAddr.sin_addr.s_addr = htonl(INADDR_LOOPBACK); // loopback only until DTLS lands (SR-2)
    bindAddr.sin_port = htons(port);
    if (bind(sock, reinterpret_cast<sockaddr*>(&bindAddr), sizeof(bindAddr)) < 0) {
        std::cerr << "bind() failed: " << std::strerror(errno) << std::endl;
        close(sock);
        return 1;
    }

    std::cout << "Listening on udp://127.0.0.1:" << port << std::endl;

    auto broadcastState = [this, sock](int playerId) {
        const PlayerState* state = GetPlayerState(playerId);
        if (!state) return;
        PlayerStatePacket snapshot{static_cast<uint32_t>(playerId), state->lastProcessedSequence,
                                   state->positionX, state->positionY, state->health};
        for (const auto& [id, addr] : clientAddrs_) {
            sendto(sock, &snapshot, sizeof(snapshot), 0,
                   reinterpret_cast<const sockaddr*>(&addr), sizeof(addr));
        }
    };

    uint8_t buffer[MAX_PACKET_SIZE];

    while (true) {
        sockaddr_in sender{};
        socklen_t senderLen = sizeof(sender);
        ssize_t received = recvfrom(sock, buffer, sizeof(buffer), 0,
                                    reinterpret_cast<sockaddr*>(&sender), &senderLen);
        if (received < 0) {
            std::cerr << "recvfrom() failed: " << std::strerror(errno) << std::endl;
            close(sock);
            return 1;
        }

        if (!AllowPacket(sender.sin_addr.s_addr, static_cast<int64_t>(time(nullptr)))) {
            continue; // SR-4: silently drop flood traffic after the first alert
        }

        // Client identity is IP:port until session tokens land (see STRIDE/Spoofing).
        int clientId = static_cast<int>(ntohs(sender.sin_port));
        clientAddrs_[clientId] = sender;
        HandleIncomingPacket(buffer, static_cast<size_t>(received), clientId);

        // FR-3 + correction path: everyone gets the sender's authoritative state;
        // a rejected input still broadcasts the unchanged snapshot.
        broadcastState(clientId);
        if (lastShotVictim_ >= 0) {
            broadcastState(lastShotVictim_);
        }
    }
}

void NetworkServer::ProcessPlayerLogic(int clientId, const PlayerInputPacket& packet) {
    PlayerState& state = players_[clientId];

    // Drop stale/replayed inputs; UDP reorders and attackers replay.
    if (state.lastProcessedSequence != 0 && packet.sequenceNumber <= state.lastProcessedSequence) {
        return;
    }
    state.lastProcessedSequence = packet.sequenceNumber;

    // Authoritative delta physics: server decides how far an input moves you.
    state.positionX = std::clamp(state.positionX + packet.movementX * PLAYER_SPEED * TICK_SECONDS,
                                 -ARENA_HALF_EXTENT, ARENA_HALF_EXTENT);
    state.positionY = std::clamp(state.positionY + packet.movementY * PLAYER_SPEED * TICK_SECONDS,
                                 -ARENA_HALF_EXTENT, ARENA_HALF_EXTENT);

    float magnitude = std::sqrt(packet.movementX * packet.movementX +
                                packet.movementY * packet.movementY);
    if (magnitude > 0.0f) {
        state.facingX = packet.movementX / magnitude;
        state.facingY = packet.movementY / magnitude;
    }

    if (packet.isShooting) {
        lastShotVictim_ = ResolveShot(clientId);
    }
}

int NetworkServer::ResolveShot(int shooterId) {
    const PlayerState& shooter = players_[shooterId];

    int victimId = -1;
    float closest = SHOT_RANGE + 1.0f;
    for (auto& [id, target] : players_) {
        if (id == shooterId) continue;

        float dx = target.positionX - shooter.positionX;
        float dy = target.positionY - shooter.positionY;
        float along = dx * shooter.facingX + dy * shooter.facingY; // distance along the shot ray
        if (along <= 0.0f || along > SHOT_RANGE) continue;

        float perpendicular = std::abs(dx * shooter.facingY - dy * shooter.facingX);
        if (perpendicular > HITBOX_RADIUS) continue;

        if (along < closest) {
            closest = along;
            victimId = id;
        }
    }

    if (victimId >= 0) {
        PlayerState& victim = players_[victimId];
        victim.health -= SHOT_DAMAGE;
        std::cout << "Hit: " << shooterId << " -> " << victimId
                  << " (health " << victim.health << ")" << std::endl;
        if (victim.health <= 0) {
            std::cout << "Kill: " << shooterId << " eliminated " << victimId << std::endl;
            victim.positionX = 0.0f;
            victim.positionY = 0.0f;
            victim.health = MAX_HEALTH;
        }
    }
    return victimId;
}

void NetworkServer::DropClient(int /*clientId*/) { /* Disconnect logic */ }

void NetworkServer::FlagCheater(int /*clientId*/) { /* Logging and banning logic */ }
