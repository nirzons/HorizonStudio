using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Windows.Input;
using Microsoft.Win32;
using NINA.Equipment.Interfaces.Mediator;
using NirZonshine.NINA.HorizonVisualMapper.Domain;

namespace NirZonshine.NINA.HorizonVisualMapper.ViewModels.Commands {
    public class MappingCommands {
        private readonly HorizonMapperDockableVM _vm;
        private readonly ITelescopeMediator _telescopeMediator;

        public ICommand StartMappingCommand { get; }
        public ICommand StopMappingCommand { get; }
        public ICommand DropPinCommand { get; }
        public ICommand UndoPinCommand { get; }
        public ICommand ClearPinsCommand { get; }
        public ICommand SaveHorizonCommand { get; }

        public MappingCommands(HorizonMapperDockableVM vm, ITelescopeMediator telescopeMediator) {
            _vm = vm;
            _telescopeMediator = telescopeMediator;

            StartMappingCommand = new RelayCommand(o => StartMapping());
            StopMappingCommand = new RelayCommand(o => StopMapping());
            DropPinCommand = new RelayCommand(o => DropPin());
            UndoPinCommand = new RelayCommand(o => UndoPin());
            ClearPinsCommand = new RelayCommand(o => ClearPins());
            SaveHorizonCommand = new RelayCommand(o => SaveHorizon());
        }

        public void SaveHorizon() {
            if (_vm.HorizonNodes.Count < 3) {
                _vm.Log("[Error] Cannot save horizon: You need to drop at least 3 pins.");
                global::NINA.Core.Utility.Notification.Notification.ShowError("Need at least 3 points to save a horizon.");
                return;
            }

            try {
                // FIX #4: Work with raw (Az, Alt) value tuples throughout the sort/unwrap/interpolate
                // pipeline. NEVER reconstruct a HorizonNode during this phase — the HorizonNode
                // constructor normalizes azimuth back to [0, 360), which silently defeats the
                // 360°-boundary unwrapping done below.
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

                // FIX #5: Perform 1-degree linear interpolation across the azimuth range.
                // For every pair of adjacent nodes, fill in any missing integer-degree steps.
                // This produces a dense, smooth horizon profile that N.I.N.A. renders faithfully.
                var interpolated = new List<(double Az, double Alt)>();

                for (int i = 0; i < rawNodes.Count - 1; i++) {
                    double az1 = rawNodes[i].Az;
                    double alt1 = rawNodes[i].Alt;
                    double az2 = rawNodes[i + 1].Az;
                    double alt2 = rawNodes[i + 1].Alt;

                    // Emit the current node
                    interpolated.Add((az1, alt1));

                    // Walk every integer degree between az1 and az2
                    int startDeg = (int)Math.Ceiling(az1);
                    int endDeg = (int)Math.Floor(az2);

                    for (int deg = startDeg; deg <= endDeg; deg++) {
                        // Skip if it coincides exactly with az1 (already added) or az2 (added next iteration)
                        if (Math.Abs(deg - az1) < 1e-9 || Math.Abs(deg - az2) < 1e-9) continue;

                        double t = (deg - az1) / (az2 - az1);
                        double altInterp = alt1 + t * (alt2 - alt1);
                        interpolated.Add(((double)deg, altInterp));
                    }
                }

                // Add the final node
                if (rawNodes.Count > 0) {
                    interpolated.Add(rawNodes[rawNodes.Count - 1]);
                }

                _vm.Log($"[Save] Interpolated {rawNodes.Count} pins → {interpolated.Count} horizon points at 1° resolution.");

                // Step 3: Prompt user for save location
                var dialog = new SaveFileDialog {
                    Title = "Save N.I.N.A. Horizon Profile",
                    Filter = "N.I.N.A. Horizon Files (*.hrzn)|*.hrzn|CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                    DefaultExt = ".hrzn",
                    FileName = $"CustomHorizon_{DateTime.Now:yyyyMMdd_HHmm}.hrzn"
                };

                if (dialog.ShowDialog() == true) {
                    // Step 4: Write the file, normalizing azimuth back to [0, 360) for the output.
                    // Values unwrapped beyond 360° (e.g., 365°) fold back to 5°, which is correct
                    // since the interpolation is now complete and sequence is guaranteed by sort order.
                    var lines = interpolated.Select(n => {
                        double normalizedAz = (n.Az % 360.0 + 360.0) % 360.0;
                        return $"{normalizedAz:F4} {n.Alt:F4}";
                    });
                    File.WriteAllLines(dialog.FileName, lines);

                    _vm.Log($"[Save] Successfully saved {interpolated.Count} points to {dialog.FileName}");
                    global::NINA.Core.Utility.Notification.Notification.ShowSuccess($"Horizon profile saved successfully to {Path.GetFileName(dialog.FileName)}!");
                }
            } catch (Exception ex) {
                _vm.Log($"[Error] Failed to save horizon: {ex.Message}");
                global::NINA.Core.Utility.Notification.Notification.ShowError($"Failed to save horizon: {ex.Message}");
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
            _vm.HorizonNodes.Add(node);

            _vm.LastNodeAlt = alt;
            _vm.LastNodeAz = az;
            _vm.NodeCount = _vm.HorizonNodes.Count;
            _vm.LastNodeText = node.ToString();

            _vm.Log($"[Pin Placed] Added Horizon Node - Alt: {alt:F2}°, Az: {az:F2}° (Total: {_vm.NodeCount})");
        }

        public void UndoPin() {
            if (_vm.HorizonNodes.Count == 0) {
                _vm.Log("[Warning] Undo stack is empty.");
                return;
            }

            var removed = _vm.HorizonNodes[_vm.HorizonNodes.Count - 1];
            _vm.HorizonNodes.RemoveAt(_vm.HorizonNodes.Count - 1);
            _vm.NodeCount = _vm.HorizonNodes.Count;

            if (_vm.HorizonNodes.Count > 0) {
                var top = _vm.HorizonNodes[_vm.HorizonNodes.Count - 1];
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
            _vm.NodeCount = 0;
            _vm.LastNodeAlt = 0.0;
            _vm.LastNodeAz = 0.0;
            _vm.LastNodeText = "None";

            _vm.Log("[Clear Pins] Removed all horizon nodes from active session.");
        }
    }
}
