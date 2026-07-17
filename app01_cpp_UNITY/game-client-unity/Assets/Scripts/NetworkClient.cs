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
        System.IntPtr ptr = Marshal.AllocHGlobal(len);
        Marshal.StructureToPtr(obj, ptr, true);
        Marshal.Copy(ptr, arr, 0, len);
        Marshal.FreeHGlobal(ptr);
        return arr;
    }
}
