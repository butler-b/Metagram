﻿using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace MultiChatServer
{
    internal class Client : SocketAsyncEventArgs
    {
        static string aes_key = "AXe8YwuIn1zxt3FPWTZFlAa14EHdPAdN9FaZ9RQWihc="; //44자
        static string aes_iv = "bsxnWolsAyO7kCfWuyrnqg=="; //24자

        // 메시지는 개행으로 구분한다.
        private static char CR = (char)0x0D;
        private static char LF = (char)0x0A;
        private Socket socket;
        // 메시지를 모으기 위한 버퍼
        private StringBuilder sb = new StringBuilder();
        private IPEndPoint remoteAddr;

        public delegate void ClientReceiveHandler(Socket sock, String msg); //수신 메시지 이벤트를 위한 델리게이트
        public event ClientReceiveHandler OnReceive;
        public delegate void ClientDisconnectHandler(Socket sock);
        public event ClientDisconnectHandler OnDisconnect;

        public Client(Socket socket)
        {
            try
            {
                this.socket = socket;
                // 메모리 버퍼를 초기화 한다. 크기는 1024이다
                base.SetBuffer(new byte[1024], 0, 1024);
                base.UserToken = socket;
                // 메시지가 오면 이벤트를 발생시킨다. (IOCP로 꺼내는 것)
                base.Completed += Client_Completed;
                // 메시지가 오면 이벤트를 발생시킨다. (IOCP로 넣는 것)
                this.socket.ReceiveAsync(this);
                // 접속 환영 메시지
                remoteAddr = (IPEndPoint)socket.RemoteEndPoint;
                if (remoteAddr != null)
                {
                    //Console.WriteLine($"Client : (From: {remoteAddr.Address.ToString()}:{remoteAddr.Port}, Connection time: {DateTime.Now})");
                    //this.Send("Welcome server!\r\n>");
                }

            }
            catch (Exception e)
            {
                return;
            }

        }
        ~Client()
        {
            socket = null;
        }

        public void Send(String msg)
        {
            string original = msg;
            string str_encrypted = EncryptAES(original);
            byte[] sendData = Encoding.Unicode.GetBytes(str_encrypted);
            //sendArgs.SetBuffer(sendData, 0, sendData.Length);
            //socket.SendAsync(sendArgs);
            // Client로 메시지 전송
            try
            {
                if (socket != null)
                    socket.Send(sendData, sendData.Length, SocketFlags.None);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        private void Client_Completed(object sender, SocketAsyncEventArgs e)
        {
            // 접속이 연결되어 있으면...
            if (socket.Connected && base.BytesTransferred > 0)
            {
                // 수신 데이터는 e.Buffer에 있다.
                byte[] data = e.Buffer;
                // 데이터를 string으로 변환한다.
                string msg = Encoding.Unicode.GetString(data).Trim('\0');
                msg = DecryptAES(msg);
                // 메모리 버퍼를 초기화 한다. 크기는 1024였다.
                base.SetBuffer(new byte[8192], 0, 8192);
                // 버퍼의 공백은 없앤다.
                sb.Append(msg);
                // 메시지의 끝이 이스케이프 \r\n의 형태이면 서버에 표시한다.
                if (sb.Length >= 2 && sb[sb.Length - 2] == CR && sb[sb.Length - 1] == LF)
                {
                    // 개행은 없애고..
                    sb.Length = sb.Length - 2;
                    // string으로 변환한다.
                    msg = sb.ToString();
                    if (OnReceive != null)
                        OnReceive(this.socket, msg); // 수신 이벤트 발생

                    // 콘솔에 출력한다.
                    Console.WriteLine(DateTime.Now + " " + msg);
                    // Client로 Echo를 보낸다.
                    //Send($"Echo - {msg}\r\n>");
                    // 만약 메시지가 exit이면 접속을 끊는다.
                    if ("exit".Equals(msg, StringComparison.OrdinalIgnoreCase))
                    {
                        OnDisconnect(socket);
                        // 접속 종료 메시지
                        Console.WriteLine($"Disconnected : (From: {remoteAddr.Address.ToString()}:{remoteAddr.Port}, Connection time: {DateTime.Now})");
                        // 접속을 중단한다.
                        socket.DisconnectAsync(this);
                        return;
                    }
                    // 버퍼를 비운다.
                    sb.Clear();
                }
                // 메시지가 오면 이벤트를 발생시킨다. (IOCP로 넣는 것)
                this.socket.ReceiveAsync(this);
            }
            else
            {
                OnDisconnect(socket);
                // 접속이 끊겼다..
                Console.WriteLine($"Disconnected : (From: {remoteAddr.Address.ToString()}:{remoteAddr.Port}, Connection time: {DateTime.Now})");
            }
        }
        public static string EncryptAES(string plainText)
        {
            byte[] encrypted;

            using (AesCryptoServiceProvider aes = new AesCryptoServiceProvider())
            {
                aes.KeySize = 256;  //암호화 키 크기 설정(bit)
                aes.BlockSize = 128;    // 블록 사이즈 설정(bit)
                aes.Key = Convert.FromBase64String(aes_key);    //입력된 암호화 키 설정
                aes.IV = Convert.FromBase64String(aes_iv);      //입력된 iv 값 설정
                aes.Mode = CipherMode.CBC;  //CBC 모드 사용
                aes.Padding = PaddingMode.PKCS7; // 패딩 방법 설정, 블록 크기인 128비트에 데이터 크기를 맞추려고 할 때 필요

                ICryptoTransform enc = aes.CreateEncryptor(aes.Key, aes.IV); 

                using (MemoryStream ms = new MemoryStream())
                {
                    using (CryptoStream cs = new CryptoStream(ms, enc, CryptoStreamMode.Write))
                    {
                        using (StreamWriter sw = new StreamWriter(cs))
                        {
                            sw.Write(plainText);
                        }

                        encrypted = ms.ToArray();
                    }
                }
            }

            return Convert.ToBase64String(encrypted); //base64로 인코딩
        }

        public static string DecryptAES(string encryptedText)
        {
            string decrypted = null;
            byte[] cipher = Convert.FromBase64String(encryptedText); //base64로 디코딩

            using (AesCryptoServiceProvider aes = new AesCryptoServiceProvider())
            {
                aes.KeySize = 256;
                aes.BlockSize = 128;
                aes.Key = Convert.FromBase64String(aes_key);
                aes.IV = Convert.FromBase64String(aes_iv);
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                ICryptoTransform dec = aes.CreateDecryptor(aes.Key, aes.IV);

                using (MemoryStream ms = new MemoryStream(cipher))
                {
                    using (CryptoStream cs = new CryptoStream(ms, dec, CryptoStreamMode.Read))
                    {
                        using (StreamReader sr = new StreamReader(cs))
                        {
                            decrypted = sr.ReadToEnd();
                        }
                    }
                }
            }

            return decrypted;
        }
    }

    // 서버 Event로 SocketAsyncEventArgs를 상속받았다.
    class Server : SocketAsyncEventArgs
    {
        public delegate void ClientReceiveHandler(Socket sock, String msg); //수신 메시지 이벤트를 위한 델리게이트
        public event ClientReceiveHandler OnReceive;
        public delegate void ClientDisconnectHandler(Socket sock);
        public event ClientDisconnectHandler OnDisconnect;
        public delegate void ClientConnectHandler(Socket sock);
        public event ClientConnectHandler OnConnect;

        private Socket socket;
        public List<Socket> clientSocketList = new List<Socket>();//클라이언트 소켓을 관리하는 리스트, 소켓과 접속 아이디를 관리하자.
        public Dictionary<Socket, Client> clientList = new Dictionary<Socket, Client>();//클라이언트 소켓을 관리하는 리스트, 소켓과 접속 아이디를 관리하자.

        public Server(Socket socket)
        {
            this.socket = socket;
            base.UserToken = socket;
            // Client로부터 Accept이 되면 이벤트를 발생시킨다. (IOCP로 꺼내는 것)
            base.Completed += Server_Completed;
        }

        public void SocketClose()
        {
            foreach (var client in clientSocketList)
            {
                if (client.Connected) client.Disconnect(false);
                client.Dispose();
            }
            foreach (var client in clientList)
            {
                Client c = client.Value;
                c.Dispose();
            }
            clientSocketList.Clear();
            clientList.Clear();

        }

        // Client가 접속하면 이벤트를 발생한다.
        private void Server_Completed(object sender, SocketAsyncEventArgs e)
        {
            try
            {
                // 접속이 완료되면, Client Event를 생성하여 Receive이벤트를 생성한다.
                var client = new Client(e.AcceptSocket);
                client.OnReceive += ClientReceive;
                client.OnDisconnect += ClientDisconnect;

                clientSocketList.Add(e.AcceptSocket);
                clientList.Add(e.AcceptSocket, client);
                // 서버 Event에 cilent를 제거한다.
                e.AcceptSocket = null;
                // Client로부터 Accept이 되면 이벤트를 발생시킨다. (IOCP로 넣는 것)
                this.socket.AcceptAsync(e);
                if (OnConnect != null)
                {
                    OnConnect(this.socket);
                }
            }
            catch (Exception ex)
            {
                return;
            }

        }

        private void ClientDisconnect(Socket sock)
        {

            if (OnDisconnect != null)
                OnDisconnect(sock);

            clientList.Remove(sock);
            clientSocketList.Remove(sock);
        }

        private void ClientReceive(Socket sock, string msg)
        {
            if (OnReceive != null)
                OnReceive(sock, msg);
        }
        public void SendAllMessage(String msg)
        {
            try
            {
                foreach (var client in clientList)
                {
                    Client c = client.Value;
                    c.Send(msg);
                }
            }
            catch (Exception e2)
            {
                Console.WriteLine(e2);
                return;
            }
            
        }
    }

    class ServerProgram : Socket
    {
        public delegate void ClientReceiveHandler(Socket sock, String msg); //수신 메시지 이벤트를 위한 델리게이트
        public event ClientReceiveHandler OnReceive;
        public delegate void ClientDisconnectHandler(Socket sock);
        public event ClientDisconnectHandler OnDisconnect;
        public delegate void ClientConnectHandler(Socket sock);
        public event ClientConnectHandler OnConnect;

        private bool _disposed = false;


        public Server serverSocket;
        public IPEndPoint ipEndPoint;
        public ServerProgram(int port) : base(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            ipEndPoint = new IPEndPoint(IPAddress.Any, port);
            try
            {
                base.Bind(ipEndPoint);
                base.Listen(20);
                // 비동기 소켓으로 Server 클래스를 선언한다. (IOCP로 집어넣는것)
                serverSocket = new Server(this);
                serverSocket.OnConnect += ClientConnect;
                serverSocket.OnDisconnect += ClientDisConnect;
                serverSocket.OnReceive += ClientRecieve;

                base.AcceptAsync(serverSocket);
            }
            catch (Exception ex)
            {
                return;
            }

        }

        private void ClientRecieve(Socket sock, string msg)
        {
            if (OnReceive != null)
                OnReceive(sock, msg);
        }

        private void ClientDisConnect(Socket sock)
        {
            if (OnDisconnect != null)
                OnDisconnect(sock);
        }

        private void ClientConnect(Socket sock)
        {
            if (OnConnect != null)
                OnConnect(sock);
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            try
            {
                if (serverSocket != null)
                {
                    serverSocket.SocketClose();
                    base.Disconnect(true);

                    base.Shutdown(SocketShutdown.Both);
                    base.Close();
                    base.Dispose();

                    GC.SuppressFinalize(serverSocket);
                }
                //serverSocket = null;
            }
            catch (Exception ex)
            {
                //
            }
            finally
            {
                //base.Close(0);
            }
            _disposed = true;
        }

        

        public void SendMessage(String msg)
        {
            if (serverSocket != null)
                serverSocket.SendAllMessage(msg);
        }
    }

}