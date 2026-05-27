using System;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;
using NINA.Core.Utility;
using NirZonshine.NINA.HorizonStudio.Services;

namespace NirZonshine.NINA.HorizonStudio.ViewModels {
    public partial class HorizonMapperDockableVM {
        public string Logs {
            get => _logs;
            set { _logs = value; RaisePropertyChanged(nameof(Logs)); }
        }

        public void Log(string message) {
            var formatted = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Logger.Info($"[Horizon Studio] {message}");
            ThreadHelper.RunOnUI(() => {
                _logBuffer.Enqueue(formatted);
                if (_logBuffer.Count > MaxLogLines) {
                    _logBuffer.Dequeue();
                }
                Logs = string.Join("\n", _logBuffer);
            });
        }

        internal void SetStatus(string text, Brush color) {
            ThreadHelper.RunOnUI(() => {
                StatusIndicatorText = text;
                StatusIndicatorColor = color;
            });
        }

        private void SettingsManager_PropertyChanged(object sender, PropertyChangedEventArgs e) {
            RaisePropertyChanged(e.PropertyName);
            Camera?.RaisePropertyChanged(e.PropertyName);
            Webcam?.RaisePropertyChanged(e.PropertyName);
            Radar?.RaisePropertyChanged(e.PropertyName);
            Landmark?.RaisePropertyChanged(e.PropertyName);
        }

        private void SafetyManager_PropertyChanged(object sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(SafetyManager.IsSolarSafetyAlert) ||
                e.PropertyName == nameof(SafetyManager.IsZenithSafetyAlert) ||
                e.PropertyName == nameof(SafetyManager.SafetyMessage)) {
                RaisePropertyChanged(nameof(IsSolarSafetyAlert));
                RaisePropertyChanged(nameof(IsZenithSafetyAlert));
                RaisePropertyChanged(nameof(SafetyMessage));
            }
        }

        private void SafetyManager_SafetyLockoutTriggered(object sender, string reason) {
            Log($"[SAFETY WARNING] Emergency Lockout Triggered: {reason}. All hardware movements suspended.");
            _navigationCommands?.StopMapping();
            try {
                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() => {
                    try { Camera?.StopMainCamera(); } catch { }
                }));
            } catch { }
        }

        private void StatusTimer_Tick(object sender, EventArgs e) {
            var currentCamera = IsCameraConnected;
            var currentMount = IsMountConnected;
            var currentSlewing = IsSlewing;

            if (currentCamera != _lastIsCameraConnected) {
                _lastIsCameraConnected = currentCamera;
                RaisePropertyChanged(nameof(IsCameraConnected));
                Camera?.NotifyParentPropertiesChanged();
                CommandManager.InvalidateRequerySuggested();
            }

            if (currentMount != _lastIsMountConnected) {
                _lastIsMountConnected = currentMount;
                RaisePropertyChanged(nameof(IsMountConnected));
                Camera?.NotifyParentPropertiesChanged();
                Radar?.NotifyParentPropertiesChanged();
                Landmark?.NotifyParentPropertiesChanged();
                CommandManager.InvalidateRequerySuggested();
            }

            if (currentSlewing != _lastIsSlewing) {
                _lastIsSlewing = currentSlewing;
                RaisePropertyChanged(nameof(IsSlewing));
                Radar?.NotifyParentPropertiesChanged();
                Landmark?.NotifyParentPropertiesChanged();
                CommandManager.InvalidateRequerySuggested();
            }

            if (IsMountConnected && _currentTelescopeInfo != null) {
                CurrentAlt = _currentTelescopeInfo.Altitude;
                CurrentAz = _currentTelescopeInfo.Azimuth;
                Webcam?.UpdateRotationAngle();
            }
        }

        private void WebcamService_StateChanged(object sender, WebcamState state) {
            ThreadHelper.RunOnUI(() => {
                Webcam?.OnWebcamStateChanged(state);
            });
        }
    }
}
