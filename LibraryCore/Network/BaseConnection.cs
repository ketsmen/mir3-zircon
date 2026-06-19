using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Reflection;
using G = Library.Network.GeneralPackets;

namespace Library.Network
{
    public abstract class BaseConnection
    {
        public static readonly ConcurrentDictionary<string, DiagnosticValue> Diagnostics = new ConcurrentDictionary<string, DiagnosticValue>();
        public static Dictionary<(Type ConnectionType, Type PacketType), MethodInfo> PacketMethods = new Dictionary<(Type ConnectionType, Type PacketType), MethodInfo>();
        private static readonly object PacketMethodsLock = new object();
        public static bool Monitor;

        public bool Connected { get; set; }
        protected bool Sending { get; set; }

        public int TotalBytesSent { get; set; }
        public int TotalBytesReceived { get; set; }
        public int TotalPacketsProcessed { get; set; }

        public bool AdditionalLogging;

        protected TcpClient Client;

        public DateTime TimeConnected { get; set; }
        public TimeSpan Duration => Time.Now - TimeConnected;

        protected abstract TimeSpan TimeOutDelay { get; }
        public DateTime TimeOutTime { get; set; }

        private bool _disconnecting;
        public bool Disconnecting
        {
            get { return _disconnecting; }
            set
            {
                if (_disconnecting == value) return;
                _disconnecting = value;
                TimeOutTime = Time.Now.AddSeconds(2);
            }
        }

        public ConcurrentQueue<Packet> ReceiveList = new ConcurrentQueue<Packet>();
        public ConcurrentQueue<Packet> SendList = new ConcurrentQueue<Packet>();
        private byte[] _rawData = new byte[0];

        public EventHandler<Exception> OnException;

        protected BaseConnection(TcpClient client)
        {
            Client = client;
            Client.NoDelay = true;

            Connected = true;
            TimeConnected = Time.Now;

            TotalPacketsProcessed = 0;
        }

        protected void BeginReceive()
        {
            try
            {
                TcpClient client = Client;
                if (client == null || !client.Connected) return;

                byte[] rawBytes = new byte[8 * 1024];

                client.Client.BeginReceive(rawBytes, 0, rawBytes.Length, SocketFlags.None, ReceiveData, rawBytes);
            }
            catch (Exception ex)
            {
                if (AdditionalLogging)
                    OnException?.Invoke(this, ex);
                Disconnecting = true;
            }
        }
        private void ReceiveData(IAsyncResult result)
        {
            try
            {
                if (!Connected) return;

                TcpClient client = Client;
                if (client == null) return;

                int dataRead = client.Client.EndReceive(result);

                if (dataRead == 0)
                {
                    Disconnecting = true;
                    return;
                }

                TotalBytesReceived += dataRead;

                UpdateTimeOut();

                byte[] rawBytes = result.AsyncState as byte[];

                byte[] temp = _rawData;
                _rawData = new byte[dataRead + temp.Length];
                Buffer.BlockCopy(temp, 0, _rawData, 0, temp.Length);
                Buffer.BlockCopy(rawBytes, 0, _rawData, temp.Length, dataRead);

                ConcurrentQueue<Packet> receiveList = ReceiveList;
                if (receiveList == null) return;

                Packet p;

                while ((p = Packet.ReceivePacket(_rawData, out _rawData)) != null)
                {
                    receiveList.Enqueue(p);
                    TotalPacketsProcessed++;
                }

                BeginReceive();
            }
            catch (Exception ex)
            {
                if (AdditionalLogging)
                    OnException?.Invoke(this, ex);
                Disconnecting = true;
            }
        }
        private void BeginSend(List<byte> data)
        {
            if (!Connected || data.Count == 0) return;

            try
            {
                TcpClient client = Client;
                if (client == null) return;

                Sending = true;
                TotalBytesSent += data.Count;
                client.Client.BeginSend(data.ToArray(), 0, data.Count, SocketFlags.None, SendData, null);
                UpdateTimeOut();
            }
            catch (Exception ex)
            {
                if (AdditionalLogging)
                    OnException?.Invoke(this, ex);
                Disconnecting = true;
                Sending = false;
            }
        }
        private void SendData(IAsyncResult result)
        {
            try
            {
                Sending = false;
                TcpClient client = Client;
                if (client == null) return;

                client.Client.EndSend(result);
                UpdateTimeOut();
            }
            catch (Exception ex)
            {
                if (AdditionalLogging)
                    OnException?.Invoke(this, ex);
                Disconnecting = true;
            }
        }
        public virtual void Enqueue(Packet p)
        {
            ConcurrentQueue<Packet> sendList = SendList;
            if (!Connected || p == null || sendList == null) return;

            sendList.Enqueue(p);
        }

        public abstract void TryDisconnect();

        public virtual void Disconnect()
        {
            if (!Connected) return;

            Connected = false;

            SendList = null;
            ReceiveList = null;
            _rawData = null;

            TcpClient client = Client;
            Client = null;

            client?.Close();
        }

        public abstract void TrySendDisconnect(Packet p);

        public virtual void SendDisconnect(Packet p)
        {
            if (!Connected || Disconnecting)
            {
                Disconnecting = true;
                return;
            }

            List<byte> data = new List<byte>();

            data.AddRange(p.GetPacketBytes());

            BeginSendDisconnect(data);
        }
        private void BeginSendDisconnect(List<byte> data)
        {
            if (!Connected || data.Count == 0) return;

            if (Disconnecting) return;

            try
            {
                Disconnecting = true;

                TcpClient client = Client;
                if (client == null) return;

                TotalBytesSent += data.Count;
                client.Client.BeginSend(data.ToArray(), 0, data.Count, SocketFlags.None, SendDataDisconnect, null);
            }
            catch (Exception ex)
            {
                if (AdditionalLogging)
                    OnException?.Invoke(this, ex);
            }
        }
        private void SendDataDisconnect(IAsyncResult result)
        {

            try
            {
                TcpClient client = Client;
                if (client == null) return;

                client.Client.EndSend(result);
            }
            catch (Exception ex)
            {
                if (AdditionalLogging)
                    OnException?.Invoke(this, ex);
            }
        }

        public virtual void Process()
        {
            TcpClient client = Client;
            if (client == null || !client.Connected)
            {
                TryDisconnect();
                return;
            }

            ConcurrentQueue<Packet> receiveList = ReceiveList;
            ConcurrentQueue<Packet> sendList = SendList;
            if (receiveList == null || sendList == null)
            {
                TryDisconnect();
                return;
            }

            while (!receiveList.IsEmpty && !Disconnecting)
            {
                try
                {
                    Packet p;
                    if (!receiveList.TryDequeue(out p)) continue;

                    ProcessPacket(p);
                }
                catch (NotImplementedException ex)
                {
                    OnException?.Invoke(this, ex);
                    Disconnecting = true;
                }
                catch (Exception ex)
                {
                    OnException?.Invoke(this, ex);
                    throw;
                }
            }

            if (Time.Now >= TimeOutTime)
            {
                if (!Disconnecting)
                    TrySendDisconnect(new G.Disconnect { Reason = DisconnectReason.TimedOut });
                else
                    TryDisconnect();

                return;
            }

            if (!Disconnecting && Sending)
                UpdateTimeOut();

            if (sendList.IsEmpty || Sending) return;

            List<byte> data = new List<byte>();
            while (!sendList.IsEmpty)
            {
                Packet p;

                if (!sendList.TryDequeue(out p)) continue;

                if (p == null) continue;

                try
                {
                    byte[] bytes = p.GetPacketBytes();

                    data.AddRange(bytes);
                }
                catch (Exception ex)
                {
                    OnException?.Invoke(this, ex);
                    Disconnecting = true;
                    return;
                }


                if (!Monitor) continue;

                Type type = p.GetType();
                DiagnosticValue value = Diagnostics.GetOrAdd(type.FullName, name => new DiagnosticValue { Name = name });

                lock (value)
                {
                    value.Count++;
                    value.TotalSize += p.Length;

                    if (p.Length > value.LargestSize)
                        value.LargestSize = p.Length;
                }
            }

            BeginSend(data);
        }

        private void ProcessPacket(Packet p)
        {
            if (p == null) return;

            DateTime start = Time.Now;

            Type connectionType = GetType();
            (Type ConnectionType, Type PacketType) key = (connectionType, p.PacketType);

            MethodInfo info;
            lock (PacketMethodsLock)
            {
                if (!PacketMethods.TryGetValue(key, out info))
                {
                    info = connectionType.GetMethod("Process", new[] { p.PacketType });
                    PacketMethods[key] = info;
                }
            }

            if (info == null)
            {
                ProcessUnhandledPacket(p);
                return;
            }

            info.Invoke(this, new object[] { p });

            if (!Monitor) return;

            TimeSpan execution = Time.Now - start;
            DiagnosticValue value = Diagnostics.GetOrAdd(p.PacketType.FullName, name => new DiagnosticValue { Name = name });

            lock (value)
            {
                value.Count++;
                value.TotalTime += execution;
                value.TotalSize += p.Length;

                if (execution > value.LargestTime)
                    value.LargestTime = execution;

                if (p.Length > value.LargestSize)
                    value.LargestSize = p.Length;
            }
        }

        protected virtual void ProcessUnhandledPacket(Packet p)
        {
            throw new NotImplementedException($"Not Implemented Exception: Method Process({p.PacketType}).");
        }

        public void UpdateTimeOut()
        {
            if (Disconnecting) return;

            TimeOutTime = Time.Now + TimeOutDelay;
        }
    }


    public class DiagnosticValue
    {
        public string Name { get; set; }
        public TimeSpan TotalTime { get; set; }
        public TimeSpan LargestTime { get; set; }
        public int Count { get; set; }
        public long TotalSize { get; set; }
        public long LargestSize { get; set; }

        public long TotalTicks => TotalTime.Ticks;
        public long TotalMilliseconds => TotalTicks / TimeSpan.TicksPerMillisecond;

        public long LargestTicks => LargestTime.Ticks;
        public long LargestMilliseconds => LargestTicks / TimeSpan.TicksPerMillisecond;
    }
}
