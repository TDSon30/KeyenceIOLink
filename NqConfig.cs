namespace NQ_LRX_Demo
{
    public static class NqConfig
    {
        public const string NqIp = "192.168.3.250";
        public const byte IoLinkPort = 0x01;

        public const byte TO_Instance = 0x7C;
        public const ushort TO_Length = 154;

        public const byte OT_Instance = 0xFE;
        public const ushort OT_Length = 0;

        public const byte Config_Instance = 0x01;

        public const int Port1OffsetBytes = 6;
        public const double Resolution = 0.1;

        public const ushort IndexOpCmd = 104;
        public const byte SubIndex = 0x00;
        public const ushort ValZeroShift = 8;

        // Check IO update mỗi 1s
        public const int HealthCheckMs = 1000;
        public const int ReconnectDelayMs = 200; // nghỉ 200ms trước khi connect lại
    }
}
