using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NirZonshine.NINA.HorizonStudio.Domain {
    /// <summary>
    /// Represents a named terrestrial synchronization landmark with mutable coordinates.
    /// </summary>
    public class SyncLandmark : INotifyPropertyChanged {
        private const double RadarMaxRadius = 220.0;
        private const double RadarCenterOffset = 250.0;

        private string _name;
        private double _azimuth;
        private double _altitude;
        private bool _isSelected;

        public event PropertyChangedEventHandler PropertyChanged;

        public bool IsSelected {
            get => _isSelected;
            set {
                if (_isSelected != value) {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Id { get; }

        public string Name {
            get => _name;
            set {
                if (_name != value) {
                    _name = value;
                    OnPropertyChanged();
                }
            }
        }

        public double Azimuth {
            get => _azimuth;
            set {
                // Keep azimuth in [0, 360) range
                double normalized = (value % 360.0 + 360.0) % 360.0;
                if (Math.Abs(_azimuth - normalized) > 1e-9) {
                    _azimuth = normalized;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(RadarX));
                    OnPropertyChanged(nameof(RadarY));
                }
            }
        }

        public double Altitude {
            get => _altitude;
            set {
                // Clamp altitude to [-90, 90] range
                double clamped = Math.Max(-90.0, Math.Min(90.0, value));
                if (Math.Abs(_altitude - clamped) > 1e-9) {
                    _altitude = clamped;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(RadarX));
                    OnPropertyChanged(nameof(RadarY));
                }
            }
        }

        /// <summary>
        /// Canvas X coordinate for this landmark on the sky-dome radar display.
        /// </summary>
        public double RadarX {
            get {
                double r = RadarMaxRadius * (90.0 - Altitude) / 90.0;
                double rad = Azimuth * Math.PI / 180.0;
                return RadarCenterOffset + r * Math.Sin(rad);
            }
        }

        /// <summary>
        /// Canvas Y coordinate for this landmark on the sky-dome radar display.
        /// </summary>
        public double RadarY {
            get {
                double r = RadarMaxRadius * (90.0 - Altitude) / 90.0;
                double rad = Azimuth * Math.PI / 180.0;
                return RadarCenterOffset - r * Math.Cos(rad);
            }
        }

        public SyncLandmark(string name, double azimuth, double altitude) {
            Id = Guid.NewGuid().ToString();
            _name = name;
            _azimuth = (azimuth % 360.0 + 360.0) % 360.0;
            _altitude = Math.Max(-90.0, Math.Min(90.0, altitude));
        }

        public SyncLandmark(string id, string name, double azimuth, double altitude) {
            Id = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString() : id;
            _name = name;
            _azimuth = (azimuth % 360.0 + 360.0) % 360.0;
            _altitude = Math.Max(-90.0, Math.Min(90.0, altitude));
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public override string ToString() {
            return $"{Name} (Az: {Azimuth:F2}°, Alt: {Altitude:F2}°)";
        }
    }
}
