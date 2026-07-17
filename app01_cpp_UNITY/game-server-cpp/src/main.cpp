#include <cmath>
#include <cstdlib>
#include <cstring>
#include <iostream>

#include "NetworkServer.h"

namespace {

int failures = 0;

void Expect(bool condition, const char* what) {
    if (condition) {
        std::cout << "PASS: " << what << std::endl;
    } else {
        std::cout << "FAIL: " << what << std::endl;
        ++failures;
    }
}

void SendInput(NetworkServer& server, int clientId, uint32_t seq, float x, float y) {
    PlayerInputPacket packet{seq, x, y, false};
    server.HandleIncomingPacket(reinterpret_cast<const uint8_t*>(&packet), sizeof(packet), clientId);
}

// Offline self-test of the validation gates and authoritative state (no socket needed).
int RunSelfTest(NetworkServer& server) {
    const float step = PLAYER_SPEED * TICK_SECONDS;

    SendInput(server, 1, 1, 1.0f, 0.0f);
    const PlayerState* p1 = server.GetPlayerState(1);
    Expect(p1 && std::abs(p1->positionX - step) < 1e-6f, "valid input moves player by one server step");

    SendInput(server, 2, 1, 5.0f, 0.0f);
    Expect(server.GetPlayerState(2) == nullptr, "speed-hack input creates no state");

    uint8_t malformed[4] = {0, 1, 2, 3};
    server.HandleIncomingPacket(malformed, sizeof(malformed), 3);
    Expect(server.GetPlayerState(3) == nullptr, "malformed packet creates no state");

    SendInput(server, 1, 1, 1.0f, 0.0f); // replayed sequence number
    p1 = server.GetPlayerState(1);
    Expect(p1 && std::abs(p1->positionX - step) < 1e-6f, "replayed sequence is ignored");

    for (uint32_t seq = 2; seq < 20000; ++seq) {
        SendInput(server, 1, seq, 1.0f, 0.0f);
    }
    p1 = server.GetPlayerState(1);
    Expect(p1 && p1->positionX == ARENA_HALF_EXTENT, "position clamps at arena bound");

    std::cout << (failures == 0 ? "self-test OK" : "self-test FAILED") << std::endl;
    return failures == 0 ? 0 : 1;
}

} // namespace

int main(int argc, char** argv) {
    NetworkServer server;

    if (argc == 3 && std::strcmp(argv[1], "--listen") == 0) {
        int port = std::atoi(argv[2]);
        if (port <= 0 || port > 65535) {
            std::cerr << "Invalid port: " << argv[2] << std::endl;
            return 1;
        }
        return server.Run(static_cast<uint16_t>(port));
    }

    if (argc != 1) {
        std::cerr << "Usage: " << argv[0] << " [--listen <port>]" << std::endl;
        return 1;
    }

    return RunSelfTest(server);
}
