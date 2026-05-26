using System;
using System.Threading;
using System.Threading.Tasks;
using NirZonshine.NINA.HorizonStudio.Domain;

namespace NirZonshine.NINA.HorizonStudio.ViewModels.Commands {
    public partial class NavigationCommands {
        public void SlewCCW() {
            int count = _vm.HorizonNodes.Count;
            if (count == 0) return;

            double currentAz = _vm.CurrentAz;
            int currentIndex = _vm.ActiveNodeIndex;
            int ccwIndex;
            if (currentIndex == -1) {
                int closestIndex = GetClosestNodeIndex(currentAz);
                if (closestIndex != -1) {
                    double diff = Math.Abs(currentAz - _vm.HorizonNodes[closestIndex].Azimuth) % 360.0;
                    double dist = diff > 180.0 ? 360.0 - diff : diff;
                    if (dist < 1.0) {
                        ccwIndex = (closestIndex - _vm.VerificationStepSize) % count;
                        if (ccwIndex < 0) ccwIndex += count;
                    } else {
                        ccwIndex = closestIndex;
                    }
                } else {
                    ccwIndex = count - 1;
                }
            } else {
                ccwIndex = (currentIndex - _vm.VerificationStepSize) % count;
                if (ccwIndex < 0) ccwIndex += count;
            }

            var targetNode = _vm.HorizonNodes[ccwIndex];
            double targetAz = targetNode.Azimuth;
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

            _vm.ActiveNodeIndex = ccwIndex;
            SlewToActiveNode();
        }

        public void SlewCW() {
            int count = _vm.HorizonNodes.Count;
            if (count == 0) return;

            double currentAz = _vm.CurrentAz;
            int currentIndex = _vm.ActiveNodeIndex;
            int cwIndex;
            if (currentIndex == -1) {
                int closestIndex = GetClosestNodeIndex(currentAz);
                if (closestIndex != -1) {
                    double diff = Math.Abs(currentAz - _vm.HorizonNodes[closestIndex].Azimuth) % 360.0;
                    double dist = diff > 180.0 ? 360.0 - diff : diff;
                    if (dist < 1.0) {
                        cwIndex = (closestIndex + _vm.VerificationStepSize) % count;
                    } else {
                        cwIndex = closestIndex;
                    }
                } else {
                    cwIndex = 0;
                }
            } else {
                cwIndex = (currentIndex + _vm.VerificationStepSize) % count;
            }

            var targetNode = _vm.HorizonNodes[cwIndex];
            double targetAz = targetNode.Azimuth;
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

            _vm.ActiveNodeIndex = cwIndex;
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
                    System.Windows.Application.Current.Dispatcher.Invoke(() => {
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
                double distToSpecial = GetAngularDistance(azimuth, altitude, landmark.Azimuth, landmark.Altitude);
                if (distToSpecial < 2.5) {
                    snappedLandmark = landmark;
                    break;
                }
            }

            double targetAlt;
            double targetAz;

            if (snappedLandmark != null) {
                _vm.SelectedLandmark = snappedLandmark;
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
                    double dist = GetAngularDistance(azimuth, clickedAlt, node.Azimuth, node.Altitude);
                    if (dist < 2.5) {
                        snappedIndex = i;
                        break;
                    }
                }

                if (snappedIndex != -1) {
                    _vm.ActiveNodeIndex = snappedIndex;
                    var snappedNode = _vm.HorizonNodes[snappedIndex];
                    targetAlt = snappedNode.Altitude;
                    targetAz = snappedNode.Azimuth;
                    _vm.Log($"[Radar Click] Snapped to existing Horizon Node at index {snappedIndex} - Alt: {targetAlt:F2}°, Az: {targetAz:F2}°");
                } else {
                    _vm.ActiveNodeIndex = -1;
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
                    System.Windows.Application.Current.Dispatcher.Invoke(() => {
                        _vm.IsActionSlewing = false;
                    });
                }
            });
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
