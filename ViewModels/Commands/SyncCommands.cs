using System;
using System.Collections.Generic;
using System.Linq;
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

        public SyncCommands(HorizonMapperDockableVM vm, ITelescopeMediator telescopeMediator) {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            _telescopeMediator = telescopeMediator ?? throw new ArgumentNullException(nameof(telescopeMediator));

            PrepareSyncCommand = new RelayCommand(o => PrepareSync(), o => _vm.CanPrepareSync);
            ConfirmSyncCommand = new RelayCommand(o => ConfirmSync(), o => _vm.CanConfirmSync);
            CancelSyncCommand = new RelayCommand(o => CancelSync(), o => _vm.IsSyncPreparing);
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
                } else {
                    oldToNewMap.TryGetValue(_vm.SyncRefNode, out newSyncRefNode);
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

                _vm.IsSyncPreparing = false;
                _vm.SyncRefNode = null;

                _vm.Log($"[Profile Sync] Profile successfully warped! Applied ΔAlt = {deltaAlt:F3}°, ΔAz = {deltaAz:F3}° across all {_vm.NodeCount} points using 3D cosine-tilt correction.");
                global::NINA.Core.Utility.Notification.Notification.ShowSuccess($"Profile successfully warped using 3D Tilt Correction!");

            } catch (Exception ex) {
                _vm.Log($"[Error] Failed to warp profile: {ex.Message}");
                global::NINA.Core.Utility.Notification.Notification.ShowError($"Failed to warp profile: {ex.Message}");
            }
        }
    }
}
