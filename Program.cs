using System;
using System.Text;
using System.Threading;

namespace NQ_LRX_Demo
{
    class Program
    {
        private LrXCyclicReader _reader;
        private LrXMessageWriter _writer;

        private CancellationTokenSource _cts;
        private bool _linkOk = false;

        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            var reader = new LrXCyclicReader();
            Console.WriteLine("--- KHỞI TẠO HỆ THỐNG ---");

            reader.Connect();

            LrXMessageWriter? writer = reader.IsConnected ? new LrXMessageWriter(reader.Client) : null;

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                reader.Disconnect();
                Environment.Exit(0);
            };

            DateTime nextPing = DateTime.UtcNow.AddSeconds(1);
            bool linkOk = reader.IsConnected;

            while (true)
            {
                // Gửi lệnh Zero-Shift
                if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Enter)
                {
                    writer?.SendZeroShift(NqConfig.IoLinkPort);
                }

                // ---- Ping kết nối mỗi 1 giây ----
                if (DateTime.UtcNow >= nextPing)
                {
                    nextPing = DateTime.UtcNow.AddSeconds(1);

                    linkOk = reader.PingIdentity(300);

                    if (!linkOk)
                    {
                        Console.Write("\r[PING FAIL] Mất phản hồi -> Reconnect...                             ");
                        reader.Reconnect();

                        writer = reader.IsConnected
                            ? new LrXMessageWriter(reader.Client)
                            : null;

                        linkOk = reader.IsConnected && reader.PingIdentity(300);
                    }
                }

                // Đọc dữ liệu process data
                var data = reader.Read();

                // Hiển thị
                if (reader.IsConnected && linkOk && data.IsValid)
                {
                    Console.Write(
                        $"\rDist: {data.DistanceMm,7:0.1} mm | " +
                        $"OUT1:{(data.Out1 ? 1 : 0)} OUT2:{(data.Out2 ? 1 : 0)} " +
                        $"Warn:{(data.Warning ? 1 : 0)} Err:{(data.Error ? 1 : 0)} | Link: OK        ");
                }
                else
                {
                    if (!reader.IsConnected)
                        Console.Write("\r[DISCONNECTED]                                                     ");
                    else if (!linkOk)
                        Console.Write("\r[LINK DOWN] (Ping Identity fail)                                   ");
                    else
                        Console.Write("\r[WAITING] Đang chờ dữ liệu cyclic...                               ");
                }

                Thread.Sleep(100);
            }
        }
    }
}
