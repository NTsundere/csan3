using System;
using System.Text;

namespace Lab3
{
    public class UdpMessage
    {
        public string Ip { get; }
        public string Username { get; }

        public UdpMessage(string ip, string username)
        {
            Ip = ip;
            Username = username;
        }

        public UdpMessage(byte[] data)
        {
            string[] parts = Encoding.UTF8.GetString(data).Split('|');
            Ip = parts[0];
            Username = parts[1];
        }

        public byte[] ToBytes()
        {
            return Encoding.UTF8.GetBytes($"{Ip}|{Username}");
        }
    }
}