using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Concurrent;

public class CameraStreamer : MonoBehaviour
{
    public Camera cam;

    private RenderTexture rt;
    private Texture2D tex;
    private UdpClient client;
    private IPEndPoint endPoint;

    int width = 320;
    int height = 240;

    private ConcurrentQueue<byte[]> frameQueue = new ConcurrentQueue<byte[]>();
    private Thread senderThread;
    private bool running = false;

    int frameId = 0;

    void Start()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) { Debug.LogError("No camera!"); return; }

        rt  = new RenderTexture(width, height, 24);
        tex = new Texture2D(width, height, TextureFormat.RGB24, false);

        client   = new UdpClient();
        endPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5005);

        StartSenderThread();
        Debug.Log("CameraStreamer started");
    }

    void StartSenderThread()
    {
        running = true;

        senderThread = new Thread(() =>
        {
            int chunkSize = 1400; // 🔥 SAFE UDP SIZE (FIXED)

            while (running)
            {
                if (frameQueue.TryDequeue(out byte[] raw))
                {
                    int totalChunks = Mathf.CeilToInt((float)raw.Length / chunkSize);

                    for (int i = 0; i < totalChunks; i++)
                    {
                        int size = Mathf.Min(chunkSize, raw.Length - i * chunkSize);

                        byte[] packet = new byte[size + 8];

                        // HEADER
                        System.Buffer.BlockCopy(System.BitConverter.GetBytes(frameId), 0, packet, 0, 4);
                        System.Buffer.BlockCopy(System.BitConverter.GetBytes((ushort)totalChunks), 0, packet, 4, 2);
                        System.Buffer.BlockCopy(System.BitConverter.GetBytes((ushort)i), 0, packet, 6, 2);

                        // DATA
                        System.Buffer.BlockCopy(raw, i * chunkSize, packet, 8, size);

                        client.Send(packet, packet.Length, endPoint);
                    }

                    frameId++;
                }
                else
                {
                    Thread.Sleep(1);
                }
            }
        });

        senderThread.IsBackground = true;
        senderThread.Start();
    }

    void Update()
    {
        if (!running) return;

        cam.targetTexture = rt;
        cam.Render();

        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();

        cam.targetTexture = null;
        RenderTexture.active = null;

        byte[] raw = tex.GetRawTextureData();

        while (frameQueue.Count > 2)
            frameQueue.TryDequeue(out _);

        frameQueue.Enqueue(raw);
    }

    void OnApplicationQuit()
    {
        running = false;
        senderThread?.Join(500);
        client?.Close();
    }
}