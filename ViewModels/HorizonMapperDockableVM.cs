using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using NINA.WPF.Base.ViewModel;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyTelescope;
using NirZonshine.NINA.HorizonVisualMapper.Domain;
using NirZonshine.NINA.HorizonVisualMapper.Services;

namespace NirZonshine.NINA.HorizonVisualMapper.ViewModels {

    [Export(typeof(global::NINA.Equipment.Interfaces.ViewModel.IDockableVM))]
    public class HorizonMapperDockableVM : DockableVM, ICameraConsumer, ITelescopeConsumer {
        private readonly IProfileService _profileService;
        private readonly ICameraMediator _cameraMediator;
        private readonly ITelescopeMediator _telescopeMediator;
        private readonly IImagingMediator _imagingMediator;
        private readonly System.Windows.Threading.DispatcherTimer _statusTimer;

        private readonly SettingsManager _settingsManager;
        private readonly SafetyManager _safetyManager;

        private ImageSource _lastFrame;
        private string _logs = "[System] Welcome to Horizon Visual Mapper. Select camera to begin.";
        private bool _isRunning = false;
        private int _taskExecutingFlag = 0;

        private double _currentAlt;
        private double _currentAz;
        private double _lastNodeAlt;
        private double _lastNodeAz;
        private int _nodeCount = 0;
        private string _lastNodeText = "None";

        private string _statusIndicatorText = "Ready";
        private Brush _statusIndicatorColor = StatusIdleColor;

        private CameraInfo _currentCameraInfo;
        private TelescopeInfo _currentTelescopeInfo;
        private bool _lastIsCameraConnected;
        private bool _lastIsMountConnected;

        private static readonly Brush StatusIdleColor = CreateFrozenBrush("#72BDFF");
        private static readonly Brush StatusWarningColor = CreateFrozenBrush("#FBBF24");
        private static readonly Brush StatusSuccessColor = CreateFrozenBrush("#22C55E");
        private static readonly Brush StatusFailureColor = CreateFrozenBrush("#EF4444");
        private static readonly Brush StatusProgressColor = CreateFrozenBrush("#6366F1");

        private readonly Stack<HorizonNode> _undoStack = new Stack<HorizonNode>();

        [ImportingConstructor]
        public HorizonMapperDockableVM(IProfileService profileService, ICameraMediator cameraMediator, ITelescopeMediator telescopeMediator, IImagingMediator imagingMediator) : base(profileService) {
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            _cameraMediator = cameraMediator ?? throw new ArgumentNullException(nameof(cameraMediator));
            _telescopeMediator = telescopeMediator ?? throw new ArgumentNullException(nameof(telescopeMediator));
            _imagingMediator = imagingMediator ?? throw new ArgumentNullException(nameof(imagingMediator));

            _cameraMediator.RegisterConsumer(this);
            _telescopeMediator.RegisterConsumer(this);

            Title = "Horizon Visual Mapper";

            _settingsManager = new SettingsManager(_profileService);
            _settingsManager.PropertyChanged += SettingsManager_PropertyChanged;

            _safetyManager = new SafetyManager(_profileService, _telescopeMediator, _settingsManager);
            _safetyManager.PropertyChanged += SafetyManager_PropertyChanged;
            _safetyManager.SafetyLockoutTriggered += SafetyManager_SafetyLockoutTriggered;

            _lastIsCameraConnected = IsCameraConnected;
            _lastIsMountConnected = IsMountConnected;

            // Heartbeat status poll (1 second interval)
            _statusTimer = new System.Windows.Threading.DispatcherTimer {
                Interval = TimeSpan.FromSeconds(1)
            };
            _statusTimer.Tick += StatusTimer_Tick;
            _statusTimer.Start();

            // Define HVM Icon: Curve representing a horizon dome, a mount, and obstruction tree
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M2,14 C2,6 6,2 14,2")); // Obstruction line
            group.Children.Add(Geometry.Parse("M14,14 L12,11 L10,14 Z"));  // Mountain landmark
            group.Children.Add(Geometry.Parse("M6,14 C6,10 8,8 10,14")); // Tree profile
            group.Freeze();
            ImageGeometry = group;
        }

        private void SettingsManager_PropertyChanged(object sender, PropertyChangedEventArgs e) {
            RaisePropertyChanged(e.PropertyName);
            if (e.PropertyName == nameof(FocalLengthOverride) || e.PropertyName == nameof(SafetyThreshold)) {
                RaisePropertyChanged(nameof(CanStart));
            }
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
            StopMapping();
        }

        private void StatusTimer_Tick(object sender, EventArgs e) {
            var currentCamera = IsCameraConnected;
            var currentMount = IsMountConnected;

            if (currentCamera != _lastIsCameraConnected) {
                _lastIsCameraConnected = currentCamera;
                RaisePropertyChanged(nameof(IsCameraConnected));
                RaisePropertyChanged(nameof(CanStart));
            }

            if (currentMount != _lastIsMountConnected) {
                _lastIsMountConnected = currentMount;
                RaisePropertyChanged(nameof(IsMountConnected));
                RaisePropertyChanged(nameof(CanStart));
            }

            if (IsMountConnected && _currentTelescopeInfo != null) {
                CurrentAlt = _currentTelescopeInfo.Altitude;
                CurrentAz = _currentTelescopeInfo.Azimuth;
            }
        }

        public void UpdateDeviceInfo(CameraInfo deviceInfo) {
            _currentCameraInfo = deviceInfo;
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
                RaisePropertyChanged(nameof(IsCameraConnected));
                RaisePropertyChanged(nameof(CanStart));
            });
        }

        public void UpdateDeviceInfo(TelescopeInfo deviceInfo) {
            _currentTelescopeInfo = deviceInfo;
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
                RaisePropertyChanged(nameof(IsMountConnected));
                RaisePropertyChanged(nameof(CanStart));
                if (IsMountConnected && deviceInfo != null) {
                    CurrentAlt = deviceInfo.Altitude;
                    CurrentAz = deviceInfo.Azimuth;
                }
            });
        }

        public override bool IsTool => true;

        public bool IsCameraConnected => (_currentCameraInfo?.Connected ?? _cameraMediator?.GetInfo()?.Connected ?? false);
        public bool IsMountConnected => (_currentTelescopeInfo?.Connected ?? _telescopeMediator?.GetInfo()?.Connected ?? false);

        public bool CanStart => IsMountConnected && !IsRunning && Interlocked.CompareExchange(ref _taskExecutingFlag, 0, 0) == 0;

        public double ExposureTime {
            get => _settingsManager.ExposureTime;
            set => _settingsManager.ExposureTime = value;
        }

        public int Gain {
            get => _settingsManager.Gain;
            set => _settingsManager.Gain = value;
        }

        public string Binning {
            get => _settingsManager.Binning;
            set => _settingsManager.Binning = value;
        }

        public double FocalLengthOverride {
            get => _settingsManager.FocalLengthOverride;
            set => _settingsManager.FocalLengthOverride = value;
        }

        public double SafetyThreshold {
            get => _settingsManager.SafetyThreshold;
            set => _settingsManager.SafetyThreshold = value;
        }

        public double StepSizeManual {
            get => _settingsManager.StepSizeManual;
            set => _settingsManager.StepSizeManual = value;
        }

        public bool EnableSolarSafety {
            get => _settingsManager.EnableSolarSafety;
            set => _settingsManager.EnableSolarSafety = value;
        }

        public bool EnableZenithSafety {
            get => _settingsManager.EnableZenithSafety;
            set => _settingsManager.EnableZenithSafety = value;
        }

        public double BacklashCompensationAmount {
            get => _settingsManager.BacklashCompensationAmount;
            set => _settingsManager.BacklashCompensationAmount = value;
        }

        public bool HorizonLockEnabled {
            get => _settingsManager.HorizonLockEnabled;
            set => _settingsManager.HorizonLockEnabled = value;
        }

        public bool IsSolarSafetyAlert => _safetyManager.IsSolarSafetyAlert;
        public bool IsZenithSafetyAlert => _safetyManager.IsZenithSafetyAlert;
        public string SafetyMessage => _safetyManager.SafetyMessage;

        public ImageSource LastFrame {
            get => _lastFrame;
            set {
                _lastFrame = value;
                RaisePropertyChanged(nameof(LastFrame));
            }
        }

        public string Logs {
            get => _logs;
            set {
                _logs = value;
                RaisePropertyChanged(nameof(Logs));
            }
        }

        public bool IsRunning {
            get => _isRunning;
            set {
                _isRunning = value;
                RaisePropertyChanged(nameof(IsRunning));
                RaisePropertyChanged(nameof(CanStart));
            }
        }

        public double CurrentAlt {
            get => _currentAlt;
            set { _currentAlt = value; RaisePropertyChanged(nameof(CurrentAlt)); }
        }

        public double CurrentAz {
            get => _currentAz;
            set { _currentAz = value; RaisePropertyChanged(nameof(CurrentAz)); }
        }

        public double LastNodeAlt {
            get => _lastNodeAlt;
            set { _lastNodeAlt = value; RaisePropertyChanged(nameof(LastNodeAlt)); }
        }

        public double LastNodeAz {
            get => _lastNodeAz;
            set { _lastNodeAz = value; RaisePropertyChanged(nameof(LastNodeAz)); }
        }

        public int NodeCount {
            get => _nodeCount;
            set { _nodeCount = value; RaisePropertyChanged(nameof(NodeCount)); }
        }

        public string LastNodeText {
            get => _lastNodeText;
            set { _lastNodeText = value; RaisePropertyChanged(nameof(LastNodeText)); }
        }

        public string StatusIndicatorText {
            get => _statusIndicatorText;
            set { _statusIndicatorText = value; RaisePropertyChanged(nameof(StatusIndicatorText)); }
        }

        public Brush StatusIndicatorColor {
            get => _statusIndicatorColor;
            set { _statusIndicatorColor = value; RaisePropertyChanged(nameof(StatusIndicatorColor)); }
        }

        private void SetStatus(string text, Brush color) {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => {
                StatusIndicatorText = text;
                StatusIndicatorColor = color;
            }));
        }

        public void Log(string message) {
            var formatted = $"\n[{DateTime.Now:HH:mm:ss}] {message}";
            Logger.Info($"[Horizon Visual Mapper] {message}");
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => {
                Logs += formatted;
            }));
        }

        // --- Commands ---

        private ICommand _startMappingCommand;
        public ICommand StartMappingCommand => _startMappingCommand ??= new RelayCommand(o => StartMapping());

        private ICommand _stopMappingCommand;
        public ICommand StopMappingCommand => _stopMappingCommand ??= new RelayCommand(o => StopMapping());

        private ICommand _dropPinCommand;
        public ICommand DropPinCommand => _dropPinCommand ??= new RelayCommand(o => DropPin());

        private ICommand _undoPinCommand;
        public ICommand UndoPinCommand => _undoPinCommand ??= new RelayCommand(o => UndoPin());

        private ICommand _clearPinsCommand;
        public ICommand ClearPinsCommand => _clearPinsCommand ??= new RelayCommand(o => ClearPins());

        private ICommand _jogNorthCommand;
        public ICommand JogNorthCommand => _jogNorthCommand ??= new RelayCommand(o => SlewJog(StepSizeManual, 0));

        private ICommand _jogSouthCommand;
        public ICommand JogSouthCommand => _jogSouthCommand ??= new RelayCommand(o => SlewJog(-StepSizeManual, 0));

        private ICommand _jogEastCommand;
        public ICommand JogEastCommand => _jogEastCommand ??= new RelayCommand(o => SlewJog(0, StepSizeManual));

        private ICommand _jogWestCommand;
        public ICommand JogWestCommand => _jogWestCommand ??= new RelayCommand(o => SlewJog(0, -StepSizeManual));

        public void StartMapping() {
            if (Interlocked.CompareExchange(ref _taskExecutingFlag, 1, 0) != 0) return;

            Log("Suspending sidereal tracking and initiating Horizon Visual Mapping session...");
            IsRunning = true;
            SetStatus("Active Mapping", StatusSuccessColor);

            try {
                if (_telescopeMediator.GetInfo()?.Connected == true) {
                    _telescopeMediator.SetTrackingEnabled(false);
                    Log("Sidereal tracking disabled successfully.");
                }
            } catch (Exception ex) {
                Log($"[Warning] Failed to disable sidereal tracking: {ex.Message}");
            } finally {
                Interlocked.Exchange(ref _taskExecutingFlag, 0);
            }
        }

        public void StopMapping() {
            if (!IsRunning) return;

            Log("Stopping visual mapping session. Restoring mount tracking state...");
            IsRunning = false;
            SetStatus("Ready", StatusIdleColor);

            try {
                if (_telescopeMediator.GetInfo()?.Connected == true) {
                    _telescopeMediator.StopSlew();
                    Log("Mount slews aborted.");
                }
            } catch (Exception ex) {
                Log($"[Error] StopSlew failed: {ex.Message}");
            }
        }

        public void DropPin() {
            if (!IsRunning) {
                Log("[Error] Cannot drop pin: Mapping session is not active. Click Start first.");
                return;
            }

            if (!IsMountConnected) {
                Log("[Error] Cannot drop pin: Mount is not connected.");
                return;
            }

            double alt = _telescopeMediator.GetInfo()?.Altitude ?? 0.0;
            double az = _telescopeMediator.GetInfo()?.Azimuth ?? 0.0;

            var node = new HorizonNode(az, alt);
            _undoStack.Push(node);

            LastNodeAlt = alt;
            LastNodeAz = az;
            NodeCount = _undoStack.Count;
            LastNodeText = node.ToString();

            Log($"[Pin Placed] Added Horizon Node - Alt: {alt:F2}°, Az: {az:F2}° (Total: {NodeCount})");
        }

        public void UndoPin() {
            if (_undoStack.Count == 0) {
                Log("[Warning] Undo stack is empty.");
                return;
            }

            var removed = _undoStack.Pop();
            NodeCount = _undoStack.Count;

            if (_undoStack.Count > 0) {
                var top = _undoStack.Peek();
                LastNodeAlt = top.Altitude;
                LastNodeAz = top.Azimuth;
                LastNodeText = top.ToString();
            } else {
                LastNodeAlt = 0.0;
                LastNodeAz = 0.0;
                LastNodeText = "None";
            }

            Log($"[Undo Pin] Removed Horizon Node - Alt: {removed.Altitude:F2}°, Az: {removed.Azimuth:F2}° (Total: {NodeCount})");
        }

        public void ClearPins() {
            if (_undoStack.Count == 0) return;

            _undoStack.Clear();
            NodeCount = 0;
            LastNodeAlt = 0.0;
            LastNodeAz = 0.0;
            LastNodeText = "None";

            Log("[Clear Pins] Removed all horizon nodes from active session.");
        }

        private void SlewJog(double altOffset, double azOffset) {
            if (!IsRunning) {
                Log("[Error] Jogging blocked: Click 'Start Mapping' to initiate the session.");
                return;
            }

            if (!IsMountConnected) {
                Log("[Error] Jogging blocked: Telescope mount is not connected.");
                return;
            }

            Task.Run(async () => {
                try {
                    double currentAlt = _telescopeMediator.GetInfo()?.Altitude ?? 0.0;
                    double currentAz = _telescopeMediator.GetInfo()?.Azimuth ?? 0.0;

                    double targetAlt = currentAlt + altOffset;
                    double targetAz = (currentAz + azOffset + 360.0) % 360.0;

                    // 1. Verify safety limits
                    if (!_safetyManager.IsTargetPositionSafe(targetAlt, targetAz, out string violation)) {
                        Log($"[SAFETY BLOCK] Jog rejected: {violation}");
                        return;
                    }

                    // 2. Perform backlash compensated slew
                    Log($"Jogging mount by Alt: {altOffset:F2}°, Az: {azOffset:F2}° -> Target Alt: {targetAlt:F2}°, Az: {targetAz:F2}°");
                    
                    double lat = _telescopeMediator.GetInfo()?.SiteLatitude ?? _profileService?.ActiveProfile?.AstrometrySettings?.Latitude ?? 0.0;
                    double lon = _telescopeMediator.GetInfo()?.SiteLongitude ?? _profileService?.ActiveProfile?.AstrometrySettings?.Longitude ?? 0.0;

                    var topo = new global::NINA.Astrometry.TopocentricCoordinates(
                        global::NINA.Astrometry.Angle.ByDegree(targetAz),
                        global::NINA.Astrometry.Angle.ByDegree(targetAlt),
                        global::NINA.Astrometry.Angle.ByDegree(lat),
                        global::NINA.Astrometry.Angle.ByDegree(lon)
                    );

                    await _telescopeMediator.SlewToCoordinatesAsync(topo, CancellationToken.None);
                } catch (Exception ex) {
                    Log($"[Error] Slew Jog failed: {ex.Message}");
                }
            });
        }

        public void Dispose() {
            try { StopMapping(); } catch { }
            try { _statusTimer?.Stop(); } catch { }
            try { _safetyManager?.Dispose(); } catch { }
            try { _settingsManager?.Dispose(); } catch { }
            try { _cameraMediator.RemoveConsumer(this); } catch { }
            try { _telescopeMediator.RemoveConsumer(this); } catch { }
        }

        private static Brush CreateFrozenBrush(string hex) {
            try {
                var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                b.Freeze();
                return b;
            } catch { return Brushes.SkyBlue; }
        }
    }
}
