using Lab3;
using System.Net.Sockets;

public class ChatUser
{
    public TcpClient Client { get; }
    public NetworkStream Stream { get; }
    public string Ip { get; set; }
    public string Username { get; set; }
    public bool IsOnline { get; set; } = true;

    public ChatUser(TcpClient client)
    {
        Client = client;
        Stream = client.GetStream();
    }

    public ChatUser(string ip, int port)
    {
        Client = new TcpClient(ip, port);
        Stream = Client.GetStream();
    }

    public void SendMessage(TcpMessage message)
    {
        byte[] data = message.ToBytes();
        Stream.Write(data, 0, data.Length);
    }

    public TcpMessage ReceiveMessage()
    {
        byte[] buffer = new byte[1024];
        int bytesRead = Stream.Read(buffer, 0, buffer.Length);
        return new TcpMessage(buffer);
    }

    public void Dispose()
    {
        IsOnline = false;
        Stream?.Close();
        Client?.Close();
    }
}