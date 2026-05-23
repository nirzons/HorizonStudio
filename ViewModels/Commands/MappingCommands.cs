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
                // 1. Sort the nodes by Azimuth
                var sortedNodes = _vm.HorizonNodes.OrderBy(n => n.Azimuth).ToList();

                // 2. Unwrap if they cross the 0/360 boundary
                // We look for the largest gap in Azimuth. If it's > 180 degrees, that's likely the 0/360 boundary.
                double maxGap = 0;
                int splitIndex = -1;
                for (int i = 0; i < sortedNodes.Count - 1; i++) {
                    double gap = sortedNodes[i + 1].Azimuth - sortedNodes[i].Azimuth;
                    if (gap > maxGap) {
                        maxGap = gap;
                        splitIndex = i;
                    }
                }

                if (maxGap > 180 && splitIndex != -1) {
                    _vm.Log($"[Save] Detected boundary wrap (gap {maxGap:F1}°). Unwrapping nodes...");
                    // Add 360 to the lower azimuth values (from 0 to splitIndex)
                    for (int i = 0; i <= splitIndex; i++) {
                        sortedNodes[i] = new HorizonNode(sortedNodes[i].Azimuth + 360, sortedNodes[i].Altitude);
                    }
                    // Resort after unwrapping
                    sortedNodes = sortedNodes.OrderBy(n => n.Azimuth).ToList();
                }

                // 3. Prompt user for save location
                var dialog = new SaveFileDialog {
                    Title = "Save N.I.N.A. Horizon Profile",
                    Filter = "N.I.N.A. Horizon Files (*.hrzn)|*.hrzn|CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                    DefaultExt = ".hrzn",
                    FileName = $"CustomHorizon_{DateTime.Now:yyyyMMdd_HHmm}.hrzn"
                };

                if (dialog.ShowDialog() == true) {
                    // 4. Generate text output
                    var lines = sortedNodes.Select(n => $"{n.Azimuth:F4} {n.Altitude:F4}");
                    File.WriteAllLines(dialog.FileName, lines);

                    _vm.Log($"[Save] Successfully saved {sortedNodes.Count} nodes to {dialog.FileName}");
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
