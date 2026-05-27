using System;
using System.Threading;
using System.Threading.Tasks;
using NirZonshine.NINA.HorizonStudio.Domain;
using NirZonshine.NINA.HorizonStudio.Services;

namespace NirZonshine.NINA.HorizonStudio.ViewModels.Commands {
    public partial class NavigationCommands {
        private class SlewTarget {
            public double Azimuth { get; }
            public double Altitude { get; }
            public HorizonNode Node { get; }
            public SyncLandmark Landmark { get; }

            public SlewTarget(HorizonNode node) {
                Azimuth = node.Azimuth;
                Altitude = node.Altitude;
                Node = node;
            }

            public SlewTarget(SyncLandmark landmark) {
                Azimuth = landmark.Azimuth;
                Altitude = landmark.Altitude;
                Landmark = landmark;
            }
        }

        public void SlewCCW() {
            var targets = new System.Collections.Generic.List<SlewTarget>();
            foreach (var node in _vm.HorizonNodes) {
                targets.Add(new SlewTarget(node));
            }
            foreach (var landmark in _vm.SyncLandmarks) {
                targets.Add(new SlewTarget(landmark));
            }

            int count = targets.Count;
            if (count == 0) return;

            // Sort targets by Azimuth
            targets.Sort((a, b) => a.Azimuth.CompareTo(b.Azimuth));

            double currentAz = _vm.CurrentAz;
            int closestIndex = -1;
            double minDistance = 360.0;

            for (int i = 0; i < count; i++) {
                double diff = Math.Abs(currentAz - targets[i].Azimuth) % 360.0;
                double dist = diff > 180.0 ? 360.0 - diff : diff;
                if (dist < minDistance) {
                    minDistance = dist;
                    closestIndex = i;
                }
            }

            int ccwIndex;
            if (minDistance < 1.0) {
                ccwIndex = (closestIndex - _vm.VerificationStepSize) % count;
                if (ccwIndex < 0) ccwIndex += count;
            } else {
                ccwIndex = closestIndex;
            }

            var target = targets[ccwIndex];
            double targetAz = target.Azimuth;
            double diffSlew = Math.Abs(currentAz - targetAz) % 360.0;
            double deltaAz = diffSlew > 180.0 ? 360.0 - diffSlew : diffSlew;

            if (deltaAz > 45.0) {
                var result = System.Windows.MessageBox.Show(
                    $"Warning: Slew CCW requires a large azimuth movement of {deltaAz:F1}° (from {currentAz:F1}° to {targetAz:F1}°).\n\n" +
                    "Do you want to proceed with this slew?",
                    "Large Azimuth Slew Confirmation",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning
                );
                if (result != System.Windows.MessageBoxResult.Yes) {
                    _vm.Log($"[Verification Slew] Large CCW jump of {deltaAz:F1}° aborted by user.");
                    return;
                }
            }

            if (target.Landmark != null) {
                _vm.SelectedLandmark = target.Landmark;
            } else {
                _vm.ActiveNodeIndex = _vm.HorizonNodes.IndexOf(target.Node);
            }
            SlewToActiveNode();
        }

        public void SlewCW() {
            var targets = new System.Collections.Generic.List<SlewTarget>();
            foreach (var node in _vm.HorizonNodes) {
                targets.Add(new SlewTarget(node));
            }
            foreach (var landmark in _vm.SyncLandmarks) {
                targets.Add(new SlewTarget(landmark));
            }

            int count = targets.Count;
            if (count == 0) return;

            // Sort targets by Azimuth
            targets.Sort((a, b) => a.Azimuth.CompareTo(b.Azimuth));

            double currentAz = _vm.CurrentAz;
            int closestIndex = -1;
            double minDistance = 360.0;

            for (int i = 0; i < count; i++) {
                double diff = Math.Abs(currentAz - targets[i].Azimuth) % 360.0;
                double dist = diff > 180.0 ? 360.0 - diff : diff;
                if (dist < minDistance) {
                    minDistance = dist;
                    closestIndex = i;
                }
            }

            int cwIndex;
            if (minDistance < 1.0) {
                cwIndex = (closestIndex + _vm.VerificationStepSize) % count;
            } else {
                cwIndex = closestIndex;
            }

            var target = targets[cwIndex];
            double targetAz = target.Azimuth;
            double diffSlew = Math.Abs(currentAz - targetAz) % 360.0;
            double deltaAz = diffSlew > 180.0 ? 360.0 - diffSlew : diffSlew;

            if (deltaAz > 45.0) {
                var result = System.Windows.MessageBox.Show(
                    $"Warning: Slew CW requires a large azimuth movement of {deltaAz:F1}° (from {currentAz:F1}° to {targetAz:F1}°).\n\n" +
                    "Do you want to proceed with this slew?",
                    "Large Azimuth Slew Confirmation",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning
                );
                if (result != System.Windows.MessageBoxResult.Yes) {
                    _vm.Log($"[Verification Slew] Large CW jump of {deltaAz:F1}° aborted by user.");
                    return;
                }
            }

            if (target.Landmark != null) {
                _vm.SelectedLandmark = target.Landmark;
            } else {
                _vm.ActiveNodeIndex = _vm.HorizonNodes.IndexOf(target.Node);
            }
            SlewToActiveNode();
        }

        private void SlewToActiveNode() {
            var node = _vm.ActiveNode;
            if (node == null) return;

            if (!_vm.IsMountConnected) {
                _vm.Log("[Error] Slewing blocked: Telescope mount is not connected.");
                return;
            }

            if (_telescopeMediator.GetInfo()?.Slewing == true) {
                _vm.Log("[Error] Slew blocked: Telescope is currently slewing.");
                return;
            }

            double targetAlt = node.Altitude;
            double targetAz = node.Azimuth;

            try {
                if (_telescopeMediator.GetInfo()?.Connected == true) {
                    _telescopeMediator.SetTrackingEnabled(false);
                    _vm.Log("Sidereal tracking automatically suspended for verification slew.");
                }
            } catch (Exception ex) {
                _vm.Log($"[Warning] Failed to auto-disable tracking: {ex.Message}");
            }

            _vm.LastRequestedAlt = targetAlt;
            _vm.LastRequestedAz = targetAz;

            _vm.IsActionSlewing = true;

            System.Threading.Tasks.Task.Run(async () => {
                try {
                    _vm.Log($"[Verification Slew] Slewing to active node - Alt: {targetAlt:F2}°, Az: {targetAz:F2}°");

                    double lat = _telescopeMediator.GetInfo()?.SiteLatitude ?? _profileService?.ActiveProfile?.AstrometrySettings?.Latitude ?? 0.0;
                    double lon = _telescopeMediator.GetInfo()?.SiteLongitude ?? _profileService?.ActiveProfile?.AstrometrySettings?.Longitude ?? 0.0;

                    var topo = new global::NINA.Astrometry.TopocentricCoordinates(
                        global::NINA.Astrometry.Angle.ByDegree(targetAz),
                        global::NINA.Astrometry.Angle.ByDegree(targetAlt),
                        global::NINA.Astrometry.Angle.ByDegree(lat),
                        global::NINA.Astrometry.Angle.ByDegree(lon)
                    );

                    DateTime startTime = DateTime.UtcNow;
                    await _telescopeMediator.SlewToCoordinatesAsync(topo, CancellationToken.None);
                    DateTime endTime = DateTime.UtcNow;

                    if (_vm.IsExactPositionEnabled) {
                        double errorAlt = _vm.CurrentAlt - targetAlt;
                        double errorAz = _vm.CurrentAz - targetAz;

                        if (errorAz > 180.0) errorAz -= 360.0;
                        if (errorAz < -180.0) errorAz += 360.0;

                        if (Math.Abs(errorAlt) > 0.01 || Math.Abs(errorAz) > 0.01) {
                            double slewSeconds = (endTime - startTime).TotalSeconds;
                            if (slewSeconds < 1.0) slewSeconds = 1.0;

                            double rateAlt = errorAlt / slewSeconds;
                            double rateAz = errorAz / slewSeconds;

                            double predictedAlt = targetAlt - (rateAlt * 8.0);
                            double predictedAz = (targetAz - (rateAz * 8.0) + 360.0) % 360.0;

                            _vm.Log($"[Exact Position] Drift error detected (Alt Error: {errorAlt:F3}°, Az Error: {errorAz:F3}°). Applied Predictive Lead. Initiating Micro-Jump...");

                            var microTopo = new global::NINA.Astrometry.TopocentricCoordinates(
                                global::NINA.Astrometry.Angle.ByDegree(predictedAz),
                                global::NINA.Astrometry.Angle.ByDegree(predictedAlt),
                                global::NINA.Astrometry.Angle.ByDegree(lat),
                                global::NINA.Astrometry.Angle.ByDegree(lon)
                            );

                            await _telescopeMediator.SlewToCoordinatesAsync(microTopo, CancellationToken.None);
                        }
                    }

                    _vm.Log("Slew completed.");
                    _telescopeMediator.SetTrackingEnabled(false);
                } catch (Exception ex) {
                    _vm.Log($"[Error] Slew to active node failed: {ex.Message}");
                } finally {
                    ThreadHelper.RunOnUI(() => {
                        _vm.IsActionSlewing = false;
                    });
                }
            });
        }

        public void RadarClickSlew(double canvasX, double canvasY) {
            if (!_vm.IsMountConnected) {
                _vm.Log("[Error] Slewing blocked: Telescope mount is not connected.");
                return;
            }

            if (_telescopeMediator.GetInfo()?.Slewing == true) {
                _vm.Log("[Error] Slew blocked: Telescope is currently slewing.");
                return;
            }

            double dx = canvasX - 250.0;
            double dy = 250.0 - canvasY;
            double r = Math.Sqrt(dx * dx + dy * dy);

            if (r > 220.0) {
                r = 220.0;
            }

            double rad = Math.Atan2(dx, dy);
            double azimuth = rad * 180.0 / Math.PI;
            azimuth = (azimuth % 360.0 + 360.0) % 360.0;
            double altitude = 90.0 - (90.0 * r / 220.0);
            if (altitude < 0.0) altitude = 0.0;
            if (altitude > 90.0) altitude = 90.0;

            SyncLandmark snappedLandmark = null;
            foreach (var landmark in _vm.SyncLandmarks) {
                double distToSpecial = AstronomyHelper.GetAngularDistance(azimuth, altitude, landmark.Azimuth, landmark.Altitude);
                if (distToSpecial < 2.5) {
                    snappedLandmark = landmark;
                    break;
                }
            }

            double targetAlt;
            double targetAz;

            if (snappedLandmark != null) {
                if (!_vm.IsSyncPreparing) {
                    _vm.SelectedLandmark = snappedLandmark;
                }
                targetAlt = snappedLandmark.Altitude;
                targetAz = snappedLandmark.Azimuth;
                _vm.Log($"[Radar Click] Snapped to Sync Landmark '{snappedLandmark.Name}' - Alt: {targetAlt:F2}°, Az: {targetAz:F2}°");
            } else {
                double clickedAlt = _vm.HorizonNodes.Count > 0 ? _vm.GetInterpolatedAltitude(azimuth) : altitude;

                if (_vm.HorizonNodes.Count > 0) {
                    double altDiff = Math.Abs(altitude - clickedAlt);
                    if (altDiff > 5.0) {
                        return;
                    }
                }

                int snappedIndex = -1;
                for (int i = 0; i < _vm.HorizonNodes.Count; i++) {
                    var node = _vm.HorizonNodes[i];
                    double dist = AstronomyHelper.GetAngularDistance(azimuth, clickedAlt, node.Azimuth, node.Altitude);
                    if (dist < 2.5) {
                        snappedIndex = i;
                        break;
                    }
                }

                if (snappedIndex != -1) {
                    if (!_vm.IsSyncPreparing) {
                        _vm.ActiveNodeIndex = snappedIndex;
                    }
                    var snappedNode = _vm.HorizonNodes[snappedIndex];
                    targetAlt = snappedNode.Altitude;
                    targetAz = snappedNode.Azimuth;
                    _vm.Log($"[Radar Click] Snapped to existing Horizon Node at index {snappedIndex} - Alt: {targetAlt:F2}°, Az: {targetAz:F2}°");
                } else {
                    if (!_vm.IsSyncPreparing) {
                        _vm.ActiveNodeIndex = -1;
                    }
                    if (_vm.HorizonNodes.Count == 0) {
                        targetAlt = _vm.CurrentAlt;
                        _vm.Log($"[Radar Click] Clicked radar with 0 nodes. Maintaining current altitude - Alt: {targetAlt:F2}°, Az: {azimuth:F2}°");
                    } else {
                        targetAlt = _vm.GetInterpolatedAltitude(azimuth);
                        _vm.Log($"[Radar Click] Clicked unmapped area. Interpolating target - Alt: {targetAlt:F2}°, Az: {azimuth:F2}°");
                    }
                    targetAz = azimuth;
                }
            }

            double currentAz = _vm.CurrentAz;
            double diffSlew = Math.Abs(currentAz - targetAz) % 360.0;
            double deltaAz = diffSlew > 180.0 ? 360.0 - diffSlew : diffSlew;

            if (deltaAz > 45.0) {
                var result = System.Windows.MessageBox.Show(
                    $"Warning: Clicked radar target requires a large azimuth movement of {deltaAz:F1}° (from {currentAz:F1}° to {targetAz:F1}°).\n\n" +
                    "Do you want to proceed with this slew?",
                    "Large Azimuth Slew Confirmation",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning
                );
                if (result != System.Windows.MessageBoxResult.Yes) {
                    _vm.Log($"[Radar Slew] Large click jump of {deltaAz:F1}° aborted by user.");
                    return;
                }
            }

            try {
                if (_telescopeMediator.GetInfo()?.Connected == true) {
                    _telescopeMediator.SetTrackingEnabled(false);
                    _vm.Log("Sidereal tracking automatically suspended for radar slew.");
                }
            } catch (Exception ex) {
                _vm.Log($"[Warning] Failed to auto-disable tracking: {ex.Message}");
            }

            _vm.LastRequestedAlt = targetAlt;
            _vm.LastRequestedAz = targetAz;
            _vm.IsActionSlewing = true;

            System.Threading.Tasks.Task.Run(async () => {
                try {
                    _vm.Log($"[Radar Slew] Slewing mount to - Alt: {targetAlt:F2}°, Az: {targetAz:F2}°");

                    double lat = _telescopeMediator.GetInfo()?.SiteLatitude ?? _profileService?.ActiveProfile?.AstrometrySettings?.Latitude ?? 0.0;
                    double lon = _telescopeMediator.GetInfo()?.SiteLongitude ?? _profileService?.ActiveProfile?.AstrometrySettings?.Longitude ?? 0.0;

                    var topo = new global::NINA.Astrometry.TopocentricCoordinates(
                        global::NINA.Astrometry.Angle.ByDegree(targetAz),
                        global::NINA.Astrometry.Angle.ByDegree(targetAlt),
                        global::NINA.Astrometry.Angle.ByDegree(lat),
                        global::NINA.Astrometry.Angle.ByDegree(lon)
                    );

                    DateTime startTime = DateTime.UtcNow;
                    await _telescopeMediator.SlewToCoordinatesAsync(topo, CancellationToken.None);
                    DateTime endTime = DateTime.UtcNow;

                    if (_vm.IsExactPositionEnabled) {
                        double errorAlt = _vm.CurrentAlt - targetAlt;
                        double errorAz = _vm.CurrentAz - targetAz;

                        if (errorAz > 180.0) errorAz -= 360.0;
                        if (errorAz < -180.0) errorAz += 360.0;

                        if (Math.Abs(errorAlt) > 0.01 || Math.Abs(errorAz) > 0.01) {
                            double slewSeconds = (endTime - startTime).TotalSeconds;
                            if (slewSeconds < 1.0) slewSeconds = 1.0;

                            double rateAlt = errorAlt / slewSeconds;
                            double rateAz = errorAz / slewSeconds;

                            double predictedAlt = targetAlt - (rateAlt * 8.0);
                            double predictedAz = (targetAz - (rateAz * 8.0) + 360.0) % 360.0;

                            _vm.Log($"[Exact Position] Drift error detected (Alt Error: {errorAlt:F3}°, Az Error: {errorAz:F3}°). Applied Predictive Lead. Initiating Micro-Jump...");

                            var microTopo = new global::NINA.Astrometry.TopocentricCoordinates(
                                global::NINA.Astrometry.Angle.ByDegree(predictedAz),
                                global::NINA.Astrometry.Angle.ByDegree(predictedAlt),
                                global::NINA.Astrometry.Angle.ByDegree(lat),
                                global::NINA.Astrometry.Angle.ByDegree(lon)
                            );

                            await _telescopeMediator.SlewToCoordinatesAsync(microTopo, CancellationToken.None);
                        }
                    }

                    _vm.Log("Slew completed.");
                    _telescopeMediator.SetTrackingEnabled(false);
                } catch (Exception ex) {
                    _vm.Log($"[Error] Radar click slew failed: {ex.Message}");
                } finally {
                    ThreadHelper.RunOnUI(() => {
                        _vm.IsActionSlewing = false;
                    });
                }
            });
        }
    }
}
