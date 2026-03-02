using Sres.Net.EEIP;

namespace NQ_LRX_Demo
{
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