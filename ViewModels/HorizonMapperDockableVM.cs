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
using NirZonshine.NINA.HorizonVisualMapper.ViewModels.Commands;

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
        internal int TaskExecutingFlag = 0;

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

        internal static readonly Brush StatusIdleColor = CreateFrozenBrush("#72BDFF");
        internal static readonly Brush StatusWarningColor = CreateFrozenBrush("#FBBF24");
        internal static readonly Brush StatusSuccessColor = CreateFrozenBrush("#22C55E");
        internal static readonly Brush StatusFailureColor = CreateFrozenBrush("#EF4444");
        internal static readonly Brush StatusProgressColor = CreateFrozenBrush("#6366F1");

        private MappingCommands _mappingCommands;
        private MountJogCommands _mountJogCommands;

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

            _mappingCommands = new MappingCommands(this, _telescopeMediator);
            _mountJogCommands = new MountJogCommands(this, _telescopeMediator, _safetyManager, _profileService);
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
            _mappingCommands?.StopMapping();
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

        public bool CanStart => IsMountConnected && !IsRunning && Interlocked.CompareExchange(ref TaskExecutingFlag, 0, 0) == 0;

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

        internal void SetStatus(string text, Brush color) {
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

        public ICommand StartMappingCommand => _mappingCommands.StartMappingCommand;
        public ICommand StopMappingCommand => _mappingCommands.StopMappingCommand;
        public ICommand DropPinCommand => _mappingCommands.DropPinCommand;
        public ICommand UndoPinCommand => _mappingCommands.UndoPinCommand;
        public ICommand ClearPinsCommand => _mappingCommands.ClearPinsCommand;

        public ICommand JogNorthCommand => _mountJogCommands.JogNorthCommand;
        public ICommand JogSouthCommand => _mountJogCommands.JogSouthCommand;
        public ICommand JogEastCommand => _mountJogCommands.JogEastCommand;
        public ICommand JogWestCommand => _mountJogCommands.JogWestCommand;

        public void Dispose() {
            try { _mappingCommands?.StopMapping(); } catch { }
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
