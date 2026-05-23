using System;

namespace NirZonshine.NINA.HorizonVisualMapper.Domain {
    /// <summary>
    /// Represents a single terrestrial obstruction node on the horizon.
    /// Coordinates are in degrees.
    /// </summary>
    public class HorizonNode {
        public double Azimuth { get; }
        public double Altitude { get; }
        public DateTime Timestamp { get; }

        public HorizonNode(double azimuth, double altitude) {
            // Keep azimuth in [0, 360) range
            Azimuth = (azimuth % 360.0 + 360.0) % 360.0;
            // Clamp altitude to [-90, 90] range
            Altitude = Math.Max(-90.0, Math.Min(90.0, altitude));
            Timestamp = DateTime.Now;
        }

        public double RadarX {
            get {
                // Altitude 90 is at center (r=0), Altitude 0 is at outer radius (r=120)
                double r = 120.0 * (90.0 - Altitude) / 90.0;
                double rad = Azimuth * Math.PI / 180.0;
                return 150.0 + r * Math.Sin(rad);
            }
        }

        public double RadarY {
            get {
                double r = 120.0 * (90.0 - Altitude) / 90.0;
                double rad = Azimuth * Math.PI / 180.0;
                return 150.0 - r * Math.Cos(rad);
            }
        }

        public override string ToString() {
            // Formatted for N.I.N.A. horizon file: "Azimuth Altitude"
            return $"{Azimuth:F2} {Altitude:F2}";
        }
    }
}
