using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Lab3
{
    public partial class MainWindow : Window
    {
        private readonly SynchronizationContext context;
        private volatile bool aliveUdpTask;
        private volatile bool aliveTcpTask;
        private const int CONNECT = 1;
        private const int MESSAGE = 2;
        private const int EXIT_USER = 3;
        private const int HISTORY_REQUEST = 4;
        private const int HISTORY_RESPONSE = 5;
        private const int UDP_LOCAL_PORT = 8501;
        private const int UDP_REMOTE_PORT = 8502;
        private const int TCP_PORT = 8503;
        private object synlock = new object();
        private static List<string> chatHistory = new List<string>();
        public string Username { get; set; }
        public string IpAddress { get; set; }
        public List<ChatUser> ChatUsers { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            context = SynchronizationContext.Current;
            ChatUsers = new List<ChatUser>();
            tbMessage.IsEnabled = false;
        }

        private void ButtonConnect_Click(object sender, RoutedEventArgs e)
        {
            Username = tbUserName.Text.Trim();
            IpAddress = tbUserIP.Text.Trim();

            if (string.IsNullOrEmpty(Username))
            {
                MessageBox.Show("Введите имя пользователя!");
                return;
            }

            if (!IPAddress.TryParse(IpAddress, out _))
            {
                MessageBox.Show("Некорректный IP-адрес!");
                return;
            }

            try
            {
                tbUserName.IsReadOnly = true;
                tbUserIP.IsReadOnly = true;
                btnConnect.IsEnabled = false;
                tbMessage.IsEnabled = true;

                // Запуск серверных компонентов
                Task.Run(() => ListenUdpMessages());
                Task.Run(() => ListenTcpMessages());

                // Уведомление о подключении
                SendFirstNotification();
                tbChat.AppendText("Вы успешно подключились!\r\n");
                chatHistory.Add($"{DateTime.Now} {Username} ({IpAddress}) присоединился\r\n");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка подключения: {ex.Message}");
            }
        }

        private void SendFirstNotification()
        {
            try
            {
                using (var udpSender = new UdpClient(new IPEndPoint(IPAddress.Parse(IpAddress), UDP_LOCAL_PORT)))
                {
                    udpSender.EnableBroadcast = true;
                    var message = new UdpMessage(IpAddress, Username).ToBytes();
                    udpSender.Send(message, message.Length, new IPEndPoint(IPAddress.Broadcast, UDP_REMOTE_PORT));
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка отправки уведомления: {ex.Message}");
            }
        }

        private void ListenUdpMessages()
        {
            aliveUdpTask = true;
            using (var udpReceiver = new UdpClient(new IPEndPoint(IPAddress.Parse(IpAddress), UDP_REMOTE_PORT)))
            {
                while (aliveUdpTask)
                {
                    try
                    {
                        IPEndPoint remoteEP = null;
                        byte[] data = udpReceiver.Receive(ref remoteEP);
                        var msg = new UdpMessage(data);

                        if (msg.Ip != IpAddress)
                        {
                            var newUser = new ChatUser(msg.Ip, TCP_PORT)
                            {
                                Username = msg.Username
                            };


                            // Добавляем пользователя в список
                            lock (synlock)
                            {
                                ChatUsers.Add(newUser);
                            }

                            // Отправляем информацию о себе
                            newUser.SendMessage(new TcpMessage(CONNECT, IpAddress, Username, true));

                            // Отображение подключения
                            context.Post(_ =>
                            {
                                string connectMsg = $"{DateTime.Now} {msg.Username} ({msg.Ip}) присоединился\r\n";
                                tbChat.AppendText(connectMsg);
                                chatHistory.Add(connectMsg);
                            }, null);
                            Task.Run(() => ListenUser(newUser));
                        }
                    }
                    catch { }
                }
            }
        }

        private void ListenTcpMessages()
        {
            aliveTcpTask = true;
            var listener = new TcpListener(IPAddress.Parse(IpAddress), TCP_PORT);
            listener.Start();

            while (aliveTcpTask)
            {
                if (listener.Pending())
                {
                    var client = listener.AcceptTcpClient();
                    var newUser = new ChatUser(client);

                    Task.Run(() =>
                    {
                        try
                        {
                            var msg = newUser.ReceiveMessage();
                            newUser.Username = msg.Username;
                            newUser.Ip = msg.Ip;

                            lock (synlock)
                            {
                                ChatUsers.Add(newUser);
                            }

                            context.Post(_ =>
                            {
                                string connectMsg = $"{DateTime.Now} {newUser.Username} ({newUser.Ip}) присоединился\r\n";
                                tbChat.AppendText(connectMsg);
                                chatHistory.Add(connectMsg);
                            }, null);

                            // Запрос на получение истории сразу после подключения
                            newUser.RequestHistory();  // Здесь добавляем вызов

                            ListenUser(newUser);
                        }
                        catch { /* Игнорируем ошибки */ }
                    });
                }
                Thread.Sleep(100);
            }
            listener.Stop();
        }

        private void ListenUser(ChatUser user)
        {
            try
            {
                while (user.IsOnline)
                {
                    if (user.Stream.DataAvailable)
                    {
                        var message = user.ReceiveMessage();
                        switch (message.Code)
                        {
                            case TcpMessage.HISTORY_REQUEST: // Добавляем обработку запроса истории
                                SendHistoryToNewUser(user);
                                break;

                            case TcpMessage.HISTORY_RESPONSE:
                                context.Post(_ =>
                                {
                                    tbChat.Clear();
                                    var lines = message.MessageText.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                                    foreach (var line in lines)
                                    {
                                        tbChat.AppendText(line + Environment.NewLine);
                                    }
                                }, null);
                                break;

                            case TcpMessage.MESSAGE:
                                context.Post(_ =>
                                {
                                    tbChat.AppendText($"{message.MessageText}\r\n");
                                    chatHistory.Add(message.MessageText);
                                }, null);
                                break;
                        }
                    }
                    Thread.Sleep(100);
                }
            }
            catch { }
            finally
            {
                user.Dispose();
                lock (synlock) ChatUsers.Remove(user);
            }
        }

        private void SendHistoryToNewUser(ChatUser user)
        {
            try
            {
                string history = string.Join(Environment.NewLine, chatHistory);
                user.SendMessage(new TcpMessage(TcpMessage.HISTORY_RESPONSE, IpAddress, history, false));
            }
            catch { }
        }

        private void SendTcpMessage()
        {
            if (string.IsNullOrWhiteSpace(tbMessage.Text)) return;

            string message = $"{DateTime.Now} {Username} ({IpAddress}): {tbMessage.Text}";
            chatHistory.Add(message.Replace("\r\n", "")); // Сохраняем в историю

            // Отправка сообщения
            var tcpMessage = new TcpMessage(MESSAGE, IpAddress, message, false);

            lock (synlock)
            {
                foreach (var user in ChatUsers.ToArray())
                {
                    try
                    {
                        user.SendMessage(tcpMessage);
                    }
                    catch
                    {
                        user.Dispose();
                        ChatUsers.Remove(user);
                    }
                }
            }

            // Отображение своего сообщения
            context.Post(_ =>
            {
                tbChat.AppendText($"{message}\r\n");
                tbMessage.Clear();
            }, null);
        }

        private void ExitChat()
        {
            aliveUdpTask = false;
            aliveTcpTask = false;

            var exitMessage = new TcpMessage(EXIT_USER, IpAddress, "покинул чат", false);

            lock (synlock)
            {
                foreach (var user in ChatUsers)
                {
                    try
                    {
                        user.SendMessage(exitMessage);
                        user.Dispose();
                    }
                    catch { }
                }
                ChatUsers.Clear();
            }

            context.Post(_ =>
            {
                tbChat.AppendText($"{DateTime.Now} Вы ({IpAddress}) покинули чат\r\n");
            }, null);
        }

        private void ButtonSend_Click(object sender, RoutedEventArgs e) => SendTcpMessage();
        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e) => ExitChat();
    }
}