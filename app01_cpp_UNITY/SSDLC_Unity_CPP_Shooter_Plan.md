# Secure SDLC Project Plan: 2D Multiplayer Shooter (Unity & C++)

## Architecture Note on Unity & C++
**Technical Alignment:** While Unity primarily uses C# for its frontend/gameplay logic, using **C++** is an excellent choice for building a high-performance, **authoritative dedicated game server** or a native low-level networking plugin. In this plan, the architecture will consist of a **Unity Client (C#)** and a **Dedicated Network Server (C++)** to ensure maximum performance and security against cheating.

---

## Phase 1: Planning & Requirements (Inception)

### 1.1 Product Requirements Document (PRD)

#### Functional Requirements (FR)
*   **FR-1 (Matchmaking & Lobbies):** Players must be able to authenticate, create, or join public/private match lobbies.
*   **FR-2 (2D Gameplay):** Top-down or side-scrolling 2D movement, weapon switching, shooting, and collision detection.
*   **FR-3 (Multiplayer State Synchronization):** Real-time synchronization of player positions, health, shooting states, and kill feeds across a minimum of 8 players per match.
*   **FR-4 (Leaderboard):** Real-time tracking of scores (Kills/Deaths) during and after the match.

#### Non-Functional & Security Requirements (SR)
*   **SR-1 (Authoritative Server Model):** The C++ server must be the absolute authority on game state (positions, collisions, hits). The Unity client only sends inputs, not coordinates, to prevent speed-hacks and wall-hacks.
*   **SR-2 (Secure Transport Layer):** All network traffic must use a secure protocol (e.g., DTLS or encrypted UDP via ENet/OpenSSL) to prevent eavesdropping and tampering.
*   **SR-3 (Input Validation):** The C++ server must strictly validate all incoming network packets to prevent Buffer Overflow attacks and Remote Code Execution (RCE).
*   **SR-4 (Rate Limiting):** The server must implement network throttling per client IP to mitigate Denial of Service (DoS) attacks.

---

### 1.2 User Stories

#### **User Story 1: Secure Authentication**
*   **As a** player,
*   **I want to** log in securely using a username and password,
*   **So that** my profile, stats, and match history are protected.
*   **Acceptance Criteria:**
    *   Passwords must be hashed using bcrypt/Argon2 before storage.
    *   Authentication tokens (JWT) must expire after 24 hours.
    *   Failed login attempts must be rate-limited (max 5 attempts per minute per IP).

#### **User Story 2: Authoritative Shooting Mechanics**
*   **As a** player,
*   **I want to** shoot at an enemy player and have the hit register accurately,
*   **So that** the gameplay feels fair and free from cheaters.
*   **Acceptance Criteria:**
    *   The Unity client plays local particle effects instantly (client prediction).
    *   The C++ server calculates the raycast/collision using historical bounding boxes (lag compensation).
    *   If the server confirms the hit, it updates health and broadcasts it; otherwise, the client is corrected.

---

## Phase 2: Architecture & Design (Secure Design)

### 2.1 Threat Modeling (STRIDE Matrix)

| Threat Category | Project Threat | Mitigation Strategy |
| :--- | :--- | :--- |
| **Spoofing** | Cheater impersonating another player or server. | Implement cryptographic session tokens (JWT) validated on every packet header. |
| **Tampering** | Modifying memory via CheatEngine to change health/ammo. | Keep health and ammo variables *only* on the C++ server. Client values are purely visual. |
| **Information Disclosure** | Map-hacking (seeing enemies through walls/fog of war). | **Area of Interest (AoI):** The server only sends positional data of enemies who are within the player's potential line of sight. |
| **Denial of Service** | Packet flooding to crash the match server. | Implement network-level rate limiting and packet size validation in C++. |

---

## Phase 3: Implementation (Secure Coding)

### 3.1 Directory Structure
```text
├── game-client-unity/
│   ├── Assets/
│   │   ├── Scripts/
│   │   │   ├── NetworkClient.cs
│   │   │   └── PlayerController.cs
├── game-server-cpp/
│   ├── src/
│   │   ├── main.cpp
│   │   ├── NetworkServer.cpp
│   │   └── NetworkServer.h
│   ├── include/
│   └── CMakeLists.txt
```

### 3.2 Secure Foundation Code Samples

#### **C++ Authoritative Server: Packet Validation (`NetworkServer.cpp`)**
```cpp
#include <iostream>
#include <vector>
#include <cstring>

// Define strict packet constraints to prevent buffer overflows
const size_t MAX_PACKET_SIZE = 512;

struct PlayerInputPacket {
    uint32_t sequenceNumber;
    float movementX;
    float movementY;
    bool isShooting;
};

class NetworkServer {
public:
    void HandleIncomingPacket(const uint8_t* rawBuffer, size_t bufferSize, int clientId) {
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

private:
    void ProcessPlayerLogic(int clientId, const PlayerInputPacket& packet) {
        // Authoritative server moves the player based on validated delta physics
        std::cout << "Client " << clientId << " moved securely by X: " << packet.movementX << std::endl;
    }

    void DropClient(int clientId) { /* Disconnect logic */ }
    void FlagCheater(int clientId) { /* Logging and banning logic */ }
};
```

#### **Unity Client: Secure Input Forwarder (`NetworkClient.cs`)**
```csharp
using System;
using System.Runtime.InteropServices;
using UnityEngine;

public class NetworkClient : MonoBehaviour
{
    // Struct matching the C++ server layout exactly
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PlayerInputPacket
    {
        public uint sequenceNumber;
        public float movementX;
        public float movementY;
        public byte isShooting; // Using byte for reliable cross-platform serialization
    }

    private uint seqCounter = 0;

    void Update()
    {
        float moveX = Input.GetAxisRaw("Horizontal"); // Returns -1, 0, or 1
        float moveY = Input.GetAxisRaw("Vertical");
        bool shoot = Input.GetButton("Fire1");

        SendInputToServer(moveX, moveY, shoot);
    }

    private void SendInputToServer(float x, float y, bool shoot)
    {
        PlayerInputPacket packet = new PlayerInputPacket
        {
            sequenceNumber = seqCounter++,
            movementX = Mathf.Clamp(x, -1f, 1f), // Clean data locally before sending
            movementY = Mathf.Clamp(y, -1f, 1f),
            isShooting = shoot ? (byte)1 : (byte)0
        };

        byte[] rawData = StructureToByteArray(packet);
        
        // Network Transport Layer (e.g., Telepathy, ENet, or custom C++ plugin wrapper)
        // Transport.Send(rawData);
    }

    private byte[] StructureToByteArray(object obj)
    {
        int len = Marshal.SizeOf(obj);
        byte[] arr = new byte[len];
        IntPtr ptr = Marshal.AllocHGlobal(len);
        Marshal.StructureToPtr(obj, ptr, true);
        Marshal.Copy(ptr, arr, 0, len);
        Marshal.FreeHGlobal(ptr);
        return arr;
    }
}
```

---

## Phase 4: Verification & Testing (Security Assessment)

*   **Static Application Security Testing (SAST):**
    *   Run **SonarQube** or **Clang-Tidy** on the C++ server code to detect memory leaks, uninitialized variables, and unsafe functions (`strcpy`, `memcpy` without size bounds).
*   **Dynamic Application Security Testing (DAST) / Fuzzing:**
    *   Use fuzz testing tools (like **AFL++**) on the C++ server network port by flooding it with corrupted, malformed, or massive packets to guarantee it handles bad data without crashing.
*   **Cheat Simulation (Penetration Testing):**
    *   Attempt to modify memory values locally on the Unity client using tools like CheatEngine to verify that the C++ server rejects unauthorized health adjustments or instant teleportation.

---

## Phase 5: Deployment & Maintenance (Operations)

*   **CI/CD Pipeline Security Gates:**
    *   Integrate automated vulnerability scanning for C++ dynamic libraries (`.dll` / `.so`) and external Unity packages. If a High/Critical vulnerability is discovered, the build fails automatically.
*   **Production Monitoring:**
    *   Implement centralized logging (e.g., ELK Stack) tracking unexpected client disconnects, packet size anomalies, and repeated failed authentication vectors to catch active DDoS or exploitation attempts in real time.
