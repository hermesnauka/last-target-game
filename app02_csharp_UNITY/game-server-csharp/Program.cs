using System.Buffers.Binary;

namespace GameServer;

public static class Program
{
    private static int _failures;

    private static void Expect(bool condition, string what)
    {
        Console.WriteLine((condition ? "PASS: " : "FAIL: ") + what);
        if (!condition)
        {
            _failures++;
        }
    }

    private static void SendInput(NetworkServer server, int clientId, uint seq, float x, float y, bool shoot = false)
    {
        Span<byte> buffer = stackalloc byte[GameConstants.InputPacketSize];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, seq);
        BinaryPrimitives.WriteSingleLittleEndian(buffer[4..], x);
        BinaryPrimitives.WriteSingleLittleEndian(buffer[8..], y);
        buffer[12] = shoot ? (byte)1 : (byte)0;
        server.HandleIncomingPacket(buffer, clientId);
    }

    private static void TestValidationAndMovement(NetworkServer server)
    {
        const float step = GameConstants.PlayerSpeed * GameConstants.TickSeconds;

        SendInput(server, 1, 1, 1.0f, 0.0f);
        PlayerState? p1 = server.GetPlayerState(1);
        Expect(p1 is not null && Math.Abs(p1.PositionX - step) < 1e-6f, "valid input moves player by one server step");

        SendInput(server, 2, 1, 5.0f, 0.0f);
        Expect(server.GetPlayerState(2) is null, "speed-hack input creates no state");

        byte[] malformed = [0, 1, 2, 3];
        server.HandleIncomingPacket(malformed, 3);
        Expect(server.GetPlayerState(3) is null, "malformed packet creates no state");

        SendInput(server, 1, 1, 1.0f, 0.0f); // replayed sequence number
        p1 = server.GetPlayerState(1);
        Expect(p1 is not null && Math.Abs(p1.PositionX - step) < 1e-6f, "replayed sequence is ignored");

        for (uint seq = 2; seq < 20000; seq++)
        {
            SendInput(server, 1, seq, 1.0f, 0.0f);
        }
        p1 = server.GetPlayerState(1);
        Expect(p1 is not null && p1.PositionX == GameConstants.ArenaHalfExtent, "position clamps at arena bound");
    }

    private static void TestCombat()
    {
        NetworkServer server = new();

        // Target moves 10 steps right; shooter takes one step right behind it.
        for (uint seq = 1; seq <= 10; seq++)
        {
            SendInput(server, 20, seq, 1.0f, 0.0f);
        }
        SendInput(server, 10, 1, 1.0f, 0.0f);

        SendInput(server, 10, 2, 1.0f, 0.0f, shoot: true);
        PlayerState? target = server.GetPlayerState(20);
        Expect(target is not null && target.Health == GameConstants.MaxHealth - GameConstants.ShotDamage,
               "shot along facing damages target");

        for (uint seq = 3; seq <= 5; seq++)
        {
            SendInput(server, 10, seq, 0.0f, 0.0f, shoot: true);
        }
        target = server.GetPlayerState(20);
        Expect(target is not null && target.Health == GameConstants.MaxHealth &&
                   target.PositionX == 0.0f && target.PositionY == 0.0f,
               "fourth hit kills and respawns target at origin");

        // Respawned target is behind the shooter now — the next shot must miss.
        SendInput(server, 10, 6, 0.0f, 0.0f, shoot: true);
        target = server.GetPlayerState(20);
        Expect(target is not null && target.Health == GameConstants.MaxHealth, "shot misses target behind the shooter");
    }

    // Offline self-test of the validation gates and authoritative state (no socket needed).
    private static int RunSelfTest(NetworkServer server)
    {
        TestValidationAndMovement(server);
        TestCombat();
        Console.WriteLine(_failures == 0 ? "self-test OK" : "self-test FAILED");
        return _failures == 0 ? 0 : 1;
    }

    public static int Main(string[] args)
    {
        NetworkServer server = new();

        if (args.Length == 2 && args[0] == "--listen")
        {
            if (!ushort.TryParse(args[1], out ushort port) || port == 0)
            {
                Console.Error.WriteLine($"Invalid port: {args[1]}");
                return 1;
            }
            return server.Run(port);
        }

        if (args.Length != 0)
        {
            Console.Error.WriteLine("Usage: GameServer [--listen <port>]");
            return 1;
        }

        return RunSelfTest(server);
    }
}
