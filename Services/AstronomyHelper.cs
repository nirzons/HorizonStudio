using System;

namespace NirZonshine.NINA.HorizonStudio.Services {
    /// <summary>
    /// Provides shared astronomical coordinate utilities and geometry calculations.
    /// </summary>
    public static class AstronomyHelper {
        /// <summary>
        /// Calculates the angular distance (in degrees) between two spherical coordinates using the Law of Cosines.
        /// </summary>
        public static double GetAngularDistance(double az1, double alt1, double az2, double alt2) {
            double rad = Math.PI / 180.0;
            double rAz1 = az1 * rad;
            double rAlt1 = alt1 * rad;
            double rAz2 = az2 * rad;
            double rAlt2 = alt2 * rad;

            double cosTheta = Math.Sin(rAlt1) * Math.Sin(rAlt2) + Math.Cos(rAlt1) * Math.Cos(rAlt2) * Math.Cos(rAz1 - rAz2);
            cosTheta = Math.Max(-1.0, Math.Min(1.0, cosTheta));

            return Math.Acos(cosTheta) * 180.0 / Math.PI;
        }
    }
}
