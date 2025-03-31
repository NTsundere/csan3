using System.Net.Sockets;

namespace Lab3
{
    public class ChatUser
    {
        public TcpClient Client { get; }
        public NetworkStream Stream { get; }
        public string Ip { get; set; }
        public string Username { get; set; }
        public bool IsOnline { get; set; } = true;

        public ChatUser(string ip, int port)
        {
            Client = new TcpClient(ip, port);
            Stream = Client.GetStream();
            Ip = ip;
        }

        public ChatUser(TcpClient client)
        {
            Client = client;
            Stream = client.GetStream();
            Ip = ((System.Net.IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
        }

        public void SendMessage(TcpMessage message)
        {
            byte[] data = message.ToBytes();
            Stream.Write(data, 0, data.Length);
        }

        public TcpMessage ReceiveMessage()
        {
            byte[] buffer = new byte[4096];
            int bytesRead = Stream.Read(buffer, 0, buffer.Length);
            return new TcpMessage(buffer);
        }
        public void RequestHistory()
        {
            var historyRequestMessage = new TcpMessage(TcpMessage.HISTORY_REQUEST, Ip, "", false);
            SendMessage(historyRequestMessage);
        }
        public void Dispose()
        {
            IsOnline = false;
            Stream?.Close();
            Client?.Close();
        }
    }
}