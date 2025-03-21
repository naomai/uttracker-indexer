
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Naomai.UTT.Indexer;

public class SocketManager <T> where T : ISocket, new()
{
    public readonly T socket;
    protected List<string> ignoreIps = new List<string>();

    protected Dictionary<IPEndPoint, EndpointPacketBuffer> ipPacketQueue = new Dictionary<IPEndPoint, EndpointPacketBuffer>();
    protected DateTime lastProcessed;
    readonly object _queueLock = new object();

    public int UpdateIntervalMs = 100;
    public delegate void NewDataReceivedHandler( EndpointPacketBuffer packetBuffer, IPEndPoint source);
    public event NewDataReceivedHandler? NewDataReceived;

    public SocketManager()
    {
        IPEndPoint bindEndpoint = new IPEndPoint(IPAddress.Any, 0);
        socket = new T();
        
        socket.Bind(bindEndpoint);
        socket.ReceiveTimeout = 5000;
        socket.ReceiveBufferSize = 1024 * 1024;

        lastProcessed = DateTime.UtcNow;

        ReceiveLoop();
        DispatchLoop();
    }

    protected async void ReceiveLoop()
    {
        byte[] buffer = new byte[2000];
        EndPoint bindEndpoint = new IPEndPoint(IPAddress.Any, 0);
        IPEndPoint sourceEndpoint;
        string sourceIp;
        SocketReceiveFromResult receiveResult;

        while(true)
        {
            receiveResult = await socket.ReceiveFromAsync(buffer, bindEndpoint);

            sourceEndpoint = (IPEndPoint)receiveResult.RemoteEndPoint;
            sourceIp = sourceEndpoint.ToString();

            if (ignoreIps.Contains(sourceIp))
            {
                continue;
            }
            EnqueuePacket(sourceEndpoint, 
                    buffer.Take(receiveResult.ReceivedBytes).ToArray()
                );
        }
    }

    public void SendTo(string destination, string packet)
    {
        byte[] buffer = Encoding.ASCII.GetBytes(packet);
        string host = GetHost(destination);
        UInt16 port = GetPort(destination);

        IPAddress addr;
        IPEndPoint endpoint;

        if(!IPAddress.TryParse(host, out addr))
        {
            addr = Dns.GetHostEntry(host).AddressList[0];
        }
        endpoint = new IPEndPoint(addr, port);

        try
        {
            socket.SendTo(buffer, endpoint);
        }
        catch(Exception e)
        {
            FlushSocket();
        }
    }

    protected async void DispatchLoop()
    {
        while (true)
        {
            Tick();
            await Task.Delay(25);
        }
    }

    protected void Tick()
    {
        if ((DateTime.UtcNow - lastProcessed).TotalMilliseconds > UpdateIntervalMs)
        {
            DequeueAll();
            lastProcessed = DateTime.UtcNow;
        }
    }

    private void EnqueuePacket(IPEndPoint sourceEndpoint, byte[] packet)
    {
        lock(_queueLock)
        {
            if(!ipPacketQueue.ContainsKey(sourceEndpoint))
            {
                ipPacketQueue[sourceEndpoint] = new EndpointPacketBuffer();
            }
        }
        ipPacketQueue[sourceEndpoint].Enqueue(packet);
    }

    private void DequeueAll()
    {
        List<IPEndPoint> hosts;

        lock (_queueLock)
        {
            if(ipPacketQueue.Count == 0)
            {
                return;
            }
            hosts = ipPacketQueue.Keys.ToList();
        }
        foreach(IPEndPoint host in hosts)
        {
            DequeueForHost(host);
        }
    }

    private void DequeueForHost(IPEndPoint host)
    {
        EndpointPacketBuffer packetQueue = ipPacketQueue[host];
        if (packetQueue.NewPackets)
        {
            NewDataReceived(packetQueue, host);
        }
    }

    public void AddIgnoredIp(EndPoint ip)
    {
        AddIgnoredIp(ip.ToString());
    }

    public void AddIgnoredIp(string ip)
    {
        ignoreIps.Add(ip);
    }

    public void ClearIgnoredIps()
    {
        ignoreIps.Clear();
    }

    private static string GetHost(string addr)
    {
        string[] slices = addr.Split(":", 2);
        return slices[0];
    }

    private static UInt16 GetPort(string addr)
    {
        string[] slices = addr.Split(":", 2);
        return UInt16.Parse(slices[1]);
    }

    private void FlushSocket()
    {
        try
        {
            socket.Receive(null, socket.Available, SocketFlags.None);
        }
        catch (ArgumentNullException nullEx)
        {

        }
    }
}

public class SocketManager: SocketManager<UdpSocketAdapter>
{

}

public class UdpSocketAdapter :  ISocket
{
    Socket _socket;
    public UdpSocketAdapter()
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    }


    public int ReceiveBufferSize
    {
        get => _socket.ReceiveBufferSize;
        set => _socket.ReceiveBufferSize = value;
    }

    public int ReceiveTimeout
    {
        get => _socket.ReceiveTimeout;
        set => _socket.ReceiveTimeout = value;
    }

    public int Available => _socket.Available;

    public virtual void Bind(EndPoint localEP) => _socket.Bind(localEP);

    public virtual int Receive(byte[] buffer, int size, SocketFlags socketFlags)
        => _socket.Receive(buffer, size, socketFlags);

    public virtual async Task<SocketReceiveFromResult> ReceiveFromAsync(ArraySegment<byte> buffer, EndPoint remoteEP)
        => await _socket.ReceiveFromAsync(buffer,  remoteEP);
    

    public virtual int SendTo(byte[] buffer, EndPoint remoteEP) => _socket.SendTo(buffer, remoteEP);
}

public interface ISocket
{
    int ReceiveTimeout { get; set; }
    int ReceiveBufferSize { get; set; }
    int Available { get; }

    void Bind(EndPoint localEP);
    int Receive(byte[] buffer, int size, SocketFlags socketFlags);
    Task<SocketReceiveFromResult> ReceiveFromAsync(ArraySegment<byte> buffer, EndPoint remoteEP);
    int SendTo(byte[] buffer, EndPoint remoteEP);
}


public class EndpointPacketBuffer
{
    Queue<byte[]> _packetQueue = new Queue<byte[]>();
    readonly object _queueLock = new object();
    public bool NewPackets = false;

    public void Enqueue(byte[] packet)
    {
        lock (_queueLock){
            _packetQueue.Enqueue(packet);
        }
        NewPackets = true;
    }

    public byte[] Dequeue()
    {
        NewPackets = false;
        lock (_queueLock)
        {
            return _packetQueue.Dequeue();
        }
    }

    public byte[] PeekLast()
    {
        NewPackets = false;
        return _packetQueue.Last();
    }

    public byte[] PeekAll()
    {
        // establish working length of packet stream
        byte[] result = new byte[this.Length];
        int offset = 0;
        NewPackets = false;

        lock (_queueLock)
        {
            foreach (byte[] packet in _packetQueue)
            {
                // if packet is zero-terminated, grab position of null byte and trim packet
                byte[] packetTrimmed = packet;
                int zeroByteOffset = Array.IndexOf(packet, 0);
                if(zeroByteOffset != -1)
                {
                    Array.Resize(ref packetTrimmed, zeroByteOffset);
                }
                packetTrimmed.CopyTo(result, offset);
                offset += packetTrimmed.Length;
                
            }
        }
        // adjust length to discard trimmed bytes in zero-terminated packets 
        Array.Resize(ref result, offset);
        return result;
    }

    public void Clear()
    {
        NewPackets = false;
        lock (_queueLock)
        {
            _packetQueue.Clear();
        }
    }

    public int Length
    {
        get
        {
            int bytesTotal = 0;
            lock (_queueLock)
            {
                foreach (byte[] packet in _packetQueue)
                {
                    bytesTotal += packet.Length;
                }
            }
            return bytesTotal;
        }
    }

    public override string ToString()
    {
        return $"PB:CT={_packetQueue.Count}";
    }
}
