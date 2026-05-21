using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Input;
using NINA.Equipment.Interfaces.Mediator;
using NirZonshine.NINA.HorizonVisualMapper.Domain;

namespace NirZonshine.NINA.HorizonVisualMapper.ViewModels.Commands {
    public class MappingCommands {
        private readonly HorizonMapperDockableVM _vm;
        private readonly ITelescopeMediator _telescopeMediator;
        private readonly Stack<HorizonNode> _undoStack = new Stack<HorizonNode>();

        public ICommand StartMappingCommand { get; }
        public ICommand StopMappingCommand { get; }
        public ICommand DropPinCommand { get; }
        public ICommand UndoPinCommand { get; }
        public ICommand ClearPinsCommand { get; }

        public MappingCommands(HorizonMapperDockableVM vm, ITelescopeMediator telescopeMediator) {
            _vm = vm;
            _telescopeMediator = telescopeMediator;

            StartMappingCommand = new RelayCommand(o => StartMapping());
            StopMappingCommand = new RelayCommand(o => StopMapping());
            DropPinCommand = new RelayCommand(o => DropPin());
            UndoPinCommand = new RelayCommand(o => UndoPin());
            ClearPinsCommand = new RelayCommand(o => ClearPins());
        }

        public void StartMapping() {
            if (Interlocked.CompareExchange(ref _vm.TaskExecutingFlag, 1, 0) != 0) return;

            _vm.Log("Suspending sidereal tracking and initiating Horizon Visual Mapping session...");
            _vm.IsRunning = true;
            _vm.SetStatus("Active Mapping", HorizonMapperDockableVM.StatusSuccessColor);

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
                }
            } catch (Exception ex) {
                _vm.Log($"[Error] StopSlew failed: {ex.Message}");
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

            double alt = _vm.CurrentAlt;
            double az = _vm.CurrentAz;

            var node = new HorizonNode(az, alt);
            _undoStack.Push(node);

            _vm.LastNodeAlt = alt;
            _vm.LastNodeAz = az;
            _vm.NodeCount = _undoStack.Count;
            _vm.LastNodeText = node.ToString();

            _vm.Log($"[Pin Placed] Added Horizon Node - Alt: {alt:F2}°, Az: {az:F2}° (Total: {_vm.NodeCount})");
        }

        public void UndoPin() {
            if (_undoStack.Count == 0) {
                _vm.Log("[Warning] Undo stack is empty.");
                return;
            }

            var removed = _undoStack.Pop();
            _vm.NodeCount = _undoStack.Count;

            if (_undoStack.Count > 0) {
                var top = _undoStack.Peek();
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
            if (_undoStack.Count == 0) return;

            _undoStack.Clear();
            _vm.NodeCount = 0;
            _vm.LastNodeAlt = 0.0;
            _vm.LastNodeAz = 0.0;
            _vm.LastNodeText = "None";

            _vm.Log("[Clear Pins] Removed all horizon nodes from active session.");
        }
    }
}
