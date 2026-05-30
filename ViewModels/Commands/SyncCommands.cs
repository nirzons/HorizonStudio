using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Input;
using NINA.Equipment.Interfaces.Mediator;
using NirZonshine.NINA.HorizonStudio.Domain;
using NirZonshine.NINA.HorizonStudio.Services;

namespace NirZonshine.NINA.HorizonStudio.ViewModels.Commands {
    public class SyncCommands {
        private readonly HorizonMapperDockableVM _vm;
        private readonly ITelescopeMediator _telescopeMediator;

        public ICommand PrepareSyncCommand { get; }
        public ICommand ConfirmSyncCommand { get; }
        public ICommand CancelSyncCommand { get; }
        public ICommand AutoDetectPolarOffsetCommand { get; }
        public ICommand ApplyPolarSyncCommand { get; }

        public SyncCommands(HorizonMapperDockableVM vm, ITelescopeMediator telescopeMediator) {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            _telescopeMediator = telescopeMediator ?? throw new ArgumentNullException(nameof(telescopeMediator));

            PrepareSyncCommand = new RelayCommand(o => PrepareSync(), o => _vm.CanPrepareSync);
            ConfirmSyncCommand = new RelayCommand(o => ConfirmSync(), o => _vm.CanConfirmSync);
            CancelSyncCommand = new RelayCommand(o => CancelSync(), o => _vm.IsSyncPreparing);
            AutoDetectPolarOffsetCommand = new RelayCommand(o => AutoDetectPolarOffset());
            ApplyPolarSyncCommand = new RelayCommand(o => ApplyPolarSync());
        }

        public void PrepareSync() {
            if (!_vm.HasActiveNode) {
                _vm.Log("[Error] Cannot prepare sync: No active node selected.");
                return;
            }
            if (!_vm.IsMountConnected) {
                _vm.Log("[Error] Cannot prepare sync: Mount is not connected.");
                return;
            }
            _vm.SyncRefNode = _vm.ActiveNode;
            _vm.IsSyncPreparing = true;
            if (_vm.SelectedLandmark != null) {
                _vm.Log($"[Profile Sync] Prepared sync using Landmark '{_vm.SelectedLandmark.Name}' (Alt: {_vm.SyncRefNode.Altitude:F2}°, Az: {_vm.SyncRefNode.Azimuth:F2}°) as reference. Jog the mount to center this landmark in the webcam view, then click Confirm Sync.");
            } else {
                _vm.Log($"[Profile Sync] Prepared sync using node {_vm.ActiveNodeIndex} (Alt: {_vm.SyncRefNode.Altitude:F2}°, Az: {_vm.SyncRefNode.Azimuth:F2}°) as reference. Jog the mount to center the physical landmark, then click Confirm Sync.");
            }
        }

        public void CancelSync() {
            _vm.SyncRefNode = null;
            _vm.IsSyncPreparing = false;
            _vm.Log("[Profile Sync] Profile sync cancelled.");
        }

        public void ConfirmSync() {
            if (!_vm.IsSyncPreparing || _vm.SyncRefNode == null) {
                _vm.Log("[Error] Cannot confirm sync: Sync is not prepared.");
                return;
            }
            if (!_vm.IsMountConnected) {
                _vm.Log("[Error] Cannot confirm sync: Mount is not connected.");
                return;
            }
            if (_telescopeMediator.GetInfo()?.Slewing == true || _vm.IsActionSlewing) {
                _vm.Log("[Error] Cannot confirm sync: Mount is currently slewing.");
                return;
            }

            double syncRefAz = _vm.SyncRefNode.Azimuth;
            double syncRefAlt = _vm.SyncRefNode.Altitude;
            double currentAz = _vm.CurrentAz;
            double currentAlt = _vm.CurrentAlt;

            double deltaAz = currentAz - syncRefAz;
            if (deltaAz > 180.0) deltaAz -= 360.0;
            if (deltaAz < -180.0) deltaAz += 360.0;

            double deltaAlt = currentAlt - syncRefAlt;

            double angularDistance = AstronomyHelper.GetAngularDistance(syncRefAz, syncRefAlt, currentAz, currentAlt);
            
            string message;
            string title;
            System.Windows.MessageBoxImage icon;

            if (angularDistance > 5.0) {
                message = $"🚨 CRITICAL WARNING: The calculated 3D Tilt Correction offset is {angularDistance:F2}°, which is exceptionally large and exceeds the safe limit of 5.0°!\n\n" +
                          "This usually indicates that the wrong landmark has been centered, or that the mount setup is highly misaligned. Slewing with an incorrect horizon model can lead to equipment collisions or tracking failures.\n\n" +
                          $"Reference Node Original: Alt {syncRefAlt:F2}°, Az {syncRefAz:F2}°\n" +
                          $"Mount Current Position: Alt {currentAlt:F2}°, Az {currentAz:F2}°\n" +
                          $"Offset to Apply: ΔAlt = {deltaAlt:F3}°, ΔAz = {deltaAz:F3}°\n\n" +
                          "Are you ABSOLUTELY SURE you want to force this warp profile sync?";
                title = "🚨 DANGER: LARGE TILT OFFSET DETECTED";
                icon = System.Windows.MessageBoxImage.Error;
            } else {
                message = $"Warning: This will shift and warp all {_vm.HorizonNodes.Count} points in the current profile using 3D Tilt Correction to correct for mount tilt or alignment errors.\n\n" +
                          $"Reference Node Original: Alt {syncRefAlt:F2}°, Az {syncRefAz:F2}°\n" +
                          $"Mount Current Position: Alt {currentAlt:F2}°, Az {currentAz:F2}°\n" +
                          $"Offset to Apply: ΔAlt = {deltaAlt:F3}°, ΔAz = {deltaAz:F3}°\n\n" +
                          "Are you sure you want to warp the entire profile?";
                title = "Confirm Profile 3D Tilt Correction Sync";
                icon = System.Windows.MessageBoxImage.Warning;
            }

            var result = System.Windows.MessageBox.Show(
                message,
                title,
                System.Windows.MessageBoxButton.YesNo,
                icon
            );

            if (result != System.Windows.MessageBoxResult.Yes) {
                _vm.Log("[Profile Sync] Profile sync aborted by user.");
                return;
            }

            try {
                WarpProfile(syncRefAz, syncRefAlt, currentAz, currentAlt, deltaAz, deltaAlt, _vm.SyncRefNode);
                _vm.IsSyncPreparing = false;
                _vm.SyncRefNode = null;
            } catch (Exception ex) {
                _vm.Log($"[Error] Failed to warp profile: {ex.Message}");
                global::NINA.Core.Utility.Notification.Notification.ShowError($"Failed to warp profile: {ex.Message}");
            }
        }

        public void WarpProfile(double syncRefAz, double syncRefAlt, double currentAz, double currentAlt, double deltaAz, double deltaAlt, HorizonNode refNodeToTrack) {
            if (_vm.HorizonNodes.Count == 0) {
                if (_vm.SelectedLandmark != null) {
                    _vm.SelectedLandmark.Azimuth = currentAz;
                    _vm.SelectedLandmark.Altitude = currentAlt;

                    foreach (var landmark in _vm.SyncLandmarks) {
                        if (landmark == _vm.SelectedLandmark) continue;
                        double oldAz = landmark.Azimuth;
                        double oldAlt = landmark.Altitude;
                        double newAz = (oldAz + deltaAz) % 360.0;
                        if (newAz < 0.0) newAz += 360.0;
                        double newAlt = oldAlt + (deltaAlt * Math.Cos((oldAz - syncRefAz) * Math.PI / 180.0));
                        newAlt = Math.Max(-90.0, Math.Min(90.0, newAlt));

                        landmark.Azimuth = newAz;
                        landmark.Altitude = newAlt;
                    }
                    _vm.Log($"[Profile Sync] Synced landmark '{_vm.SelectedLandmark.Name}' (No horizon points to warp).");
                }
                return;
            }

            var oldToNewMap = new Dictionary<HorizonNode, HorizonNode>();
            var warpedList = new List<HorizonNode>();

            foreach (var node in _vm.HorizonNodes) {
                double oldAz = node.Azimuth;
                double oldAlt = node.Altitude;

                double newAz = (oldAz + deltaAz) % 360.0;
                if (newAz < 0.0) newAz += 360.0;

                double newAlt = oldAlt + (deltaAlt * Math.Cos((oldAz - syncRefAz) * Math.PI / 180.0));
                newAlt = Math.Max(-90.0, Math.Min(90.0, newAlt));

                var newNode = new HorizonNode(newAz, newAlt);
                oldToNewMap[node] = newNode;
                warpedList.Add(newNode);
            }

            var sortedWarpedList = warpedList.OrderBy(n => n.Azimuth).ToList();

            var historyList = _vm.PinHistory.ToList();
            historyList.Reverse();
            _vm.PinHistory.Clear();
            foreach (var oldNode in historyList) {
                if (oldToNewMap.TryGetValue(oldNode, out var newNode)) {
                    _vm.PinHistory.Push(newNode);
                }
            }

            HorizonNode newSyncRefNode = null;
            if (_vm.SelectedLandmark != null) {
                _vm.SelectedLandmark.Azimuth = currentAz;
                _vm.SelectedLandmark.Altitude = currentAlt;
            } else if (refNodeToTrack != null) {
                oldToNewMap.TryGetValue(refNodeToTrack, out newSyncRefNode);
            }

            foreach (var landmark in _vm.SyncLandmarks) {
                if (_vm.SelectedLandmark != null && landmark == _vm.SelectedLandmark) {
                    continue;
                }
                double oldAz = landmark.Azimuth;
                double oldAlt = landmark.Altitude;
                double newAz = (oldAz + deltaAz) % 360.0;
                if (newAz < 0.0) newAz += 360.0;
                double newAlt = oldAlt + (deltaAlt * Math.Cos((oldAz - syncRefAz) * Math.PI / 180.0));
                newAlt = Math.Max(-90.0, Math.Min(90.0, newAlt));

                landmark.Azimuth = newAz;
                landmark.Altitude = newAlt;
            }

            _vm.HorizonNodes.Clear();
            foreach (var node in sortedWarpedList) {
                _vm.HorizonNodes.Add(node);
            }

            _vm.NodeCount = _vm.HorizonNodes.Count;

            if (newSyncRefNode != null) {
                _vm.ActiveNodeIndex = sortedWarpedList.IndexOf(newSyncRefNode);
            } else {
                _vm.ActiveNodeIndex = -1;
            }

            if (_vm.PinHistory.Count > 0) {
                var top = _vm.PinHistory.Peek();
                _vm.LastNodeAlt = top.Altitude;
                _vm.LastNodeAz = top.Azimuth;
                _vm.LastNodeText = top.ToString();
            }

            _vm.Log($"[Profile Sync] Profile successfully warped! Applied ΔAlt = {deltaAlt:F3}°, ΔAz = {deltaAz:F3}° across all {_vm.NodeCount} points using 3D cosine-tilt correction.");
            global::NINA.Core.Utility.Notification.Notification.ShowSuccess($"Profile successfully warped using 3D Tilt Correction!");
        }

        public void AutoDetectPolarOffset() {
            try {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                string logsDir = Path.Combine(localAppData, "NINA", "Logs");
                if (!Directory.Exists(logsDir)) {
                    _vm.Log("[Error] Polar alignment auto-detect failed: N.I.N.A. Logs directory does not exist.");
                    global::NINA.Core.Utility.Notification.Notification.ShowError("Logs directory not found.");
                    return;
                }

                var logFiles = new DirectoryInfo(logsDir)
                    .GetFiles("*.log")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .ToList();

                if (logFiles.Count == 0) {
                    _vm.Log("[Error] Polar alignment auto-detect failed: No N.I.N.A. log files found.");
                    global::NINA.Core.Utility.Notification.Notification.ShowError("No log files found.");
                    return;
                }

                // Scan fresh log files (modified in the last 6 hours)
                var decPattern = new Regex(@"Calculated Alignment Errors:.*Alt:\s*(?<alt>[-+]?\d+\.?\d*)\'\s*,\s*Az:\s*(?<az>[-+]?\d+\.?\d*)\'", RegexOptions.Compiled | RegexOptions.IgnoreCase);
                var dmsPattern = new Regex(@"Calculated Alignment Errors:.*Alt\s*(?<altSign>[-+])?(?:(?<altDeg>\d+)[°d])?\s*(?<altMin>\d+)\'\s*(?<altSec>\d+)?\""\s*,\s*Az\s*(?<azSign>[-+])?(?:(?<azDeg>\d+)[°d])?\s*(?<azMin>\d+)\'\s*(?<azSec>\d+)?\""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
                var tppaPattern = new Regex(@"Calculated alignment error\s*-\s*Altitude:\s*(?<altSign>[-+])?(?:(?<altDeg>\d+)[°d])?\s*(?<altMin>\d+)\'\s*(?<altSec>\d+)?\"".*Azimuth:\s*(?<azSign>[-+])?(?:(?<azDeg>\d+)[°d])?\s*(?<azMin>\d+)\'\s*(?<azSec>\d+)?\""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

                var candidates = new List<PolarLogMatch>();

                foreach (var logFile in logFiles) {
                    var fileAge = DateTime.UtcNow - logFile.LastWriteTimeUtc;
                    if (fileAge.TotalHours > 6) break; // log files are sorted descending by last write

                    try {
                        using (var fs = new FileStream(logFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var sr = new StreamReader(fs)) {
                            string line;
                            while ((line = sr.ReadLine()) != null) {
                                double altArcmin = 0.0;
                                double azArcmin = 0.0;
                                bool found = false;

                                var match = decPattern.Match(line);
                                if (match.Success) {
                                    altArcmin = double.Parse(match.Groups["alt"].Value);
                                    azArcmin = double.Parse(match.Groups["az"].Value);
                                    found = true;
                                } else {
                                    match = dmsPattern.Match(line);
                                    if (!match.Success) {
                                        match = tppaPattern.Match(line);
                                    }

                                    if (match.Success) {
                                        double altDeg = match.Groups["altDeg"].Success ? double.Parse(match.Groups["altDeg"].Value) : 0.0;
                                        double altMin = double.Parse(match.Groups["altMin"].Value);
                                        double altSec = match.Groups["altSec"].Success ? double.Parse(match.Groups["altSec"].Value) : 0.0;
                                        altArcmin = (altDeg * 60.0) + altMin + (altSec / 60.0);
                                        if (match.Groups["altSign"].Value == "-") altArcmin = -altArcmin;

                                        double azDeg = match.Groups["azDeg"].Success ? double.Parse(match.Groups["azDeg"].Value) : 0.0;
                                        double azMin = double.Parse(match.Groups["azMin"].Value);
                                        double azSec = match.Groups["azSec"].Success ? double.Parse(match.Groups["azSec"].Value) : 0.0;
                                        azArcmin = (azDeg * 60.0) + azMin + (azSec / 60.0);
                                        if (match.Groups["azSign"].Value == "-") azArcmin = -azArcmin;
                                        found = true;
                                    }
                                }

                                if (found) {
                                    // Parse timestamp from line
                                    DateTime lineTime = logFile.LastWriteTimeUtc; // default fallback
                                    int pipeIndex = line.IndexOf('|');
                                    if (pipeIndex > 0) {
                                        string timeStr = line.Substring(0, pipeIndex).Trim();
                                        if (DateTime.TryParse(timeStr, out DateTime dt)) {
                                            lineTime = dt.ToUniversalTime();
                                        }
                                    }

                                    var lineAge = DateTime.UtcNow - lineTime;
                                    if (lineAge.TotalHours <= 6 && lineAge.TotalHours >= -1.0) {
                                        candidates.Add(new PolarLogMatch {
                                            Timestamp = lineTime,
                                            AltOffset = altArcmin,
                                            AzOffset = azArcmin,
                                            LogFile = logFile.Name
                                        });
                                    }
                                }
                            }
                        }
                    } catch (Exception ex) {
                        _vm.Log($"[Warning] Failed to read log file {logFile.Name}: {ex.Message}");
                    }
                }

                if (candidates.Count == 0) {
                    _vm.Log("[Error] Polar alignment auto-detect failed: No fresh polar alignment errors found in the logs (last 6 hours).");
                    global::NINA.Core.Utility.Notification.Notification.ShowWarning("No polar alignment logs found in the last 6 hours.");
                    return;
                }

                // Sort chronologically
                var sortedCandidates = candidates.OrderBy(c => c.Timestamp).ToList();

                // Trace back to find the initial offset of the latest run
                int selectedIndex = sortedCandidates.Count - 1;
                for (int i = sortedCandidates.Count - 1; i > 0; i--) {
                    var gap = sortedCandidates[i].Timestamp - sortedCandidates[i - 1].Timestamp;
                    if (gap.TotalMinutes > 5.0) {
                        // Gap is more than 5 minutes, this marks the start of the latest run!
                        selectedIndex = i;
                        break;
                    }
                    if (i == 1) {
                        selectedIndex = 0;
                    }
                }

                var bestMatch = sortedCandidates[selectedIndex];
                _vm.PolarAltOffset = Math.Round(bestMatch.AltOffset / 60.0, 4);
                _vm.PolarAzOffset = Math.Round(bestMatch.AzOffset / 60.0, 4);

                _vm.Log($"[Polar Sync] Auto-detected fresh alignment from N.I.N.A. logs ({bestMatch.LogFile}). Initial Alt Error: {bestMatch.AltOffset:F2}' ({_vm.PolarAltOffset:F4}°), Az Error: {bestMatch.AzOffset:F2}' ({_vm.PolarAzOffset:F4}°) (Timestamp: {bestMatch.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss})");
                global::NINA.Core.Utility.Notification.Notification.ShowSuccess("Successfully auto-detected polar offset from log file!");

            } catch (Exception ex) {
                _vm.Log($"[Error] Failed to auto-detect polar offset: {ex.Message}");
                global::NINA.Core.Utility.Notification.Notification.ShowError($"Failed to auto-detect polar offset: {ex.Message}");
            }
        }

        public void ApplyPolarSync() {
            try {
                double lat = 0.0;
                if (_vm.IsMountConnected) {
                    lat = _telescopeMediator.GetInfo()?.SiteLatitude ?? 0.0;
                }
                if (lat == 0.0 && _vm.ProfileService?.ActiveProfile?.AstrometrySettings != null) {
                    lat = _vm.ProfileService.ActiveProfile.AstrometrySettings.Latitude;
                }

                if (lat == 0.0) {
                    _vm.Log("[Error] Cannot apply polar sync: Mount is not connected, and active profile astrometry Latitude is unset or 0.0.");
                    global::NINA.Core.Utility.Notification.Notification.ShowError("Observer Latitude is required for 3D polar alignment warping!");
                    return;
                }

                double altOffset = _vm.PolarAltOffset;
                double azOffset = _vm.PolarAzOffset;

                // Reference coordinates at Celestial Pole (Home Node)
                double syncRefAz = lat >= 0.0 ? 0.0 : 180.0;
                double syncRefAlt = lat;

                // Mount target coordinate based on offsets (negative adjustment to counteract error)
                double currentAz = syncRefAz - azOffset;
                double currentAlt = syncRefAlt - altOffset;

                double deltaAz = currentAz - syncRefAz;
                if (deltaAz > 180.0) deltaAz -= 360.0;
                if (deltaAz < -180.0) deltaAz += 360.0;

                double deltaAlt = currentAlt - syncRefAlt;

                double angularDistance = AstronomyHelper.GetAngularDistance(syncRefAz, syncRefAlt, currentAz, currentAlt);

                string message = $"This will shift and warp all {_vm.HorizonNodes.Count} points in your profile using 3D cosine-tilt correction, treating the Celestial Pole as a virtual reference node matching your polar alignment offsets.\n\n" +
                                 $"Celestial Pole: Alt {syncRefAlt:F2}°, Az {syncRefAz:F2}° ({(lat >= 0.0 ? "North" : "South")} Hemisphere)\n" +
                                 $"Polar Alignment Offsets: Alt = {altOffset:F3}° ({altOffset * 60.0:F1}'), Az = {azOffset:F3}° ({azOffset * 60.0:F1}')\n" +
                                 $"Correction to Apply: ΔAlt = {deltaAlt:F3}°, ΔAz = {deltaAz:F3}°\n\n" +
                                 "Are you sure you want to warp the entire profile?";
                
                string title = "Confirm 3D Polar Alignment Sync";
                var icon = System.Windows.MessageBoxImage.Warning;

                if (angularDistance > 5.0) {
                    message = $"🚨 CRITICAL WARNING: The calculated 3D Tilt Correction offset is {angularDistance:F2}°, which is exceptionally large and exceeds the safe limit of 5.0°!\n\n" +
                              $"Celestial Pole: Alt {syncRefAlt:F2}°, Az {syncRefAz:F2}° ({(lat >= 0.0 ? "North" : "South")} Hemisphere)\n" +
                              $"Polar Alignment Offsets: Alt = {altOffset:F3}° ({altOffset * 60.0:F1}'), Az = {azOffset:F3}° ({azOffset * 60.0:F1}')\n" +
                              $"Correction to Apply: ΔAlt = {deltaAlt:F3}°, ΔAz = {deltaAz:F3}°\n\n" +
                              "Are you ABSOLUTELY SURE you want to force this warp profile sync?";
                    title = "🚨 DANGER: LARGE TILT OFFSET DETECTED";
                    icon = System.Windows.MessageBoxImage.Error;
                }

                var result = System.Windows.MessageBox.Show(
                    message,
                    title,
                    System.Windows.MessageBoxButton.YesNo,
                    icon
                );

                if (result != System.Windows.MessageBoxResult.Yes) {
                    _vm.Log("[Polar Sync] Polar alignment sync aborted by user.");
                    return;
                }

                WarpProfile(syncRefAz, syncRefAlt, currentAz, currentAlt, deltaAz, deltaAlt, null);
                _vm.Log($"[Polar Sync] Successfully applied polar alignment tilt sync (ΔAlt = {deltaAlt:F3}°, ΔAz = {deltaAz:F3}°).");

            } catch (Exception ex) {
                _vm.Log($"[Error] Failed to apply polar alignment sync: {ex.Message}");
                global::NINA.Core.Utility.Notification.Notification.ShowError($"Failed to apply polar sync: {ex.Message}");
            }
        }

        private class PolarLogMatch {
            public DateTime Timestamp { get; set; }
            public double AltOffset { get; set; }
            public double AzOffset { get; set; }
            public string LogFile { get; set; }
        }
    }
}
