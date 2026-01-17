using System;
using System.Net.Sockets;
using System.Reflection;
using Sres.Net.EEIP;

namespace NQ_LRX_Demo
{
    public class LrXCyclicReader
    {
        // ✅ Có reader.Client như bạn cần
        public EEIPClient Client { get; private set; }

        // ✅ Có reader.IsConnected như bạn cần
        public bool IsConnected { get; private set; } = false;

        public LrXCyclicReader()
        {
            Client = new EEIPClient();
        }

        // ✅ Có reader.Connect()
        public void Connect()
        {
            try
            {
                Console.Write($"[Cyclic] Đang kết nối đến {NqConfig.NqIp}... ");
                Client.IPAddress = NqConfig.NqIp;
                Client.RegisterSession();

                Client.ConfigurationAssemblyInstanceID = NqConfig.Config_Instance;

                // O->T (PC -> NQ)
                Client.O_T_InstanceID = NqConfig.OT_Instance;
                Client.O_T_Length = NqConfig.OT_Length;
                Client.O_T_RealTimeFormat = RealTimeFormat.Modeless;
                Client.O_T_OwnerRedundant = false;
                Client.O_T_Priority = Priority.Low;
                Client.O_T_VariableLength = false;
                Client.O_T_ConnectionType = ConnectionType.Point_to_Point;
                Client.RequestedPacketRate_O_T = 200000;

                // T->O (NQ -> PC)
                Client.T_O_InstanceID = NqConfig.TO_Instance;
                Client.T_O_Length = NqConfig.TO_Length;
                Client.T_O_RealTimeFormat = RealTimeFormat.Modeless;
                Client.T_O_OwnerRedundant = false;
                Client.T_O_Priority = Priority.Scheduled;
                Client.T_O_VariableLength = false;

                // Nếu multicast hay “ngắt”, thử đổi sang Point_to_Point
                Client.T_O_ConnectionType = ConnectionType.Multicast;
                // Client.T_O_ConnectionType = ConnectionType.Point_to_Point;

                Client.RequestedPacketRate_T_O = 200000;

                Client.ForwardOpen();
                IsConnected = true;

                Console.WriteLine("OK. (ForwardOpen Success)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[Cyclic] Lỗi kết nối: {ex.Message}");
                IsConnected = false;
            }
        }

        // ✅ Có reader.Disconnect()
        public void Disconnect()
        {
            try
            {
                if (IsConnected) Client.ForwardClose();
                Client.UnRegisterSession();
            }
            catch { }
            IsConnected = false;
        }

        public void Reconnect()
        {
            Disconnect();
            System.Threading.Thread.Sleep(200);
            Client = new EEIPClient();      // tạo mới để sạch session
            Connect();
        }

        // ✅ Đọc dữ liệu process data như bạn đang parse
        public LrXData Read()
        {
            if (!IsConnected || Client.T_O_IOData == null || Client.T_O_IOData.Length < NqConfig.Port1OffsetBytes + 4)
                return new LrXData { IsValid = false };

            int b = NqConfig.Port1OffsetBytes;
            byte[] buf = Client.T_O_IOData;

            byte b0 = buf[b];
            byte b1 = buf[b + 1];
            byte b2 = buf[b + 2];
            byte b3 = buf[b + 3];

            short distanceRaw = (short)(b0 | (b1 << 8));
            ushort status = (ushort)(b2 | (b3 << 8));

            double distanceMm = distanceRaw * NqConfig.Resolution;

            return new LrXData
            {
                IsValid = true,
                DistanceMm = distanceMm,
                Out1 = (status & 0x0001) != 0,
                Out2 = (status & 0x0002) != 0,
                Warning = (status & 0x0040) != 0,
                Error = (status & 0x0800) != 0
            };
        }

        // ✅ HEARTBEAT: không dựa data đổi
        // CIP Get_Attribute_Single tới Identity Object (Class 0x01, Instance 1, Attribute 1)
        // Trả TRUE nếu thiết bị còn phản hồi
        public bool PingIdentity(int timeoutMs = 300)
        {
            if (!IsConnected) return false;

            try
            {
                var stream = GetPrivateStream(Client);
                var sessionHandle = GetPrivateSessionHandle(Client);

                if (stream == null || !stream.CanWrite) return false;

                stream.ReadTimeout = timeoutMs;
                stream.WriteTimeout = timeoutMs;

                byte service = 0x0E; // Get_Attribute_Single
                byte[] path = new byte[] { 0x20, 0x01, 0x24, 0x01, 0x30, 0x01 }; // class1 inst1 attr1
                byte pathWords = (byte)(path.Length / 2);

                int cipLen = 2 + path.Length;
                int cmdSpecificLen = 6 + 2 + 4 + 4 + (2 + 2 + cipLen);
                byte[] packet = new byte[24 + cmdSpecificLen];

                packet[0] = 0x6F; // SendRRData
                BitConverter.GetBytes((ushort)cmdSpecificLen).CopyTo(packet, 2);
                BitConverter.GetBytes((uint)sessionHandle).CopyTo(packet, 4);

                int ptr = 24;
                ptr += 6; // Interface handle + timeout = 0

                packet[ptr++] = 0x02; packet[ptr++] = 0x00; // item count

                // Null address item
                packet[ptr++] = 0x00; packet[ptr++] = 0x00;
                packet[ptr++] = 0x00; packet[ptr++] = 0x00;

                // Unconnected data item
                packet[ptr++] = 0xB2; packet[ptr++] = 0x00;
                BitConverter.GetBytes((ushort)cipLen).CopyTo(packet, ptr);
                ptr += 2;

                // CIP
                packet[ptr++] = service;
                packet[ptr++] = pathWords;
                Array.Copy(path, 0, packet, ptr, path.Length);

                stream.Write(packet, 0, packet.Length);

                byte[] resp = new byte[512];
                int read = stream.Read(resp, 0, resp.Length);

                if (read > 42)
                {
                    // theo kiểu parse của bạn: general status ở byte 42
                    byte generalStatus = resp[42];
                    return generalStatus == 0x00;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        // ===== helper reflection =====
        private NetworkStream GetPrivateStream(EEIPClient client)
        {
            try
            {
                var type = client.GetType();
                var flags = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public;
                foreach (var name in new[] { "ns", "stream", "networkStream", "_stream" })
                {
                    var field = type.GetField(name, flags);
                    if (field != null) return field.GetValue(client) as NetworkStream;
                }
                return null;
            }
            catch { return null; }
        }

        private uint GetPrivateSessionHandle(EEIPClient client)
        {
            try
            {
                var type = client.GetType();
                var flags = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public;

                var prop = type.GetProperty("SessionHandle", flags);
                if (prop != null) return (uint)prop.GetValue(client);

                var field = type.GetField("sessionHandle", flags) ?? type.GetField("_sessionHandle", flags);
                if (field != null) return (uint)field.GetValue(client);

                return 0;
            }
            catch { return 0; }
        }
    }

    public class LrXData
    {
        public bool IsValid { get; set; }
        public double DistanceMm { get; set; }
        public bool Out1 { get; set; }
        public bool Out2 { get; set; }
        public bool Warning { get; set; }
        public bool Error { get; set; }
    }
}
