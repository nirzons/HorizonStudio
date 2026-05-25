using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Windows.Input;
using Microsoft.Win32;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Profile.Interfaces;
using NirZonshine.NINA.HorizonStudio.Domain;

namespace NirZonshine.NINA.HorizonStudio.ViewModels.Commands {
    public class MappingCommands {
        private readonly HorizonMapperDockableVM _vm;
        private readonly ITelescopeMediator _telescopeMediator;
        private readonly IProfileService _profileService;
        private readonly Stack<HorizonNode> _pinHistory = new Stack<HorizonNode>();

        public ICommand StartMappingCommand { get; }
        public ICommand StopMappingCommand { get; }
        public ICommand DropPinCommand { get; }
        public ICommand UndoPinCommand { get; }
        public ICommand ClearPinsCommand { get; }
        public ICommand SaveHorizonCommand { get; }
        public ICommand LoadHorizonCommand { get; }
        public ICommand DeletePointCommand { get; }

        public ICommand SlewCCWCommand { get; }
        public ICommand SlewCWCommand { get; }

        public MappingCommands(HorizonMapperDockableVM vm, ITelescopeMediator telescopeMediator, IProfileService profileService) {
            _vm = vm;
            _telescopeMediator = telescopeMediator;
            _profileService = profileService;

            StartMappingCommand = new RelayCommand(o => StartMapping());
            StopMappingCommand = new RelayCommand(o => StopMapping());
            DropPinCommand = new RelayCommand(o => DropPin());
            UndoPinCommand = new RelayCommand(o => UndoPin());
            ClearPinsCommand = new RelayCommand(o => ClearPins());
            SaveHorizonCommand = new RelayCommand(o => SaveHorizon());
            LoadHorizonCommand = new RelayCommand(o => LoadHorizon());
            DeletePointCommand = new RelayCommand(o => DeletePoint(), o => _vm.HasActiveNode);

            SlewCCWCommand = new RelayCommand(o => SlewCCW());
            SlewCWCommand = new RelayCommand(o => SlewCW());
        }

        public void SaveHorizon() {
            if (_vm.HorizonNodes.Count < 3) {
                _vm.Log("[Error] Cannot save horizon: You need to drop at least 3 pins.");
                global::NINA.Core.Utility.Notification.Notification.ShowError("Need at least 3 points to save a horizon.");
                return;
            }

            try {
                // FIX #4: Work with raw (Az, Alt) value tuples throughout the sort/unwrap
                // pipeline. NEVER reconstruct a HorizonNode during this phase — the HorizonNode
                // constructor normalizes azimuth back to [0, 360), which silently defeats the
                // 360°-boundary unwrapping done below.
                // NOTE: N.I.N.A. interpolates between pins natively, so we only write the raw
                // user-dropped nodes. No pre-interpolation is needed or desired here.
                var rawNodes = _vm.HorizonNodes
                    .Select(n => (Az: n.Azimuth, Alt: n.Altitude))
                    .OrderBy(n => n.Az)
                    .ToList();

                // Step 1: Detect and unwrap a 0°/360° boundary crossing.
                // We look for the largest azimuth gap. If it exceeds 180°, it is almost certainly
                // the wrap boundary rather than a genuine horizon gap.
                double maxGap = 0;
                int splitIndex = -1;
                for (int i = 0; i < rawNodes.Count - 1; i++) {
                    double gap = rawNodes[i + 1].Az - rawNodes[i].Az;
                    if (gap > maxGap) {
                        maxGap = gap;
                        splitIndex = i;
                    }
                }

                if (maxGap > 180 && splitIndex != -1) {
                    _vm.Log($"[Save] Detected boundary wrap (gap {maxGap:F1}°). Unwrapping nodes...");
                    // Nodes from [0..splitIndex] have low azimuth values (e.g., 5°, 10°).
                    // Add 360° to them so they sort correctly after the wrap point (e.g., 365°, 370°).
                    // We operate on raw doubles here — NOT HorizonNode — so the normalization
                    // constructor cannot undo our unwrap.
                    for (int i = 0; i <= splitIndex; i++) {
                        rawNodes[i] = (rawNodes[i].Az + 360.0, rawNodes[i].Alt);
                    }
                    // Re-sort in the unwrapped domain (values now span e.g. 310°..370°)
                    rawNodes = rawNodes.OrderBy(n => n.Az).ToList();
                }

                _vm.Log($"[Save] Writing {rawNodes.Count} pinned nodes (N.I.N.A. interpolates natively).");

                // Step 3: Prompt user for save location
                var dialog = new SaveFileDialog {
                    Title = "Save N.I.N.A. Horizon Profile",
                    Filter = "N.I.N.A. Horizon Files (*.hrzn)|*.hrzn|CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                    DefaultExt = ".hrzn",
                    FileName = $"CustomHorizon_{DateTime.Now:yyyyMMdd_HHmm}.hrzn"
                };

                if (dialog.ShowDialog() == true) {
                    // Write the file, normalizing azimuth back to [0, 360) for the output.
                    // Values unwrapped beyond 360° (e.g., 365°) fold back to 5°, which is correct
                    // since the sort order is already guaranteed by the unwrap step above.
                    var lines = rawNodes.Select(n => {
                        double normalizedAz = (n.Az % 360.0 + 360.0) % 360.0;
                        return $"{normalizedAz:F4} {n.Alt:F4}";
                    });
                    File.WriteAllLines(dialog.FileName, lines);

                    _vm.Log($"[Save] Successfully saved {rawNodes.Count} nodes to {dialog.FileName}");
                    global::NINA.Core.Utility.Notification.Notification.ShowSuccess($"Horizon profile saved successfully to {Path.GetFileName(dialog.FileName)}!");
                }
            } catch (Exception ex) {
                _vm.Log($"[Error] Failed to save horizon: {ex.Message}");
                global::NINA.Core.Utility.Notification.Notification.ShowError($"Failed to save horizon: {ex.Message}");
            }
        }

        public void LoadHorizon() {
            if (_vm.HorizonNodes.Count > 0) {
                var result = System.Windows.MessageBox.Show(
                    "Loading a new profile will clear your currently placed horizon pins. Do you want to continue?",
                    "Clear Existing Horizon Pins?",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning
                );
                if (result != System.Windows.MessageBoxResult.Yes) {
                    return;
                }
            }

            var dialog = new OpenFileDialog {
                Title = "Load N.I.N.A. Horizon Profile",
                Filter = "N.I.N.A. Horizon Files (*.hrzn)|*.hrzn|CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                DefaultExt = ".hrzn"
            };

            if (dialog.ShowDialog() == true) {
                try {
                    var lines = File.ReadAllLines(dialog.FileName);
                    var newNodes = new List<HorizonNode>();
                    int lineCount = 0;

                    foreach (var line in lines) {
                        lineCount++;
                        var trimmed = line.Trim();
                        if (string.IsNullOrWhiteSpace(trimmed)) continue;

                        var parts = trimmed.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2) {
                            if (double.TryParse(parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double az) &&
                                double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double alt)) {
                                
                                newNodes.Add(new HorizonNode(az, alt));
                            } else {
                                _vm.Log($"[Warning] Failed to parse line {lineCount} in horizon profile: '{line}'");
                            }
                        }
                    }

                    if (newNodes.Count == 0) {
                        _vm.Log("[Error] Failed to load horizon: No valid coordinates found in file.");
                        global::NINA.Core.Utility.Notification.Notification.ShowError("Failed to load horizon: No valid coordinates found in file.");
                        return;
                    }

                    ClearPins(); // Clears nodes, active index, and history

                    // NINA's .hrzn file contains items that are already sorted. 
                    // To ensure robust UI representation, sort them by azimuth just in case
                    newNodes = newNodes.OrderBy(n => n.Azimuth).ToList();

                    foreach (var node in newNodes) {
                        _vm.HorizonNodes.Add(node);
                    }
                    _vm.NodeCount = _vm.HorizonNodes.Count;

                    var top = _vm.HorizonNodes.Last();
                    _vm.LastNodeAlt = top.Altitude;
                    _vm.LastNodeAz = top.Azimuth;
                    _vm.LastNodeText = top.ToString();

                    _vm.Log($"[Load] Successfully loaded {newNodes.Count} nodes from {Path.GetFileName(dialog.FileName)}");
                    global::NINA.Core.Utility.Notification.Notification.ShowSuccess($"Horizon profile loaded: {Path.GetFileName(dialog.FileName)}!");

                } catch (Exception ex) {
                    _vm.Log($"[Error] Failed to load horizon profile: {ex.Message}");
                    global::NINA.Core.Utility.Notification.Notification.ShowError($"Failed to load horizon profile: {ex.Message}");
                }
            }
        }

        public void StartMapping() {
            if (Interlocked.CompareExchange(ref _vm.TaskExecutingFlag, 1, 0) != 0) return;

            _vm.Log("Suspending sidereal tracking and initiating Horizon Visual Mapping session...");
            _vm.IsRunning = true;
            _vm.SetStatus("Active Mapping", HorizonMapperDockableVM.StatusSuccessColor);
            _vm.LastRequestedAlt = null;
            _vm.LastRequestedAz = null;

            try {
                if (_telescopeMediator.GetInfo()?.Connected == true) {
                    _telescopeMediator.SetTrackingEnabled(false);
                    _vm.Log("Sidereal tracking disabled successfully.");
                }
            } catch (Exception ex) {
                _vm.Log($"[Warning] Failed to disable sidereal tracking: {ex.Message}");
            } finally {
                Interlocked.Exchange(ref _vm.TaskExecutingFlag, 0);
            }
        }

        public void StopMapping() {
            if (!_vm.IsRunning) return;

            _vm.Log("Stopping visual mapping session. Restoring mount tracking state...");
            _vm.IsRunning = false;
            _vm.SetStatus("Ready", HorizonMapperDockableVM.StatusIdleColor);

            try {
                if (_telescopeMediator.GetInfo()?.Connected == true) {
                    _telescopeMediator.StopSlew();
                    _vm.Log("Mount slews aborted.");
                    _telescopeMediator.SetTrackingEnabled(true);
                    _vm.Log("Sidereal tracking restored.");
                }
            } catch (Exception ex) {
                _vm.Log($"[Error] StopSlew/Tracking failed: {ex.Message}");
            }
        }

        public void DropPin() {
            if (!_vm.IsRunning) {
                _vm.Log("[Error] Cannot drop pin: Mapping session is not active. Click Start first.");
                return;
            }

            if (!_vm.IsMountConnected) {
                _vm.Log("[Error] Cannot drop pin: Mount is not connected.");
                return;
            }

            if (_telescopeMediator.GetInfo()?.Slewing == true) {
                _vm.Log("[Error] Cannot drop pin: Telescope is currently slewing.");
                global::NINA.Core.Utility.Notification.Notification.ShowError("Pin Drop Blocked: Telescope is currently slewing.");
                return;
            }

            double alt = _vm.CurrentAlt;
            double az = _vm.CurrentAz;

            var node = new HorizonNode(az, alt);
            
            // Live Sorting: Insert in Azimuth-sorted order
            int insertIndex = 0;
            while (insertIndex < _vm.HorizonNodes.Count && _vm.HorizonNodes[insertIndex].Azimuth < node.Azimuth) {
                insertIndex++;
            }
            _vm.HorizonNodes.Insert(insertIndex, node);
            _pinHistory.Push(node);

            _vm.LastNodeAlt = alt;
            _vm.LastNodeAz = az;
            _vm.NodeCount = _vm.HorizonNodes.Count;
            _vm.LastNodeText = node.ToString();

            _vm.Log($"[Pin Placed] Added Horizon Node - Alt: {alt:F2}°, Az: {az:F2}° (Total: {_vm.NodeCount})");
        }

        public void UndoPin() {
            if (_pinHistory.Count == 0) {
                _vm.Log("[Warning] Undo stack is empty.");
                return;
            }

            var removed = _pinHistory.Pop();
            _vm.HorizonNodes.Remove(removed);
            _vm.NodeCount = _vm.HorizonNodes.Count;

            if (removed == _vm.ActiveNode) {
                _vm.ActiveNodeIndex = -1;
            } else if (_vm.ActiveNodeIndex >= _vm.HorizonNodes.Count) {
                _vm.ActiveNodeIndex = _vm.HorizonNodes.Count - 1;
            }

            if (_pinHistory.Count > 0) {
                var top = _pinHistory.Peek();
                _vm.LastNodeAlt = top.Altitude;
                _vm.LastNodeAz = top.Azimuth;
                _vm.LastNodeText = top.ToString();
            } else {
                _vm.LastNodeAlt = 0.0;
                _vm.LastNodeAz = 0.0;
                _vm.LastNodeText = "None";
            }

            _vm.Log($"[Undo Pin] Removed Horizon Node - Alt: {removed.Altitude:F2}°, Az: {removed.Azimuth:F2}° (Total: {_vm.NodeCount})");
        }

        public void ClearPins() {
            if (_vm.HorizonNodes.Count == 0) return;

            _vm.HorizonNodes.Clear();
            _pinHistory.Clear();
            _vm.ActiveNodeIndex = -1;
            _vm.NodeCount = 0;
            _vm.LastNodeAlt = 0.0;
            _vm.LastNodeAz = 0.0;
            _vm.LastNodeText = "None";

            _vm.Log("[Clear Pins] Removed all horizon nodes from active session.");
        }

        private int GetClosestNodeIndex(double currentAz) {
            int count = _vm.HorizonNodes.Count;
            if (count == 0) return -1;

            int closestIndex = 0;
            double minDiff = 360.0;

            for (int i = 0; i < count; i++) {
                double diff = Math.Abs(currentAz - _vm.HorizonNodes[i].Azimuth) % 360.0;
                double shortest = diff > 180.0 ? 360.0 - diff : diff;
                if (shortest < minDiff) {
                    minDiff = shortest;
                    closestIndex = i;
                }
            }
            return closestIndex;
        }

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

            // Update VM LastRequested Alt/Az so jogging is continuous
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

                    // Exact Position Micro-Jump
                    if (_vm.IsExactPositionEnabled) {
                        double errorAlt = _vm.CurrentAlt - targetAlt;
                        double errorAz = _vm.CurrentAz - targetAz;

                        // Handle azimuth wrap-around error calculation (signed)
                        if (errorAz > 180.0) errorAz -= 360.0;
                        if (errorAz < -180.0) errorAz += 360.0;

                        if (Math.Abs(errorAlt) > 0.01 || Math.Abs(errorAz) > 0.01) {
                            double slewSeconds = (endTime - startTime).TotalSeconds;
                            if (slewSeconds < 1.0) slewSeconds = 1.0; // Prevent div/0 anomalies

                            // Calculate drift degrees per second
                            double rateAlt = errorAlt / slewSeconds;
                            double rateAz = errorAz / slewSeconds;

                            // Predict target to cancel out 8.0s of drift
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

        public void DeletePoint() {
            var activeNode = _vm.ActiveNode;
            if (activeNode == null) return;

            _vm.HorizonNodes.Remove(activeNode);
            
            // Also remove from _pinHistory if it's there
            var list = new List<HorizonNode>(_pinHistory);
            list.Remove(activeNode);
            _pinHistory.Clear();
            for (int i = list.Count - 1; i >= 0; i--) {
                _pinHistory.Push(list[i]);
            }

            _vm.ActiveNodeIndex = -1;
            _vm.NodeCount = _vm.HorizonNodes.Count;

            if (_pinHistory.Count > 0) {
                var top = _pinHistory.Peek();
                _vm.LastNodeAlt = top.Altitude;
                _vm.LastNodeAz = top.Azimuth;
                _vm.LastNodeText = top.ToString();
            } else {
                _vm.LastNodeAlt = 0.0;
                _vm.LastNodeAz = 0.0;
                _vm.LastNodeText = "None";
            }

            _vm.Log($"[Delete Point] Removed Horizon Node - Alt: {activeNode.Altitude:F2}°, Az: {activeNode.Azimuth:F2}° (Total: {_vm.NodeCount})");
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

            // Inverse Cartesian-to-polar projection math
            double dx = canvasX - 250.0;
            double dy = 250.0 - canvasY;
            double r = Math.Sqrt(dx * dx + dy * dy);

            // Radius constraint: outer boundary is 220.0
            if (r > 220.0) {
                r = 220.0;
            }

            double rad = Math.Atan2(dx, dy);
            double azimuth = rad * 180.0 / Math.PI;
            azimuth = (azimuth % 360.0 + 360.0) % 360.0;
            double altitude = 90.0 - (90.0 * r / 220.0);
            if (altitude < 0.0) altitude = 0.0;
            if (altitude > 90.0) altitude = 90.0;

            // Determine clicked altitude on the horizon line for snapping
            double clickedAlt = _vm.HorizonNodes.Count > 0 ? _vm.GetInterpolatedAltitude(azimuth) : altitude;

            // Enforce that the click must be near the horizon line (within 5.0 degrees in Altitude)
            if (_vm.HorizonNodes.Count > 0) {
                double altDiff = Math.Abs(altitude - clickedAlt);
                if (altDiff > 5.0) {
                    // Silent return to ignore clicks that are not near the horizon line
                    return;
                }
            }

            // Search for snap node within 2.5 degrees angular separation on the horizon line
            int snappedIndex = -1;
            for (int i = 0; i < _vm.HorizonNodes.Count; i++) {
                var node = _vm.HorizonNodes[i];
                double dist = GetAngularDistance(azimuth, clickedAlt, node.Azimuth, node.Altitude);
                if (dist < 2.5) {
                    snappedIndex = i;
                    break;
                }
            }

            double targetAlt;
            double targetAz;

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

            // Large Azimuth Slew Safeguard (> 45°)
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

            // Perform Slew
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

                    // Exact Position Micro-Jump
                    if (_vm.IsExactPositionEnabled) {
                        double errorAlt = _vm.CurrentAlt - targetAlt;
                        double errorAz = _vm.CurrentAz - targetAz;

                        // Handle azimuth wrap-around error calculation (signed)
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
