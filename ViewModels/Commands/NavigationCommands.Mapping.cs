using System;
using System.Collections.Generic;
using System.Threading;
using NirZonshine.NINA.HorizonStudio.Domain;

namespace NirZonshine.NINA.HorizonStudio.ViewModels.Commands {
    public partial class NavigationCommands {
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

            try {
                if (_telescopeMediator.GetInfo()?.Connected == true) {
                    _telescopeMediator.SetTrackingEnabled(false);
                    _vm.Log("Sidereal tracking automatically suspended for pin drop.");
                }
            } catch (Exception ex) {
                _vm.Log($"[Warning] Failed to auto-disable tracking: {ex.Message}");
            }

            var node = new HorizonNode(az, alt);
            
            int insertIndex = 0;
            while (insertIndex < _vm.HorizonNodes.Count && _vm.HorizonNodes[insertIndex].Azimuth < node.Azimuth) {
                insertIndex++;
            }
            _vm.HorizonNodes.Insert(insertIndex, node);
            _vm.PinHistory.Push(node);

            _vm.ActiveNodeIndex = insertIndex;

            _vm.LastNodeAlt = alt;
            _vm.LastNodeAz = az;
            _vm.NodeCount = _vm.HorizonNodes.Count;
            _vm.LastNodeText = node.ToString();

            _vm.Log($"[Pin Placed] Added Horizon Node - Alt: {alt:F2}°, Az: {az:F2}° (Total: {_vm.NodeCount})");
        }

        public void UndoPin() {
            if (_vm.PinHistory.Count == 0) {
                _vm.Log("[Warning] Undo stack is empty.");
                return;
            }

            var removed = _vm.PinHistory.Pop();
            _vm.HorizonNodes.Remove(removed);
            _vm.NodeCount = _vm.HorizonNodes.Count;

            if (removed == _vm.ActiveNode) {
                _vm.ActiveNodeIndex = -1;
            } else if (_vm.ActiveNodeIndex >= _vm.HorizonNodes.Count) {
                _vm.ActiveNodeIndex = _vm.HorizonNodes.Count - 1;
            }

            if (_vm.PinHistory.Count > 0) {
                var top = _vm.PinHistory.Peek();
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
            _vm.PinHistory.Clear();
            _vm.ActiveNodeIndex = -1;
            _vm.NodeCount = 0;
            _vm.LastNodeAlt = 0.0;
            _vm.LastNodeAz = 0.0;
            _vm.LastNodeText = "None";

            _vm.Log("[Clear Pins] Removed all horizon nodes from active session.");
        }

        public void DeletePoint() {
            var activeNode = _vm.ActiveNode;
            if (activeNode == null) return;

            if (_vm.SelectedLandmark != null) {
                var name = _vm.SelectedLandmark.Name;
                _vm.SyncLandmarks.Remove(_vm.SelectedLandmark);
                _vm.SelectedLandmark = null;
                _vm.NotifyPropertyChanged("HasLandmarks");
                _vm.Landmark?.NotifyLandmarksCollectionChanged();
                _vm.Log($"[Landmarks] Removed landmark '{name}' via Active Node card deletion.");
                return;
            }

            _vm.HorizonNodes.Remove(activeNode);
            
            var list = new List<HorizonNode>(_vm.PinHistory);
            list.Remove(activeNode);
            _vm.PinHistory.Clear();
            for (int i = list.Count - 1; i >= 0; i--) {
                _vm.PinHistory.Push(list[i]);
            }

            _vm.ActiveNodeIndex = -1;
            _vm.NodeCount = _vm.HorizonNodes.Count;

            if (_vm.PinHistory.Count > 0) {
                var top = _vm.PinHistory.Peek();
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
    }
}
