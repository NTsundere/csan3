using System;
using System.Text;

namespace Lab3
{
    public class TcpMessage
    {
        public const int CONNECT = 1;
        public const int MESSAGE = 2;
        public const int EXIT_USER = 3;
        public const int SEND_HISTORY = 4;
        public const int SHOW_HISTORY = 5;

        public int Code { get; }
        public string Ip { get; }
        public string Username { get; }
        public string MessageText { get; }

        public TcpMessage(int code, string ip, string content, bool isConnect)
        {
            Code = code;
            Ip = ip;

            if (isConnect)
                Username = content;
            else
                MessageText = content;
        }

        public TcpMessage(byte[] data)
        {
            int offset = 0;
            Code = BitConverter.ToInt32(data, offset);
            offset += sizeof(int);

            int ipLength = BitConverter.ToInt32(data, offset);
            offset += sizeof(int);
            Ip = Encoding.UTF8.GetString(data, offset, ipLength);
            offset += ipLength;

            if (Code == CONNECT)
            {
                int usernameLength = BitConverter.ToInt32(data, offset);
                offset += sizeof(int);
                Username = Encoding.UTF8.GetString(data, offset, usernameLength);
            }
            else
            {
                int messageLength = BitConverter.ToInt32(data, offset);
                offset += sizeof(int);
                MessageText = Encoding.UTF8.GetString(data, offset, messageLength);
            }
        }

        public byte[] ToBytes()
        {
            var ipBytes = Encoding.UTF8.GetBytes(Ip);
            var ipLength = BitConverter.GetBytes(ipBytes.Length);

            if (Code == CONNECT)
            {
                var usernameBytes = Encoding.UTF8.GetBytes(Username);
                var usernameLength = BitConverter.GetBytes(usernameBytes.Length);

                var result = new byte[sizeof(int) * 3 + ipBytes.Length + usernameBytes.Length];
                var offset = 0;

                BitConverter.GetBytes(Code).CopyTo(result, offset);
                offset += sizeof(int);
                ipLength.CopyTo(result, offset);
                offset += sizeof(int);
                ipBytes.CopyTo(result, offset);
                offset += ipBytes.Length;
                usernameLength.CopyTo(result, offset);
                offset += sizeof(int);
                usernameBytes.CopyTo(result, offset);

                return result;
            }
            else
            {
                var messageBytes = Encoding.UTF8.GetBytes(MessageText);
                var messageLength = BitConverter.GetBytes(messageBytes.Length);

                var result = new byte[sizeof(int) * 3 + ipBytes.Length + messageBytes.Length];
                var offset = 0;

                BitConverter.GetBytes(Code).CopyTo(result, offset);
                offset += sizeof(int);
                ipLength.CopyTo(result, offset);
                offset += sizeof(int);
                ipBytes.CopyTo(result, offset);
                offset += ipBytes.Length;
                messageLength.CopyTo(result, offset);
                offset += sizeof(int);
                messageBytes.CopyTo(result, offset);

                return result;
            }
        }
    }
}