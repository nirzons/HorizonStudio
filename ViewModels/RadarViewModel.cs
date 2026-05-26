using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace NirZonshine.NINA.HorizonStudio.ViewModels {
    public class RadarViewModel : SubViewModelBase, IRadarClickHandler {
        private readonly HorizonMapperDockableVM _parent;

        public HorizonMapperDockableVM Parent => _parent;

        public double TelescopeRadarX {
            get {
                double r = 220.0 * (90.0 - _parent.CurrentAlt) / 90.0;
                double rad = _parent.CurrentAz * Math.PI / 180.0;
                return 250.0 + r * Math.Sin(rad);
            }
        }

        public double TelescopeRadarY {
            get {
                double r = 220.0 * (90.0 - _parent.CurrentAlt) / 90.0;
                double rad = _parent.CurrentAz * Math.PI / 180.0;
                return 250.0 - r * Math.Cos(rad);
            }
        }

        public double ActiveNodeRadarX => _parent.ActiveNode?.RadarX ?? 250.0;
        public double ActiveNodeRadarY => _parent.ActiveNode?.RadarY ?? 250.0;

        public int VerificationStepSize {
            get => _parent.SettingsManager.VerificationStepSize;
            set {
                _parent.SettingsManager.VerificationStepSize = value;
                RaisePropertyChanged(nameof(VerificationStepSize));
            }
        }

        public List<int> VerificationStepSizes { get; } = new List<int> { 1, 2, 5, 10, 20, 50 };

        public bool CanVerifyPoints => _parent.IsMountConnected && !_parent.IsSlewing && !_parent.IsActionSlewing && _parent.HorizonNodes.Count > 0 && !_parent.IsSyncPreparing;

        public bool CanJog => _parent.IsMountConnected && !_parent.IsSlewing && !_parent.IsActionSlewing;

        public PointCollection RadarHorizonPoints {
            get {
                var points = new PointCollection();
                if (_parent.HorizonNodes.Count == 0) return points;

                var sortedNodes = new List<Domain.HorizonNode>(_parent.HorizonNodes);
                sortedNodes.Sort((a, b) => a.Azimuth.CompareTo(b.Azimuth));

                if (sortedNodes.Count == 1) {
                    points.Add(new System.Windows.Point(sortedNodes[0].RadarX, sortedNodes[0].RadarY));
                    return points;
                }

                for (double az = 0.0; az < 360.0; az += 1.0) {
                    double alt = GetInterpolatedAltitude(az);
                    double r = 220.0 * (90.0 - alt) / 90.0;
                    double rad = az * Math.PI / 180.0;
                    double x = 250.0 + r * Math.Sin(rad);
                    double y = 250.0 - r * Math.Cos(rad);
                    points.Add(new System.Windows.Point(x, y));
                }

                double startAlt = GetInterpolatedAltitude(0.0);
                double startR = 220.0 * (90.0 - startAlt) / 90.0;
                points.Add(new System.Windows.Point(250.0, 250.0 - startR));

                return points;
            }
        }

        public PointCollection RadarObstructionPoints {
            get {
                var points = new PointCollection();
                if (_parent.HorizonNodes.Count == 0) return points;

                for (double az = 0.0; az <= 360.0; az += 2.0) {
                    double rad = az * Math.PI / 180.0;
                    double x = 250.0 + 220.0 * Math.Sin(rad);
                    double y = 250.0 - 220.0 * Math.Cos(rad);
                    points.Add(new System.Windows.Point(x, y));
                }

                for (double az = 360.0; az >= 0.0; az -= 2.0) {
                    double alt = GetInterpolatedAltitude(az);
                    double r = 220.0 * (90.0 - alt) / 90.0;
                    double rad = az * Math.PI / 180.0;
                    double x = 250.0 + r * Math.Sin(rad);
                    double y = 250.0 - r * Math.Cos(rad);
                    points.Add(new System.Windows.Point(x, y));
                }

                return points;
            }
        }

        public RadarViewModel(HorizonMapperDockableVM parent) {
            _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        }

        public double GetInterpolatedAltitude(double azimuth) {
            var nodes = _parent.HorizonNodes;
            if (nodes.Count == 0) return 0.0;
            if (nodes.Count == 1) return nodes[0].Altitude;

            var sorted = new List<Domain.HorizonNode>(nodes);
            sorted.Sort((a, b) => a.Azimuth.CompareTo(b.Azimuth));

            azimuth = (azimuth % 360.0 + 360.0) % 360.0;

            for (int i = 0; i < sorted.Count - 1; i++) {
                if (azimuth >= sorted[i].Azimuth && azimuth <= sorted[i + 1].Azimuth) {
                    double range = sorted[i + 1].Azimuth - sorted[i].Azimuth;
                    if (range == 0.0) return sorted[i].Altitude;
                    double t = (azimuth - sorted[i].Azimuth) / range;
                    return sorted[i].Altitude + t * (sorted[i + 1].Altitude - sorted[i].Altitude);
                }
            }

            double lastAz = sorted[sorted.Count - 1].Azimuth;
            double firstAz = sorted[0].Azimuth;
            double lastAlt = sorted[sorted.Count - 1].Altitude;
            double firstAlt = sorted[0].Altitude;

            double gapSize = (firstAz - lastAz + 360.0) % 360.0;
            if (gapSize == 0.0) return lastAlt;

            double diff = (azimuth - lastAz + 360.0) % 360.0;
            double tGap = diff / gapSize;
            return lastAlt + tGap * (firstAlt - lastAlt);
        }

        public bool IsNearHorizon(double canvasX, double canvasY) {
            double dx = canvasX - 250.0;
            double dy = 250.0 - canvasY;
            double r = Math.Sqrt(dx * dx + dy * dy);
            if (r > 220.0) r = 220.0;

            double rad = Math.Atan2(dx, dy);
            double azimuth = rad * 180.0 / Math.PI;
            azimuth = (azimuth % 360.0 + 360.0) % 360.0;
            double altitude = 90.0 - (90.0 * r / 220.0);
            if (altitude < 0.0) altitude = 0.0;
            if (altitude > 90.0) altitude = 90.0;

            foreach (var landmark in _parent.SyncLandmarks) {
                double distToSpecial = GetAngularDistance(azimuth, altitude, landmark.Azimuth, landmark.Altitude);
                if (distToSpecial < 2.5) {
                    return true;
                }
            }

            if (_parent.HorizonNodes.Count == 0) return true;

            double horizonAlt = GetInterpolatedAltitude(azimuth);
            return Math.Abs(altitude - horizonAlt) <= 5.0;
        }

        public void HandleRadarClick(double x, double y) {
            _parent.HandleRadarClick(x, y);
        }

        public void NotifyParentPropertiesChanged() {
            RaisePropertyChanged(nameof(TelescopeRadarX));
            RaisePropertyChanged(nameof(TelescopeRadarY));
            RaisePropertyChanged(nameof(CanVerifyPoints));
            RaisePropertyChanged(nameof(CanJog));
        }

        public void NotifyActiveNodeChanged() {
            RaisePropertyChanged(nameof(ActiveNodeRadarX));
            RaisePropertyChanged(nameof(ActiveNodeRadarY));
        }

        public void NotifyHorizonNodesChanged() {
            RaisePropertyChanged(nameof(RadarHorizonPoints));
            RaisePropertyChanged(nameof(RadarObstructionPoints));
            RaisePropertyChanged(nameof(CanVerifyPoints));
        }

        private double GetAngularDistance(double az1, double alt1, double az2, double alt2) {
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
