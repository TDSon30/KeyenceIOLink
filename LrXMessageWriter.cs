using System;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using Sres.Net.EEIP;

namespace NQ_LRX_Demo
{
    public class LrXMessageWriter
    {
        private readonly EEIPClient _client;

        public LrXMessageWriter(EEIPClient sharedClient)
        {
            _client = sharedClient;
        }

        public void SendZeroShift(byte port)
        {
            Console.WriteLine("\n[Message] Đang gửi lệnh Zero-Shift...");

            try
            {
                var stream = GetPrivateStream(_client);
                var sessionHandle = GetPrivateSessionHandle(_client);

                if (stream == null || !stream.CanWrite)
                {
                    Console.WriteLine("[Lỗi] Không thể lấy NetworkStream từ Client. Vui lòng kiểm tra kết nối.");
                    return;
                }

                stream.ReadTimeout = 1000;
                stream.WriteTimeout = 1000;

                byte[] isduPayload = new byte[] {
                    (byte)NqConfig.IndexOpCmd, (byte)(NqConfig.IndexOpCmd >> 8), // Index LE
                    NqConfig.SubIndex,
                    (byte)(NqConfig.ValZeroShift >> 8), (byte)NqConfig.ValZeroShift // Data BE
                };

                byte[] path = new byte[] { 0x20, 0x85, 0x24, 0x01, 0x30, port };

                int requestDataLen = 1 + 1 + path.Length + isduPayload.Length;
                int cmdSpecificLen = 6 + 2 + 4 + 4 + requestDataLen;
                byte[] packet = new byte[24 + cmdSpecificLen];

                packet[0] = 0x6F; // SendRRData
                BitConverter.GetBytes((ushort)cmdSpecificLen).CopyTo(packet, 2);
                BitConverter.GetBytes(sessionHandle).CopyTo(packet, 4);

                int ptr = 24;
                ptr += 6; // Interface + Timeout = 0
                packet[ptr++] = 0x02; packet[ptr++] = 0x00; // Item Count

                // Address Item (NULL)
                packet[ptr++] = 0x00; packet[ptr++] = 0x00;
                packet[ptr++] = 0x00; packet[ptr++] = 0x00;

                // Unconnected Data Item
                packet[ptr++] = 0xB2; packet[ptr++] = 0x00;
                BitConverter.GetBytes((ushort)requestDataLen).CopyTo(packet, ptr);
                ptr += 2;

                // CIP Body
                packet[ptr++] = 0x4C; // ISDU_Write
                packet[ptr++] = (byte)(path.Length / 2);
                Array.Copy(path, 0, packet, ptr, path.Length); ptr += path.Length;
                Array.Copy(isduPayload, 0, packet, ptr, isduPayload.Length);

                stream.Write(packet, 0, packet.Length);

                byte[] buffer = new byte[1024];

                int retries = 0;
                while (!stream.DataAvailable && retries < 20)
                {
                    Thread.Sleep(50);
                    retries++;
                }

                if (stream.DataAvailable)
                {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    ParseResponse(buffer, bytesRead);
                }
                else
                {
                    Console.WriteLine("-> KẾT QUẢ: Không có phản hồi (Timeout).");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"-> Lỗi Gửi lệnh: {ex.Message}");
            }

            Console.WriteLine("------------------------------------------------");
        }

        private void ParseResponse(byte[] buffer, int bytesRead)
        {
            if (bytesRead > 42)
            {
                byte serviceResp = buffer[40];
                byte status = buffer[42];

                if (serviceResp == 0xCC && status == 0x00)
                {
                    Console.WriteLine("-> KẾT QUẢ: THÀNH CÔNG (Success).");
                }
                else
                {
                    Console.WriteLine($"-> KẾT QUẢ: THẤT BẠI (General Status: 0x{status:X2}).");

                    if (status == 0x1E && bytesRead >= 2)
                    {
                        int errIndex = bytesRead - 2;
                        ushort ioErr = (ushort)((buffer[errIndex] << 8) | buffer[errIndex + 1]);
                        Console.WriteLine($"   Mã lỗi IO-Link Chi tiết: 0x{ioErr:X4}");
                        DecodeError(ioErr);
                    }
                }
            }
            else
            {
                Console.WriteLine("-> KẾT QUẢ: Gói phản hồi quá ngắn / không hợp lệ.");
            }
        }

        private void DecodeError(ushort err)
        {
            string msg = "Tra cứu tài liệu";
            switch (err)
            {
                case 0x8011: msg = "Index không tồn tại (Kiểm tra lại Little/Big Endian)"; break;
                case 0x8030: msg = "Giá trị ngoài phạm vi (Value out of range)"; break;
                case 0x8033: msg = "Độ dài dữ liệu quá dài (Thừa byte)"; break;
                case 0x8034: msg = "Độ dài dữ liệu quá ngắn (Thiếu byte)"; break;
                case 0x8040: msg = "Tham số không hợp lệ"; break;
            }
            Console.WriteLine($"   Ý nghĩa: {msg}");
        }

        private NetworkStream GetPrivateStream(EEIPClient client)
        {
            try
            {
                var type = client.GetType();
                var flags = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public;
                var fields = new string[] { "ns", "stream", "networkStream", "_stream" };
                foreach (var name in fields)
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
}
