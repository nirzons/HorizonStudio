using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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

        public ICommand PrepareSyncCommand { get; }
        public ICommand ConfirmSyncCommand { get; }
        public ICommand CancelSyncCommand { get; }
        public ICommand AddLandmarkCommand { get; }
        public ICommand DeleteLandmarkCommand { get; }
        public ICommand SlewToLandmarkCommand { get; }
        public ICommand SelectLandmarkCommand { get; }
        public ICommand RenameLandmarkCommand { get; }
        public ICommand ClearAllLandmarksCommand { get; }

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

            PrepareSyncCommand = new RelayCommand(o => PrepareSync(), o => _vm.CanPrepareSync);
            ConfirmSyncCommand = new RelayCommand(o => ConfirmSync(), o => _vm.CanConfirmSync);
            CancelSyncCommand = new RelayCommand(o => CancelSync(), o => _vm.IsSyncPreparing);
            AddLandmarkCommand = new RelayCommand(o => AddLandmark(), o => _vm.IsMountConnected);
            DeleteLandmarkCommand = new RelayCommand(o => DeleteLandmark(), o => _vm.SelectedLandmark != null);
            SlewToLandmarkCommand = new RelayCommand(o => SlewToLandmark(), o => _vm.SelectedLandmark != null && _vm.IsMountConnected && !_vm.IsSlewing && !_vm.IsActionSlewing);
            SelectLandmarkCommand = new RelayCommand(o => SelectLandmark(o as SyncLandmark), o => o is SyncLandmark && !_vm.IsSyncPreparing);
            RenameLandmarkCommand = new RelayCommand(o => RenameLandmark(o as string), o => _vm.SelectedLandmark != null);
            ClearAllLandmarksCommand = new RelayCommand(o => ClearAllLandmarks(), o => _vm.HasLandmarks);
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

        public void AddLandmark() {
            if (!_vm.IsMountConnected) {
                _vm.Log("[Error] Cannot add landmark: Mount is not connected.");
                return;
            }
            double az = _vm.CurrentAz;
            double alt = _vm.CurrentAlt;

            int count = _vm.SyncLandmarks.Count + 1;
            string name = $"Landmark {count}";
            while (_vm.SyncLandmarks.Any(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase))) {
                count++;
                name = $"Landmark {count}";
            }

            var landmark = new SyncLandmark(name, az, alt);
            _vm.SyncLandmarks.Add(landmark);
            _vm.SelectedLandmark = landmark;
            _vm.NotifyPropertyChanged(nameof(_vm.HasLandmarks));
            _vm.Log($"[Landmarks] Added landmark '{name}' at Az: {az:F2}°, Alt: {alt:F2}°");
        }

        public void DeleteLandmark() {
            if (_vm.SelectedLandmark == null) return;
            var name = _vm.SelectedLandmark.Name;
            _vm.SyncLandmarks.Remove(_vm.SelectedLandmark);
            _vm.SelectedLandmark = null;
            _vm.NotifyPropertyChanged(nameof(_vm.HasLandmarks));
            _vm.Log($"[Landmarks] Removed landmark '{name}'.");
        }

        public void SlewToLandmark() {
            if (_vm.SelectedLandmark == null) return;
            if (!_vm.IsMountConnected) {
                _vm.Log("[Error] Slew blocked: Mount is not connected.");
                return;
            }
            if (_telescopeMediator.GetInfo()?.Slewing == true || _vm.IsActionSlewing) {
                _vm.Log("[Error] Slew blocked: Mount is currently slewing.");
                return;
            }

            double targetAlt = _vm.SelectedLandmark.Altitude;
            double targetAz = _vm.SelectedLandmark.Azimuth;

            try {
                if (_telescopeMediator.GetInfo()?.Connected == true) {
                    _telescopeMediator.SetTrackingEnabled(false);
                    _vm.Log("Sidereal tracking automatically suspended for landmark slew.");
                }
            } catch (Exception ex) {
                _vm.Log($"[Warning] Failed to auto-disable tracking: {ex.Message}");
            }

            _vm.LastRequestedAlt = targetAlt;
            _vm.LastRequestedAz = targetAz;
            _vm.IsActionSlewing = true;

            System.Threading.Tasks.Task.Run(async () => {
                try {
                    _vm.Log($"[Landmark Slew] Slewing mount to landmark '{_vm.SelectedLandmark?.Name}' - Alt: {targetAlt:F2}°, Az: {targetAz:F2}°");

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

                            _vm.Log($"[Exact Position] Drift error detected. Applied Predictive Lead. Initiating Micro-Jump...");

                            var microTopo = new global::NINA.Astrometry.TopocentricCoordinates(
                                global::NINA.Astrometry.Angle.ByDegree(predictedAz),
                                global::NINA.Astrometry.Angle.ByDegree(predictedAlt),
                                global::NINA.Astrometry.Angle.ByDegree(lat),
                                global::NINA.Astrometry.Angle.ByDegree(lon)
                            );

                            await _telescopeMediator.SlewToCoordinatesAsync(microTopo, CancellationToken.None);
                        }
                    }

                    _vm.Log("Slew to landmark completed.");
                    _telescopeMediator.SetTrackingEnabled(false);
                } catch (Exception ex) {
                    _vm.Log($"[Error] Slew to landmark failed: {ex.Message}");
                } finally {
                    System.Windows.Application.Current.Dispatcher.Invoke(() => {
                        _vm.IsActionSlewing = false;
                    });
                }
            });
        }

        public void SelectLandmark(SyncLandmark landmark) {
            if (landmark == null) return;
            _vm.SelectedLandmark = landmark;
            _vm.Log($"[Landmarks] Selected landmark '{landmark.Name}' - Alt: {landmark.Altitude:F2}°, Az: {landmark.Azimuth:F2}°");
        }

        public void RenameLandmark(string newName = null) {
            if (_vm.SelectedLandmark == null) return;
            string oldName = _vm.SelectedLandmark.Name;

            if (string.IsNullOrEmpty(newName)) {
                newName = RenameDialog.Show(oldName, "Rename Landmark");
            }

            if (!string.IsNullOrEmpty(newName) && !string.Equals(oldName, newName, StringComparison.Ordinal)) {
                if (_vm.SyncLandmarks.Any(l => l != _vm.SelectedLandmark && string.Equals(l.Name, newName, StringComparison.OrdinalIgnoreCase))) {
                    System.Windows.MessageBox.Show($"A landmark with the name '{newName}' already exists.", "Duplicate Name", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }
                _vm.SelectedLandmark.Name = newName;
                _vm.Log($"[Landmarks] Renamed landmark '{oldName}' to '{newName}'.");
            }
        }

        public void ClearAllLandmarks() {
            if (_vm.SyncLandmarks.Count == 0) return;
            _vm.SyncLandmarks.Clear();
            _vm.SelectedLandmark = null;
            _vm.NotifyPropertyChanged(nameof(_vm.HasLandmarks));
            _vm.Log("[Landmarks] Cleared all landmarks.");
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

            var result = System.Windows.MessageBox.Show(
                $"Warning: This will shift and warp all {_vm.HorizonNodes.Count} points in the current profile using 3D Tilt Correction to correct for mount tilt or alignment errors.\n\n" +
                $"Reference Node Original: Alt {syncRefAlt:F2}°, Az {syncRefAz:F2}°\n" +
                $"Mount Current Position: Alt {currentAlt:F2}°, Az {currentAz:F2}°\n" +
                $"Offset to Apply: ΔAlt = {deltaAlt:F3}°, ΔAz = {deltaAz:F3}°\n\n" +
                "Are you sure you want to warp the entire profile?",
                "Confirm Profile 3D Tilt Correction Sync",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning
            );

            if (result != System.Windows.MessageBoxResult.Yes) {
                _vm.Log("[Profile Sync] Profile sync aborted by user.");
                return;
            }

            try {
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
                    _vm.IsSyncPreparing = false;
                    _vm.SyncRefNode = null;
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

                var historyList = _pinHistory.ToList();
                historyList.Reverse();
                _pinHistory.Clear();
                foreach (var oldNode in historyList) {
                    if (oldToNewMap.TryGetValue(oldNode, out var newNode)) {
                        _pinHistory.Push(newNode);
                    }
                }

                HorizonNode newSyncRefNode = null;
                if (_vm.SelectedLandmark != null) {
                    _vm.SelectedLandmark.Azimuth = currentAz;
                    _vm.SelectedLandmark.Altitude = currentAlt;
                } else {
                    oldToNewMap.TryGetValue(_vm.SyncRefNode, out newSyncRefNode);
                }

                // Apply 3D cosine-tilt warp to all other landmarks
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

                if (_pinHistory.Count > 0) {
                    var top = _pinHistory.Peek();
                    _vm.LastNodeAlt = top.Altitude;
                    _vm.LastNodeAz = top.Azimuth;
                    _vm.LastNodeText = top.ToString();
                }

                _vm.IsSyncPreparing = false;
                _vm.SyncRefNode = null;

                _vm.Log($"[Profile Sync] Profile successfully warped! Applied ΔAlt = {deltaAlt:F3}°, ΔAz = {deltaAz:F3}° across all {_vm.NodeCount} points using 3D cosine-tilt correction.");
                global::NINA.Core.Utility.Notification.Notification.ShowSuccess($"Profile successfully warped using 3D Tilt Correction!");

            } catch (Exception ex) {
                _vm.Log($"[Error] Failed to warp profile: {ex.Message}");
                global::NINA.Core.Utility.Notification.Notification.ShowError($"Failed to warp profile: {ex.Message}");
            }
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
                string suggestedName = $"CustomHorizon_{DateTime.Now:yyyyMMdd_HHmm}.hrz";

                var dialog = new SaveFileDialog {
                    Title = "Save N.I.N.A. Horizon Profile",
                    Filter = "N.I.N.A. Horizon Files (*.hrz)|*.hrz|Legacy Horizon Files (*.hrzn)|*.hrzn|CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                    DefaultExt = ".hrz",
                    FileName = suggestedName
                };

                if (dialog.ShowDialog() == true) {
                    // Build file content with optional metadata comment header.
                    // N.I.N.A.'s native horizon parser skips lines starting with '#',
                    // so we can safely embed our landmark coordinates as a comment.
                    var fileLines = new List<string>();

                    foreach (var landmark in _vm.SyncLandmarks) {
                        fileLines.Add($"# HorizonStudio_Landmark: Id={landmark.Id};Name={landmark.Name};Az={landmark.Azimuth.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)};Alt={landmark.Altitude.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}");
                    }
                    fileLines.Add("# Az Alt");

                    // Write coordinate lines, normalizing azimuth back to [0, 360) for the output.
                    // Values unwrapped beyond 360° (e.g., 365°) fold back to 5°, which is correct
                    // since the sort order is already guaranteed by the unwrap step above.
                    foreach (var n in rawNodes) {
                        double normalizedAz = (n.Az % 360.0 + 360.0) % 360.0;
                        fileLines.Add($"{normalizedAz:F4} {n.Alt:F4}");
                    }
                    File.WriteAllLines(dialog.FileName, fileLines);

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
                Filter = "N.I.N.A. Horizon Files (*.hrz)|*.hrz|Legacy Horizon Files (*.hrzn)|*.hrzn|CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                DefaultExt = ".hrz"
            };

            if (dialog.ShowDialog() == true) {
                try {
                    var lines = File.ReadAllLines(dialog.FileName);
                    var newNodes = new List<HorizonNode>();
                    int lineCount = 0;

                    // Parse landmark metadata from comment header (if present).
                    // Also supports legacy filename-based encoding for backwards compatibility.
                    _vm.SyncLandmarks.Clear();
                    _vm.SelectedLandmark = null;
                    foreach (var line in lines) {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("# HorizonStudio_Landmark:")) {
                            var idMatch = Regex.Match(trimmed, @"Id=(?<id>[^;]+)");
                            var nameMatch = Regex.Match(trimmed, @"Name=(?<name>[^;]+)");
                            var azMatch = Regex.Match(trimmed, @"Az=(?<az>[\d.-]+)");
                            var altMatch = Regex.Match(trimmed, @"Alt=(?<alt>[\d.-]+)");

                            if (azMatch.Success && altMatch.Success &&
                                double.TryParse(azMatch.Groups["az"].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedAz) &&
                                double.TryParse(altMatch.Groups["alt"].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedAlt)) {
                                
                                string id = idMatch.Success ? idMatch.Groups["id"].Value : Guid.NewGuid().ToString();
                                string name = nameMatch.Success ? nameMatch.Groups["name"].Value : $"Landmark {_vm.SyncLandmarks.Count + 1}";
                                var landmark = new SyncLandmark(id, name, parsedAz, parsedAlt);
                                _vm.SyncLandmarks.Add(landmark);
                            }
                        } else if (trimmed.StartsWith("# HorizonStudio_Metadata:")) {
                            var metaMatch = Regex.Match(trimmed, @"LandmarkAz=(?<az>[\d.-]+).*LandmarkAlt=(?<alt>[-]?[\d.-]+)");
                            if (metaMatch.Success &&
                                double.TryParse(metaMatch.Groups["az"].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedAz) &&
                                double.TryParse(metaMatch.Groups["alt"].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedAlt)) {
                                
                                var landmark = new SyncLandmark(Guid.NewGuid().ToString(), "Landmark 1", parsedAz, parsedAlt);
                                _vm.SyncLandmarks.Add(landmark);
                                _vm.Log($"[Load] Extracted legacy single landmark from file metadata: Az {parsedAz:F2}°, Alt {parsedAlt:F2}°");
                            }
                        }
                        if (!string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith("#")) break;
                    }

                    if (_vm.SyncLandmarks.Count == 0) {
                        string fileName = Path.GetFileName(dialog.FileName);
                        var match = Regex.Match(fileName, @"_sync_Az(?<az>\d+(\.\d+)?)(_Alt|-Alt)(?<alt>[-]?\d+(\.\d+)?)", RegexOptions.IgnoreCase);
                        if (!match.Success) {
                            match = Regex.Match(fileName, @"_sync_(?<az>\d+(\.\d+)?)(_|-)(?<alt>[-]?\d+(\.\d+)?)", RegexOptions.IgnoreCase);
                        }
                        if (match.Success &&
                            double.TryParse(match.Groups["az"].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double legacyAz) &&
                            double.TryParse(match.Groups["alt"].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double legacyAlt)) {
                            
                            var landmark = new SyncLandmark(Guid.NewGuid().ToString(), "Landmark 1", legacyAz, legacyAlt);
                            _vm.SyncLandmarks.Add(landmark);
                            _vm.Log($"[Load] Extracted legacy special sync landmark from legacy filename: Az {legacyAz:F2}°, Alt {legacyAlt:F2}°");
                        }
                    }

                    if (_vm.SyncLandmarks.Count > 0) {
                        _vm.SelectedLandmark = _vm.SyncLandmarks[0];
                        _vm.NotifyPropertyChanged(nameof(_vm.HasLandmarks));
                    }

                    // Parse coordinate lines (skip comments and blank lines)
                    foreach (var line in lines) {
                        lineCount++;
                        var trimmed = line.Trim();
                        if (string.IsNullOrWhiteSpace(trimmed)) continue;
                        if (trimmed.StartsWith("#")) continue; // Skip comment lines

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

            // Auto-suspend tracking on action trigger
            try {
                if (_telescopeMediator.GetInfo()?.Connected == true) {
                    _telescopeMediator.SetTrackingEnabled(false);
                    _vm.Log("Sidereal tracking automatically suspended for pin drop.");
                }
            } catch (Exception ex) {
                _vm.Log($"[Warning] Failed to auto-disable tracking: {ex.Message}");
            }

            var node = new HorizonNode(az, alt);
            
            // Live Sorting: Insert in Azimuth-sorted order
            int insertIndex = 0;
            while (insertIndex < _vm.HorizonNodes.Count && _vm.HorizonNodes[insertIndex].Azimuth < node.Azimuth) {
                insertIndex++;
            }
            _vm.HorizonNodes.Insert(insertIndex, node);
            _pinHistory.Push(node);

            // Auto-select the newly dropped pin
            _vm.ActiveNodeIndex = insertIndex;

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

            // Auto-suspend tracking on action trigger
            try {
                if (_telescopeMediator.GetInfo()?.Connected == true) {
                    _telescopeMediator.SetTrackingEnabled(false);
                    _vm.Log("Sidereal tracking automatically suspended for verification slew.");
                }
            } catch (Exception ex) {
                _vm.Log($"[Warning] Failed to auto-disable tracking: {ex.Message}");
            }

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

            if (_vm.SelectedLandmark != null) {
                var name = _vm.SelectedLandmark.Name;
                _vm.SyncLandmarks.Remove(_vm.SelectedLandmark);
                _vm.SelectedLandmark = null;
                _vm.NotifyPropertyChanged(nameof(_vm.HasLandmarks));
                _vm.Log($"[Landmarks] Removed landmark '{name}' via Active Node card deletion.");
                return;
            }

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

            // Auto-suspend tracking on action trigger
            try {
                if (_telescopeMediator.GetInfo()?.Connected == true) {
                    _telescopeMediator.SetTrackingEnabled(false);
                    _vm.Log("Sidereal tracking automatically suspended for radar slew.");
                }
            } catch (Exception ex) {
                _vm.Log($"[Warning] Failed to auto-disable tracking: {ex.Message}");
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

    public static class RenameDialog {
        public static string Show(string defaultText, string title) {
            var dialog = new System.Windows.Window {
                Title = title,
                Width = 320,
                SizeToContent = System.Windows.SizeToContent.Height,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                Owner = System.Windows.Application.Current.MainWindow,
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0f0f12")),
                BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2A2A30")),
                BorderThickness = new System.Windows.Thickness(1),
                ResizeMode = System.Windows.ResizeMode.NoResize,
                WindowStyle = System.Windows.WindowStyle.ToolWindow
            };

            var stack = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(15) };
            
            var label = new System.Windows.Controls.TextBlock {
                Text = "Enter landmark name:",
                Foreground = System.Windows.Media.Brushes.DarkGray,
                FontSize = 11,
                Margin = new System.Windows.Thickness(0, 0, 0, 8)
            };
            stack.Children.Add(label);

            var textBox = new System.Windows.Controls.TextBox {
                Text = defaultText,
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E1E24")),
                BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3D3D45")),
                Foreground = System.Windows.Media.Brushes.White,
                Padding = new System.Windows.Thickness(4, 2, 4, 2),
                CaretBrush = System.Windows.Media.Brushes.White,
                Margin = new System.Windows.Thickness(0, 0, 0, 12)
            };
            textBox.SelectAll();
            stack.Children.Add(textBox);

            var buttonsGrid = new System.Windows.Controls.Grid();
            buttonsGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            buttonsGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });

            var okButton = new System.Windows.Controls.Button {
                Content = "OK",
                IsDefault = true,
                Height = 24,
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8B5CF6")),
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new System.Windows.Thickness(0, 0, 4, 0)
            };
            okButton.Click += (s, e) => { dialog.DialogResult = true; dialog.Close(); };
            System.Windows.Controls.Grid.SetColumn(okButton, 0);
            buttonsGrid.Children.Add(okButton);

            var cancelButton = new System.Windows.Controls.Button {
                Content = "Cancel",
                IsCancel = true,
                Height = 24,
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#ef4444")),
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new System.Windows.Thickness(4, 0, 0, 0)
            };
            cancelButton.Click += (s, e) => { dialog.DialogResult = false; dialog.Close(); };
            System.Windows.Controls.Grid.SetColumn(cancelButton, 1);
            buttonsGrid.Children.Add(cancelButton);

            stack.Children.Add(buttonsGrid);
            dialog.Content = stack;

            dialog.Loaded += (s, e) => textBox.Focus();

            if (dialog.ShowDialog() == true) {
                return textBox.Text.Trim();
            }
            return null;
        }
    }
}
