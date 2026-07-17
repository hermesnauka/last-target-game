#include <cstring>

#include "NetworkServer.h"

int main() {
    NetworkServer server;

    PlayerInputPacket validPacket{1, 0.5f, -0.3f, true};
    server.HandleIncomingPacket(reinterpret_cast<const uint8_t*>(&validPacket), sizeof(validPacket), 1);

    PlayerInputPacket cheatPacket{2, 5.0f, 0.0f, false};
    server.HandleIncomingPacket(reinterpret_cast<const uint8_t*>(&cheatPacket), sizeof(cheatPacket), 2);

    uint8_t malformed[4] = {0, 1, 2, 3};
    server.HandleIncomingPacket(malformed, sizeof(malformed), 3);

    return 0;
}
