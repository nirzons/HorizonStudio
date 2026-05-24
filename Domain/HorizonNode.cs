using System;

namespace NirZonshine.NINA.HorizonStudio.Domain {
    /// <summary>
    /// Represents a single terrestrial obstruction node on the horizon.
    /// Coordinates are in degrees.
    /// </summary>
    public class HorizonNode {
        // Radar canvas geometry constants — must match the 300x300 Canvas in Options.xaml.
        // Center is at (RadarCenterOffset, RadarCenterOffset); horizon ring radius is RadarMaxRadius.
        private const double RadarMaxRadius = 120.0;
        private const double RadarCenterOffset = 150.0;

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

        /// <summary>
        /// Canvas X coordinate for this node on the sky-dome radar display.
        /// Altitude 90° maps to the center; Altitude 0° maps to the outer ring.
        /// </summary>
        public double RadarX {
            get {
                double r = RadarMaxRadius * (90.0 - Altitude) / 90.0;
                double rad = Azimuth * Math.PI / 180.0;
                return RadarCenterOffset + r * Math.Sin(rad);
            }
        }

        /// <summary>
        /// Canvas Y coordinate for this node on the sky-dome radar display.
        /// </summary>
        public double RadarY {
            get {
                double r = RadarMaxRadius * (90.0 - Altitude) / 90.0;
                double rad = Azimuth * Math.PI / 180.0;
                return RadarCenterOffset - r * Math.Cos(rad);
            }
        }

        public override string ToString() {
            // Formatted for N.I.N.A. horizon file: "Azimuth Altitude"
            return $"{Azimuth:F2} {Altitude:F2}";
        }
    }
}
