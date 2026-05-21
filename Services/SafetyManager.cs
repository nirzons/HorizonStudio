using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using NINA.Profile.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Astrometry;
using NINA.Core.Utility;
using NINA.Equipment.Model;
using NINA.Core.Model.Equipment;
using NINA.Equipment.Equipment.MyTelescope;

namespace NirZonshine.NINA.HorizonVisualMapper.Services {
    public class SafetyManager : INotifyPropertyChanged, IDisposable, ITelescopeConsumer {
        private readonly IProfileService _profileService;
        private readonly ITelescopeMediator _telescopeMediator;
        private readonly SettingsManager _settingsManager;
        private readonly Timer _heartbeatTimer;
        private bool _disposed;

        private bool _isSolarSafetyAlert;
        private bool _isZenithSafetyAlert;
        private string _safetyMessage = "All safety systems nominal.";

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<string> SafetyLockoutTriggered;

        public SafetyManager(IProfileService profileService, ITelescopeMediator telescopeMediator, SettingsManager settingsManager) {
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            _telescopeMediator = telescopeMediator ?? throw new ArgumentNullException(nameof(telescopeMediator));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));

            // Register for telescope property updates
            _telescopeMediator.RegisterConsumer(this);

            // Start a 1Hz safety monitoring heartbeat
            _heartbeatTimer = new Timer(SafetyHeartbeat, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        }

        public void UpdateDeviceInfo(TelescopeInfo deviceInfo) {
            _latestTelescopeInfo = deviceInfo;
        }

        private TelescopeInfo _latestTelescopeInfo;

        public bool IsSolarSafetyAlert {
            get => _isSolarSafetyAlert;
            private set { if (_isSolarSafetyAlert != value) { _isSolarSafetyAlert = value; OnPropertyChanged(); } }
        }

        public bool IsZenithSafetyAlert {
            get => _isZenithSafetyAlert;
            private set { if (_isZenithSafetyAlert != value) { _isZenithSafetyAlert = value; OnPropertyChanged(); } }
        }

        public string SafetyMessage {
            get => _safetyMessage;
            private set { if (_safetyMessage != value) { _safetyMessage = value; OnPropertyChanged(); } }
        }

        private void SafetyHeartbeat(object state) {
            if (_disposed) return;

            try {
                if (_telescopeMediator == null || _telescopeMediator.GetInfo()?.Connected != true) {
                    return;
                }

                var currentPosition = _telescopeMediator.GetCurrentPosition();
                if (currentPosition == null) return;

                // Query current mount coordinates from the latest event info
                double currentAlt = _latestTelescopeInfo?.Altitude ?? _telescopeMediator.GetInfo()?.Altitude ?? 0.0;
                double currentAz = _latestTelescopeInfo?.Azimuth ?? _telescopeMediator.GetInfo()?.Azimuth ?? 0.0;

                // 1. Zenith Proximity Check
                if (_settingsManager.EnableZenithSafety) {
                    if (currentAlt > 85.0) {
                        IsZenithSafetyAlert = true;
                        SafetyMessage = $"[SAFETY WARNING] Zenith safety lock active: Altitude is {currentAlt:F2}° (Limit: 85°). Slew blocked.";
                        SafetyLockoutTriggered?.Invoke(this, "Zenith limit exceeded");
                        TriggerEmergencyStop();
                        return;
                    } else {
                        IsZenithSafetyAlert = false;
                    }
                } else {
                    IsZenithSafetyAlert = false;
                }

                // 2. Solar Proximity Check
                var sunCoords = CalculateSunPosition();
                if (sunCoords != null) {
                    double angularDistance = CalculateAngularDistance(currentAlt, currentAz, sunCoords.Alt, sunCoords.Az);
                    double threshold = _settingsManager.SafetyThreshold;

                    if (angularDistance < threshold) {
                        IsSolarSafetyAlert = true;
                        
                        if (_settingsManager.EnableSolarSafety) {
                            SafetyMessage = $"[SAFETY EXCLUSION] Solar Proximity Lock active! Mount points {angularDistance:F2}° from the Sun (Threshold: {threshold}°).";
                            SafetyLockoutTriggered?.Invoke(this, "Solar proximity detected");
                            TriggerEmergencyStop();
                        } else {
                            SafetyMessage = $"⚠ SOLAR ZONE WARNING: Mount points {angularDistance:F2}° from the Sun. Safety Lockout is DISABLED.";
                        }
                        return;
                    } else {
                        IsSolarSafetyAlert = false;
                    }
                } else {
                    IsSolarSafetyAlert = false;
                }

                SafetyMessage = "All safety systems nominal.";
            } catch (Exception ex) {
                Logger.Error($"[Horizon Visual Mapper] Safety heartbeat failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Validates if a target Alt/Az position is safe before initiating a slew.
        /// </summary>
        public bool IsTargetPositionSafe(double targetAlt, double targetAz, out string violationReason) {
            violationReason = string.Empty;

            // 1. Zenith Check
            if (_settingsManager.EnableZenithSafety && targetAlt > 85.0) {
                violationReason = $"Target Altitude ({targetAlt:F2}°) exceeds the safety limit of 85.0°.";
                return false;
            }

            // 2. Solar Check
            if (_settingsManager.EnableSolarSafety) {
                var sunCoords = CalculateSunPosition();
                if (sunCoords != null) {
                    double angularDistance = CalculateAngularDistance(targetAlt, targetAz, sunCoords.Alt, sunCoords.Az);
                    double threshold = _settingsManager.SafetyThreshold;
                    if (angularDistance < threshold) {
                        violationReason = $"Target slew is {angularDistance:F2}° from the Sun. Safety lockout threshold is {threshold}°.";
                        return false;
                    }
                }
            }

            return true;
        }

        private void TriggerEmergencyStop() {
            try {
                if (_telescopeMediator != null && _telescopeMediator.GetInfo()?.Connected == true) {
                    _telescopeMediator.StopSlew();
                }
            } catch (Exception ex) {
                Logger.Error($"[Horizon Visual Mapper] Emergency Stop failed: {ex.Message}");
            }
        }

        private class HorizontalCoordinates {
            public double Alt { get; }
            public double Az { get; }
            public HorizontalCoordinates(double alt, double az) {
                Alt = alt;
                Az = az;
            }
        }

        /// <summary>
        /// Calculates the redundant solar coordinates (Alt, Az) based on observer location and time.
        /// Uses low-precision standard solar position formulas (accurate to ~0.01°).
        /// </summary>
        private HorizontalCoordinates CalculateSunPosition() {
            try {
                if (_profileService?.ActiveProfile?.AstrometrySettings == null) return null;

                double lat = _profileService.ActiveProfile.AstrometrySettings.Latitude;
                double lon = _profileService.ActiveProfile.AstrometrySettings.Longitude;

                DateTime utcNow = DateTime.UtcNow;

                // Days since J2000.0 epoch (noon 1 Jan 2000 UTC)
                double d = (utcNow - new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc)).TotalDays;

                // Mean anomaly of the Sun
                double g = 357.529 + 0.98560028 * d;
                double gRad = g * Math.PI / 180.0;

                // Mean longitude of the Sun
                double q = 280.459 + 0.98564736 * d;

                // Geocentric ecliptic longitude of the Sun
                double L = q + 1.915 * Math.Sin(gRad) + 0.020 * Math.Sin(2 * gRad);
                double LRad = L * Math.PI / 180.0;

                // Obliquity of the ecliptic
                double e = 23.439 - 0.00000036 * d;
                double eRad = e * Math.PI / 180.0;

                // Sun's Right Ascension and Declination
                double raRad = Math.Atan2(Math.Cos(eRad) * Math.Sin(LRad), Math.Cos(LRad));
                double decRad = Math.Asin(Math.Sin(eRad) * Math.Sin(LRad));

                // Local Sidereal Time (LST) calculation
                // GMST at 0h UT
                double julianDays = d + 2451545.0;
                double T = (julianDays - 2451545.0) / 36525.0;
                double gmst = 280.46061837 + 360.98564736629 * (julianDays - 2451545.0) + T * T * (0.000387933 - T / 38710000.0);
                double lst = (gmst + lon) % 360.0;
                double lstRad = lst * Math.PI / 180.0;

                // Hour Angle (HA)
                double haRad = lstRad - raRad;

                // Latitude in radians
                double latRad = lat * Math.PI / 180.0;

                // Altitude
                double sinAlt = Math.Sin(latRad) * Math.Sin(decRad) + Math.Cos(latRad) * Math.Cos(decRad) * Math.Cos(haRad);
                double altRad = Math.Asin(sinAlt);
                double alt = altRad * 180.0 / Math.PI;

                // Azimuth
                double cosAz = (Math.Sin(decRad) - Math.Sin(latRad) * sinAlt) / (Math.Cos(latRad) * Math.Cos(altRad));
                double sinAz = -Math.Cos(decRad) * Math.Sin(haRad) / Math.Cos(altRad);
                double azRad = Math.Atan2(sinAz, cosAz);
                double az = (azRad * 180.0 / Math.PI + 360.0) % 360.0;

                return new HorizontalCoordinates(alt, az);
            } catch {
                return null;
            }
        }

        private double CalculateAngularDistance(double alt1, double az1, double alt2, double az2) {
            double rAlt1 = alt1 * Math.PI / 180.0;
            double rAz1 = az1 * Math.PI / 180.0;
            double rAlt2 = alt2 * Math.PI / 180.0;
            double rAz2 = az2 * Math.PI / 180.0;

            double cosDist = Math.Sin(rAlt1) * Math.Sin(rAlt2) + Math.Cos(rAlt1) * Math.Cos(rAlt2) * Math.Cos(rAz1 - rAz2);
            // Clamp to avoid float precision issues beyond [-1, 1]
            cosDist = Math.Max(-1.0, Math.Min(1.0, cosDist));
            return Math.Acos(cosDist) * 180.0 / Math.PI;
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            _heartbeatTimer?.Dispose();
            try { _telescopeMediator.RemoveConsumer(this); } catch { }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
