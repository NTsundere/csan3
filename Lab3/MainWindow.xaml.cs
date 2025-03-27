using Lab3;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Remoting.Messaging;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Lab3
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly SynchronizationContext context;
        private volatile bool aliveUdpTask;
        private volatile bool aliveTcpTask;
        private const int CONNECT = TcpMessage.CONNECT;
        private const int MESSAGE = TcpMessage.MESSAGE;
        private const int EXIT_USER = TcpMessage.EXIT_USER;
        private const int SEND_HISTORY = TcpMessage.SEND_HISTORY;
        private const int SHOW_HISTORY = TcpMessage.SHOW_HISTORY;
        private const int UDP_LOCAL_PORT = 8501;
        private const int UDP_REMOTE_PORT = 8502;
        private const int TCP_PORT = 8503;
        private object synlock = new object();

        public string Username { get; set; }
        public string IpAddress { get; set; }
        public List<ChatUser> ChatUsers { get; set; }
        public string History { get; set; }

        /// <summary>
        /// Инициализация окна
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            context = SynchronizationContext.Current;
            ChatUsers = new List<ChatUser>();
            tbMessage.IsEnabled = false;
        }

        #region Получение IP-адреса пользователя и Broadcast адреса
        /// <summary>
        /// Получение локального IP-адреса пользователя
        /// </summary>
        private string FindAvailableLoopbackAddress()
        {
            const int MAX_ATTEMPTS = 10; 
            int currentIndex = 1;

            while (currentIndex <= MAX_ATTEMPTS)
            {
                string testAddress = $"127.0.0.{currentIndex}";

                try
                {
                    // Пытаемся создать временный сокет для проверки
                    using (var testSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                    {
                        testSocket.Bind(new IPEndPoint(IPAddress.Parse(testAddress), 8503)); // Проверяем порт TCP
                        testSocket.Close();
                        return testAddress; // Если удалось привязаться - адрес свободен
                    }
                }
                catch (SocketException)
                {
                    // Адрес занят, пробуем следующий
                    currentIndex++;
                }
            }

            throw new Exception("Не удалось найти свободный loopback-адрес");
        }
        private void GetIpAddress()
        {
            try
            {
                // Пробуем найти свободный loopback-адрес
                IpAddress = FindAvailableLoopbackAddress();
            }
            catch
            {
                // Если не получилось, используем стандартный способ
                Socket tempSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.IP);
                try
                {
                    tempSocket.Connect("8.8.8.8", 8100);
                    IpAddress = ((IPEndPoint)tempSocket.LocalEndPoint).Address.ToString();
                }
                catch
                {
                    IpAddress = ((IPEndPoint)tempSocket.LocalEndPoint).Address.ToString();
                }
                tempSocket.Shutdown(SocketShutdown.Both);
                tempSocket.Close();
            }
        }

        private string GetBroadcastAddress(string localIP)
        {
            string temp = localIP.Substring(0, localIP.LastIndexOf(".") + 1);
            return temp + "255";
        }
        #endregion

        #region Отправка и получение UDP-пакетов
        /// <summary>
        /// Отправление UDP-пакета всем пользователям
        /// </summary>
        private void SendFirstNotification()
        {
            const int LOCAL_PORT = 8501;
            const int REMOTE_PORT = 8502;

            try
            {
                IPEndPoint sourceEndPoint = new IPEndPoint(IPAddress.Parse(IpAddress), LOCAL_PORT);
                UdpClient udpSender = new UdpClient(sourceEndPoint);
                IPEndPoint destEndPoint = new IPEndPoint(IPAddress.Parse(GetBroadcastAddress(IpAddress)), REMOTE_PORT);
                udpSender.EnableBroadcast = true;

                UdpMessage udpMessage = new UdpMessage(IpAddress, Username);
                byte[] messageBytes = udpMessage.ToBytes();
                udpSender.Send(messageBytes, messageBytes.Length, destEndPoint);
                udpSender.Dispose();

                string messageChat = "Вы успешно подключились!\r\n";
                tbChat.AppendText(messageChat);
                string datetime = DateTime.Now.ToString();
                History += string.Format("{0} {1} присоединился к чату\r\n", datetime, Username);
            }
            catch
            {
                throw new Exception("Не удалось отправить уведомление о новом пользователе.");
            }
        }

        /// <summary>
        /// Приём UDP-пакетов от новых пользователей
        /// </summary>
        private void ListenUdpMessages()
        {
            aliveUdpTask = true;
            UdpClient udpReceiver = new UdpClient(UDP_REMOTE_PORT);

            while (aliveUdpTask)
            {
                try
                {
                    IPEndPoint remoteEndPoint = null;
                    byte[] data = udpReceiver.Receive(ref remoteEndPoint);
                    UdpMessage udpMessage = new UdpMessage(data);

                    if (udpMessage.Ip != IpAddress)
                    {
                        var newUser = new ChatUser(udpMessage.Ip, TCP_PORT);
                        newUser.Username = udpMessage.Username;
                        newUser.Ip = udpMessage.Ip;

                        var connectMsg = new TcpMessage(CONNECT, IpAddress, Username, true);
                        newUser.SendMessage(connectMsg);

                        lock (synlock)
                        {
                            ChatUsers.Add(newUser);
                        }

                        string connectText = $"{DateTime.Now} {newUser.Username} ({newUser.Ip}) присоединился\r\n";
                        context.Post(_ => {
                            tbChat.AppendText(connectText);
                            History += connectText;
                        }, null);

                        Task.Run(() => ListenUser(newUser));
                    }
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
            udpReceiver.Dispose();
        }
        #endregion

        #region Отправка и получение TCP-пакетов
        /// <summary>
        /// Прослушивание TCP-пакетов
        /// </summary>
        private void ListenTcpMessages()
        {
            aliveTcpTask = true;
            TcpListener listener = new TcpListener(IPAddress.Parse(IpAddress), TCP_PORT);
            listener.Start();

            while (aliveTcpTask)
            {
                if (listener.Pending())
                {
                    var client = listener.AcceptTcpClient();
                    var newUser = new ChatUser(client);

                    Task.Run(() => {
                        try
                        {
                            var msg = newUser.ReceiveMessage();
                            newUser.Ip = msg.Ip;
                            newUser.Username = msg.Username;

                            lock (synlock)
                            {
                                ChatUsers.Add(newUser);
                            }

                            string connectText = $"{DateTime.Now} {newUser.Username} ({newUser.Ip}) присоединился\r\n";
                            context.Post(_ => {
                                tbChat.AppendText(connectText);
                                History += connectText;
                            }, null);

                            ListenUser(newUser);
                        }
                        catch { }
                    });
                }
                Thread.Sleep(100);
            }
            listener.Stop();
        }

        /// <summary>
        /// Прослушивание пользователя чата
        /// </summary>
        /// <param name="user">Пользователь чата</param>
        private void ListenUser(ChatUser user)
        {
            try
            {
                while (user.IsOnline && user.Stream != null)
                {
                    if (user.Stream.DataAvailable)
                    {
                        var message = user.ReceiveMessage();

                        switch (message.Code)
                        {
                            case MESSAGE:
                                string msgText = $"{DateTime.Now} {user.Username} ({user.Ip}): {message.MessageText}\r\n";
                                context.Post(_ => {
                                    tbChat.AppendText(msgText);
                                    History += msgText;
                                }, null);
                                break;

                            case EXIT_USER:
                                string exitText = $"{DateTime.Now} {user.Username} ({user.Ip}) покинул чат\r\n";
                                context.Post(_ => {
                                    tbChat.AppendText(exitText);
                                    History += exitText;
                                }, null);
                                user.Dispose();
                                lock (synlock) ChatUsers.Remove(user);
                                return;
                        }
                    }
                    Thread.Sleep(100);
                }
            }
            catch
            {
                string errorText = $"{DateTime.Now} {user.Username} ({user.Ip}) отключился (ошибка)\r\n";
                context.Post(_ => {
                    tbChat.AppendText(errorText);
                    History += errorText;
                }, null);
                user.Dispose();
                lock (synlock) ChatUsers.Remove(user);
            }
        }

        /// <summary>
        /// Отправка TCP-пакетов
        /// </summary>
        private void SendTcpMessage()
        {
            if (string.IsNullOrWhiteSpace(tbMessage.Text)) return;

            var message = new TcpMessage(MESSAGE, IpAddress, tbMessage.Text, false);
            string displayText = $"{DateTime.Now} Вы ({IpAddress}): {tbMessage.Text}\r\n";

            context.Post(_ => {
                tbChat.AppendText(displayText);
                History += displayText;
                tbMessage.Clear();
            }, null);

            lock (synlock)
            {
                foreach (var user in ChatUsers.ToArray())
                {
                    try
                    {
                        user.SendMessage(message);
                    }
                    catch
                    {
                        user.Dispose();
                        ChatUsers.Remove(user);
                        string disconnectMsg = $"{DateTime.Now} {user.Username} ({user.Ip}) отключился\r\n";
                        context.Post(_ => {
                            tbChat.AppendText(disconnectMsg);
                            History += disconnectMsg;
                        }, null);
                    }
                }
            }
        }

        #endregion

        #region Вывод принятой информации в чат пользователя
        /// <summary>
        /// Вывод информации в чат (сообщения пользователей, сообщения о выходе из чата,
        /// история пользователя из чата)
        /// </summary>
        /// <param name="code">Код сообщения</param>
        /// <param name="username">Имя пользователя, от кого пришло сообщение</param>
        /// <param name="message">Текст принятого сообщения</param>
        private void ShowInChat(int code, string username, string ip, string message)
        {
            string timeStamp = DateTime.Now.ToString();
            string chatMessage;

            switch (code)
            {
                case MESSAGE:
                    context.Post(_ => {
                        chatMessage = $"{timeStamp} {username} ({ip}): {message}\r\n";
                        History += chatMessage;
                        tbChat.AppendText(chatMessage);
                    }, null);
                    break;
                case EXIT_USER:
                    context.Post(_ => {
                        chatMessage = $"{timeStamp} {username} ({ip}) покинул чат\r\n";
                        History += chatMessage;
                        tbChat.AppendText(chatMessage);
                    }, null);
                    break;
                case CONNECT:
                    context.Post(_ => {
                        chatMessage = $"{timeStamp} {username} ({ip}) присоединился к чату\r\n";
                        History += chatMessage;
                        tbChat.AppendText(chatMessage);
                    }, null);
                    break;
            }
        }


        #endregion

        #region Обработка событий формы
        /// <summary>
        /// Нажтие на кнопку "Подключиться"
        /// </summary>
        private void ButtonConnect_Click(object sender, RoutedEventArgs e)
        {
            tbUserName.Text = tbUserName.Text.Trim();
            Username = tbUserName.Text;
            if (Username.Length > 0)
            {
                GetIpAddress();
                tbUserName.IsReadOnly = true;
                try
                {
                    SendFirstNotification();
                    Task listenUdpTask = new Task(ListenUdpMessages);
                    listenUdpTask.Start();
                    Task listenTcpTask = new Task(ListenTcpMessages);
                    listenTcpTask.Start();

                    btnConnect.IsEnabled = false;
                    tbMessage.IsEnabled = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                    ExitChat();
                }
            }
        }

        /// <summary>
        /// Нажтие на кнопку "Отправить"
        /// </summary>
        private void ButtonSend_Click(object sender, RoutedEventArgs e)
        {
            SendTcpMessage();
        }

        /// <summary>
        /// Закрытие окна
        /// </summary>
        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (aliveUdpTask && aliveTcpTask)
                ExitChat();
        }
        #endregion

        #region Дополнительные методы (выход из чата, запрос истории)
        /// <summary>
        /// Запрос истории
        /// </summary>
        /// <param name="code">Код, отвечающий за запрос истории или её отправку</param>
        /// <param name="user">Пользователь в чате, который отправляет или получает историю</param>
        private void GetHistory(int code, ChatUser user)
        {
            try
            {
                TcpMessage tcpHistoryMessage;
                if (code == SEND_HISTORY)
                {
                    tcpHistoryMessage = new TcpMessage(code, IpAddress, "History", false);
                }
                else // SHOW_HISTORY
                {
                    tcpHistoryMessage = new TcpMessage(code, IpAddress, History, false);
                }
                user.SendMessage(tcpHistoryMessage);
            }
            catch { }
        }

        private void ExitChat()
        {
            aliveUdpTask = false;
            aliveTcpTask = false;

            string exitText = $"{DateTime.Now} Вы ({IpAddress}) покинули чат\r\n";
            tbChat.AppendText(exitText);
            History += exitText;

            lock (synlock)
            {
                var exitMessage = new TcpMessage(EXIT_USER, IpAddress, "покинул чат", false);
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
        }

        #endregion
    }
}