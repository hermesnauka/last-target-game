using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace GameServer;

public static class GameConstants
{
    // SR-4: per-IP rate limit — max packets per fixed 1-second window.
    public const uint MaxPacketsPerSecond = 60;

    // SR-1: all movement and combat math lives server-side. Units per simulated tick.
    public const float PlayerSpeed = 5.0f;
    public const float TickSeconds = 1.0f / 60.0f;
    public const float ArenaHalfExtent = 100.0f; // arena spans [-100, 100] on both axes

    public const float ShotRange = 50.0f;
    public const float HitboxRadius = 0.5f;
    public const int ShotDamage = 25;
    public const int MaxHealth = 100;

    // Wire sizes shared with the Unity client (NetworkClient.cs, Pack = 1).
    public const int InputPacketSize = 13;
    public const int StatePacketSize = 20;
}

// Wire format: <uint seq><float x><float y><byte shooting>, little-endian, 13 bytes.
public readonly record struct PlayerInputPacket(uint SequenceNumber, float MovementX, float MovementY, bool IsShooting)
{
    public static PlayerInputPacket Parse(ReadOnlySpan<byte> buffer) => new(
        BinaryPrimitives.ReadUInt32LittleEndian(buffer),
        BinaryPrimitives.ReadSingleLittleEndian(buffer[4..]),
        BinaryPrimitives.ReadSingleLittleEndian(buffer[8..]),
        buffer[12] != 0);
}

public sealed class PlayerState
{
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float FacingX { get; set; } = 1.0f; // last non-zero movement direction, normalized
    public float FacingY { get; set; }
    public int Health { get; set; } = GameConstants.MaxHealth;
    public uint LastProcessedSequence { get; set; }

    // Authoritative snapshot broadcast to every client (FR-3, User Story 2): 20 bytes.
    public void WriteSnapshot(Span<byte> buffer, int playerId)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, (uint)playerId);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[4..], LastProcessedSequence);
        BinaryPrimitives.WriteSingleLittleEndian(buffer[8..], PositionX);
        BinaryPrimitives.WriteSingleLittleEndian(buffer[12..], PositionY);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[16..], Health);
    }
}

public sealed class NetworkServer
{
    private sealed class RateWindow
    {
        public long WindowStart;
        public uint Count;
        public bool Alerted;
    }

    private readonly Dictionary<uint, RateWindow> _rateWindows = new();
    private readonly Dictionary<int, PlayerState> _players = new();
    private readonly Dictionary<int, IPEndPoint> _clientAddrs = new(); // for FR-3 broadcast

    // Victim of the most recent processed packet's shot (-1 if none); consumed by Run.
    private int _lastShotVictim = -1;

    public PlayerState? GetPlayerState(int clientId) =>
        _players.TryGetValue(clientId, out PlayerState? state) ? state : null;

    public void HandleIncomingPacket(ReadOnlySpan<byte> buffer, int clientId)
    {
        _lastShotVictim = -1;

        // SECURITY GATE: Prevent Buffer Overflow/Underflow
        if (buffer.Length != GameConstants.InputPacketSize)
        {
            Console.Error.WriteLine($"Security Alert: Invalid packet size from client {clientId}");
            DropClient(clientId);
            return;
        }

        PlayerInputPacket packet = PlayerInputPacket.Parse(buffer);

        // SECURITY GATE: Reject NaN/Inf and out-of-range vectors (speed hacks)
        if (!float.IsFinite(packet.MovementX) || !float.IsFinite(packet.MovementY) ||
            Math.Abs(packet.MovementX) > 1.0f || Math.Abs(packet.MovementY) > 1.0f)
        {
            Console.Error.WriteLine($"Security Alert: Out-of-bounds movement input from client {clientId}");
            FlagCheater(clientId);
            return;
        }

        ProcessPlayerLogic(clientId, packet);
    }

    private bool AllowPacket(uint senderIp, long nowSeconds)
    {
        if (!_rateWindows.TryGetValue(senderIp, out RateWindow? window))
        {
            window = new RateWindow();
            _rateWindows[senderIp] = window;
        }
        if (window.WindowStart != nowSeconds)
        {
            window.WindowStart = nowSeconds;
            window.Count = 0;
            window.Alerted = false;
        }
        if (++window.Count > GameConstants.MaxPacketsPerSecond)
        {
            if (!window.Alerted)
            {
                window.Alerted = true;
                Console.Error.WriteLine($"Security Alert: Rate limit exceeded by {new IPAddress(senderIp)}");
            }
            return false;
        }
        return true;
    }

    public int Run(ushort port)
    {
        using Socket sock = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        // Loopback only until DTLS lands (SR-2)
        sock.Bind(new IPEndPoint(IPAddress.Loopback, port));
        Console.WriteLine($"Listening on udp://127.0.0.1:{port}");

        byte[] buffer = new byte[512];
        byte[] snapshot = new byte[GameConstants.StatePacketSize];

        void BroadcastState(int playerId)
        {
            PlayerState? state = GetPlayerState(playerId);
            if (state is null)
            {
                return;
            }
            state.WriteSnapshot(snapshot, playerId);
            foreach (IPEndPoint addr in _clientAddrs.Values)
            {
                sock.SendTo(snapshot, addr);
            }
        }

        EndPoint sender = new IPEndPoint(IPAddress.Any, 0);
        while (true)
        {
            int received = sock.ReceiveFrom(buffer, ref sender);
            var senderEp = (IPEndPoint)sender;

#pragma warning disable CS0618 // Address: parity with app01's uint32 IP keying
            if (!AllowPacket((uint)senderEp.Address.Address, DateTimeOffset.UtcNow.ToUnixTimeSeconds()))
#pragma warning restore CS0618
            {
                continue; // SR-4: silently drop flood traffic after the first alert
            }

            // Client identity is IP:port until session tokens land (see STRIDE/Spoofing).
            int clientId = senderEp.Port;
            _clientAddrs[clientId] = new IPEndPoint(senderEp.Address, senderEp.Port);
            HandleIncomingPacket(buffer.AsSpan(0, received), clientId);

            // FR-3 + correction path: everyone gets the sender's authoritative state;
            // a rejected input still broadcasts the unchanged snapshot.
            BroadcastState(clientId);
            if (_lastShotVictim >= 0)
            {
                BroadcastState(_lastShotVictim);
            }
        }
    }

    private void ProcessPlayerLogic(int clientId, PlayerInputPacket packet)
    {
        if (!_players.TryGetValue(clientId, out PlayerState? state))
        {
            state = new PlayerState();
            _players[clientId] = state;
        }

        // Drop stale/replayed inputs; UDP reorders and attackers replay.
        if (state.LastProcessedSequence != 0 && packet.SequenceNumber <= state.LastProcessedSequence)
        {
            return;
        }
        state.LastProcessedSequence = packet.SequenceNumber;

        // Authoritative delta physics: server decides how far an input moves you.
        state.PositionX = Math.Clamp(state.PositionX + (packet.MovementX * GameConstants.PlayerSpeed * GameConstants.TickSeconds),
                                     -GameConstants.ArenaHalfExtent, GameConstants.ArenaHalfExtent);
        state.PositionY = Math.Clamp(state.PositionY + (packet.MovementY * GameConstants.PlayerSpeed * GameConstants.TickSeconds),
                                     -GameConstants.ArenaHalfExtent, GameConstants.ArenaHalfExtent);

        float magnitude = MathF.Sqrt((packet.MovementX * packet.MovementX) + (packet.MovementY * packet.MovementY));
        if (magnitude > 0.0f)
        {
            state.FacingX = packet.MovementX / magnitude;
            state.FacingY = packet.MovementY / magnitude;
        }

        if (packet.IsShooting)
        {
            _lastShotVictim = ResolveShot(clientId);
        }
    }

    // Authoritative hit-scan from the shooter's position along its facing.
    // Returns the victim's clientId, or -1 on miss.
    private int ResolveShot(int shooterId)
    {
        PlayerState shooter = _players[shooterId];

        int victimId = -1;
        float closest = GameConstants.ShotRange + 1.0f;
        foreach ((int id, PlayerState target) in _players)
        {
            if (id == shooterId)
            {
                continue;
            }

            float dx = target.PositionX - shooter.PositionX;
            float dy = target.PositionY - shooter.PositionY;
            float along = (dx * shooter.FacingX) + (dy * shooter.FacingY); // distance along the shot ray
            if (along <= 0.0f || along > GameConstants.ShotRange)
            {
                continue;
            }

            float perpendicular = Math.Abs((dx * shooter.FacingY) - (dy * shooter.FacingX));
            if (perpendicular > GameConstants.HitboxRadius)
            {
                continue;
            }

            if (along < closest)
            {
                closest = along;
                victimId = id;
            }
        }

        if (victimId >= 0)
        {
            PlayerState victim = _players[victimId];
            victim.Health -= GameConstants.ShotDamage;
            Console.WriteLine($"Hit: {shooterId} -> {victimId} (health {victim.Health})");
            if (victim.Health <= 0)
            {
                Console.WriteLine($"Kill: {shooterId} eliminated {victimId}");
                victim.PositionX = 0.0f;
                victim.PositionY = 0.0f;
                victim.Health = GameConstants.MaxHealth;
            }
        }
        return victimId;
    }

    private static void DropClient(int clientId) { _ = clientId; /* Disconnect logic */ }

    private static void FlagCheater(int clientId) { _ = clientId; /* Logging and banning logic */ }
}
