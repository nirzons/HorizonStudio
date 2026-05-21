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

        public override string ToString() {
            // Formatted for N.I.N.A. horizon file: "Azimuth Altitude"
            return $"{Azimuth:F2} {Altitude:F2}";
        }
    }
}
