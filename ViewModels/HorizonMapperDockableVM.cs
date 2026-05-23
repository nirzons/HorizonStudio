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
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using System.Linq;
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
        private readonly IWebcamService _webcamService;

        private ImageSource _lastFrame;
        private DeviceDescriptor _selectedWebcam;
        private WebcamState _currentWebcamState = WebcamState.Disconnected;
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

        private bool _isCoAligning = false;
        private double _webcamImageRotationAngle = 0.0;

        internal static readonly Brush StatusIdleColor = CreateFrozenBrush("#72BDFF");
        internal static readonly Brush StatusWarningColor = CreateFrozenBrush("#FBBF24");
        internal static readonly Brush StatusSuccessColor = CreateFrozenBrush("#22C55E");
        internal static readonly Brush StatusFailureColor = CreateFrozenBrush("#EF4444");
        internal static readonly Brush StatusProgressColor = CreateFrozenBrush("#6366F1");

        private MappingCommands _mappingCommands;
        private MountJogCommands _mountJogCommands;

        public ObservableCollection<HorizonNode> HorizonNodes { get; } = new ObservableCollection<HorizonNode>();

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

            HorizonNodes.CollectionChanged += (s, e) => {
                RaisePropertyChanged(nameof(RadarHorizonPoints));
            };

            _mappingCommands = new MappingCommands(this, _telescopeMediator);
            _mountJogCommands = new MountJogCommands(this, _telescopeMediator, _safetyManager, _profileService);

            _webcamService = new WebcamService();
            _webcamService.StateChanged += WebcamService_StateChanged;

            StartWebcamCommand = new RelayCommand(async _ => await StartWebcamAsync(), _ => CanStartWebcam);
            StopWebcamCommand = new RelayCommand(_ => StopWebcam(), _ => CanStopWebcam);
            RefreshWebcamsCommand = new RelayCommand(_ => RefreshWebcams());

            StartCoAlignmentCommand = new RelayCommand(_ => StartCoAlignment());
            SaveCoAlignmentCommand = new RelayCommand(_ => SaveCoAlignment());
            ResetCoAlignmentCommand = new RelayCommand(_ => ResetCoAlignment());

            RefreshWebcams();
            var savedPath = _settingsManager.SelectedUvcCamera;
            if (!string.IsNullOrEmpty(savedPath)) {
                SelectedWebcam = AvailableWebcams.FirstOrDefault(w => string.Equals(w.DevicePath, savedPath, StringComparison.OrdinalIgnoreCase));
            }
            if (SelectedWebcam == null && AvailableWebcams.Count > 0) {
                SelectedWebcam = AvailableWebcams[0];
            }
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
                UpdateRotationAngle();
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
                    UpdateRotationAngle();
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

        public double StepSizeAlt {
            get => _settingsManager.StepSizeAlt;
            set { _settingsManager.StepSizeAlt = value; OnPropertyChanged(); }
        }

        public double StepSizeAz {
            get => _settingsManager.StepSizeAz;
            set { _settingsManager.StepSizeAz = value; OnPropertyChanged(); }
        }

        public bool EnableSolarSafety {
            get => _settingsManager.EnableSolarSafety;
            set => _settingsManager.EnableSolarSafety = value;
        }

        public bool EnableZenithSafety {
            get => _settingsManager.EnableZenithSafety;
            set => _settingsManager.EnableZenithSafety = value;
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

        public ObservableCollection<DeviceDescriptor> AvailableWebcams { get; } = new ObservableCollection<DeviceDescriptor>();

        public DeviceDescriptor SelectedWebcam {
            get => _selectedWebcam;
            set {
                if (_selectedWebcam != value) {
                    _selectedWebcam = value;
                    RaisePropertyChanged(nameof(SelectedWebcam));
                    _settingsManager.SelectedUvcCamera = _selectedWebcam?.DevicePath ?? string.Empty;
                    RaisePropertyChanged(nameof(CanStartWebcam));
                }
            }
        }

        public WebcamState CurrentWebcamState {
            get => _currentWebcamState;
            set {
                if (_currentWebcamState != value) {
                    _currentWebcamState = value;
                    RaisePropertyChanged(nameof(CurrentWebcamState));
                    RaisePropertyChanged(nameof(IsWebcamActive));
                    RaisePropertyChanged(nameof(CanStartWebcam));
                    RaisePropertyChanged(nameof(CanStopWebcam));
                    RaisePropertyChanged(nameof(WebcamStatusIndicatorColor));
                }
            }
        }

        public bool IsWebcamActive => CurrentWebcamState == WebcamState.Streaming;

        public bool CanStartWebcam => SelectedWebcam != null && (CurrentWebcamState == WebcamState.Disconnected || CurrentWebcamState == WebcamState.Error);
        public bool CanStopWebcam => CurrentWebcamState == WebcamState.Streaming || CurrentWebcamState == WebcamState.Connecting;

        public Brush WebcamStatusIndicatorColor {
            get {
                switch (CurrentWebcamState) {
                    case WebcamState.Streaming: return StatusSuccessColor;
                    case WebcamState.Connecting: return StatusProgressColor;
                    case WebcamState.Error: return StatusFailureColor;
                    default: return StatusIdleColor;
                }
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
            set {
                _currentAlt = value;
                RaisePropertyChanged(nameof(CurrentAlt));
                RaisePropertyChanged(nameof(TelescopeRadarX));
                RaisePropertyChanged(nameof(TelescopeRadarY));
            }
        }

        public double CurrentAz {
            get => _currentAz;
            set {
                _currentAz = value;
                RaisePropertyChanged(nameof(CurrentAz));
                RaisePropertyChanged(nameof(TelescopeRadarX));
                RaisePropertyChanged(nameof(TelescopeRadarY));
            }
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

        public bool IsCoAligning {
            get => _isCoAligning;
            set {
                if (_isCoAligning != value) {
                    _isCoAligning = value;
                    RaisePropertyChanged(nameof(IsCoAligning));
                }
            }
        }

        public bool IsCoAligned {
            get => _settingsManager.IsCoAligned;
            set {
                _settingsManager.IsCoAligned = value;
                RaisePropertyChanged(nameof(IsCoAligned));
                RaisePropertyChanged(nameof(AlignmentCenterPoint));
            }
        }

        public double AlignmentCenterX {
            get => _settingsManager.AlignmentCenterX;
            set {
                _settingsManager.AlignmentCenterX = value;
                RaisePropertyChanged(nameof(AlignmentCenterX));
                RaisePropertyChanged(nameof(AlignmentCenterPoint));
            }
        }

        public double AlignmentCenterY {
            get => _settingsManager.AlignmentCenterY;
            set {
                _settingsManager.AlignmentCenterY = value;
                RaisePropertyChanged(nameof(AlignmentCenterY));
                RaisePropertyChanged(nameof(AlignmentCenterPoint));
            }
        }

        public System.Windows.Point AlignmentCenterPoint => new System.Windows.Point(
            IsCoAligned ? AlignmentCenterX : 0.5,
            IsCoAligned ? AlignmentCenterY : 0.5
        );

        public bool IsCounterRotationEnabled {
            get => _settingsManager.IsCounterRotationEnabled;
            set {
                _settingsManager.IsCounterRotationEnabled = value;
                RaisePropertyChanged(nameof(IsCounterRotationEnabled));
                UpdateRotationAngle();
            }
        }

        public double WebcamImageRotationAngle {
            get => _webcamImageRotationAngle;
            set {
                if (_webcamImageRotationAngle != value) {
                    _webcamImageRotationAngle = value;
                    RaisePropertyChanged(nameof(WebcamImageRotationAngle));
                }
            }
        }

        public double TelescopeRadarX {
            get {
                double r = 120.0 * (90.0 - CurrentAlt) / 90.0;
                double rad = CurrentAz * Math.PI / 180.0;
                return 150.0 + r * Math.Sin(rad);
            }
        }

        public double TelescopeRadarY {
            get {
                double r = 120.0 * (90.0 - CurrentAlt) / 90.0;
                double rad = CurrentAz * Math.PI / 180.0;
                return 150.0 - r * Math.Cos(rad);
            }
        }

        public System.Windows.Media.PointCollection RadarHorizonPoints {
            get {
                var points = new System.Windows.Media.PointCollection();
                // Sort nodes by azimuth to draw the winding horizon line correctly
                var sortedNodes = new List<HorizonNode>(HorizonNodes);
                sortedNodes.Sort((a, b) => a.Azimuth.CompareTo(b.Azimuth));

                foreach (var node in sortedNodes) {
                    points.Add(new System.Windows.Point(node.RadarX, node.RadarY));
                }
                return points;
            }
        }

        private void StartCoAlignment() {
            System.Windows.MessageBox.Show(
                "To perform optical axis co-alignment, you need to view both your primary telescope camera and this webcam feed at the same time.\n\n" +
                "How to view both simultaneously in N.I.N.A.:\n" +
                "1. Click and hold this 'Horizon Visual Mapper' tab header.\n" +
                "2. Drag it over N.I.N.A.'s native 'Imaging' panel.\n" +
                "3. Hover near the left, right, or bottom edge until a blue docking preview box highlights.\n" +
                "4. Release your mouse button to dock the webcam feed side-by-side with your main camera view.\n\n" +
                "Co-Alignment Steps:\n" +
                "1. Center a distinct landmark (e.g., a chimney peak or antenna tip) in your primary telescope camera.\n" +
                "2. Click that exact same landmark in the live webcam feed below.\n" +
                "3. Click 'Save Alignment' to save your custom optical alignment center.\n\n" +
                "Click OK to begin co-alignment.",
                "Co-Alignment Assistant Setup Guide",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);

            IsCoAligning = true;
            Log("[Co-Alignment] Co-Alignment assistant started. Center a target in your main telescope camera, then click it on the webcam stream.");
        }

        private void SaveCoAlignment() {
            IsCoAligning = false;
            IsCoAligned = true;
            Log($"[Co-Alignment] Saved custom co-alignment center: ({AlignmentCenterX:F3}, {AlignmentCenterY:F3})");
        }

        private void ResetCoAlignment() {
            IsCoAligning = false;
            IsCoAligned = false;
            AlignmentCenterX = 0.5;
            AlignmentCenterY = 0.5;
            Log("[Co-Alignment] Reset co-alignment to the geometric center.");
        }

        public void HandleImageClick(double x, double y, double frameWidth, double frameHeight) {
            if (!IsWebcamActive) return;

            if (IsCoAligning) {
                if (frameWidth <= 0 || frameHeight <= 0) return;
                AlignmentCenterX = x / frameWidth;
                AlignmentCenterY = y / frameHeight;
                Log($"[Co-Alignment] Click registered: ({x:F1}, {y:F1}) -> Ratio: ({AlignmentCenterX:F3}, {AlignmentCenterY:F3})");
            }
        }

        private void UpdateRotationAngle() {
            if (!IsCounterRotationEnabled || !IsMountConnected) {
                WebcamImageRotationAngle = 0.0;
                return;
            }

            try {
                double lat = _telescopeMediator.GetInfo()?.SiteLatitude ?? _profileService?.ActiveProfile?.AstrometrySettings?.Latitude ?? 0.0;
                double alt = CurrentAlt;
                double az = CurrentAz;

                double latRad = lat * Math.PI / 180.0;
                double altRad = alt * Math.PI / 180.0;
                double azRad = az * Math.PI / 180.0;

                double y = Math.Sin(azRad);
                double x = Math.Cos(altRad) * Math.Tan(latRad) - Math.Sin(altRad) * Math.Cos(azRad);

                double qRad = Math.Atan2(y, x);
                double qDeg = qRad * 180.0 / Math.PI;

                WebcamImageRotationAngle = -qDeg;
            } catch (Exception ex) {
                Logger.Error($"[Horizon Visual Mapper] Counter-rotation calculation failed: {ex.Message}");
                WebcamImageRotationAngle = 0.0;
            }
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
        
        public ICommand JogNorthEastCommand => _mountJogCommands.JogNorthEastCommand;
        public ICommand JogNorthWestCommand => _mountJogCommands.JogNorthWestCommand;
        public ICommand JogSouthEastCommand => _mountJogCommands.JogSouthEastCommand;
        public ICommand JogSouthWestCommand => _mountJogCommands.JogSouthWestCommand;

        public ICommand DoubleJogNorthCommand => _mountJogCommands.DoubleJogNorthCommand;
        public ICommand DoubleJogSouthCommand => _mountJogCommands.DoubleJogSouthCommand;
        public ICommand DoubleJogEastCommand => _mountJogCommands.DoubleJogEastCommand;
        public ICommand DoubleJogWestCommand => _mountJogCommands.DoubleJogWestCommand;
        
        public ICommand DoubleJogNorthEastCommand => _mountJogCommands.DoubleJogNorthEastCommand;
        public ICommand DoubleJogNorthWestCommand => _mountJogCommands.DoubleJogNorthWestCommand;
        public ICommand DoubleJogSouthEastCommand => _mountJogCommands.DoubleJogSouthEastCommand;
        public ICommand DoubleJogSouthWestCommand => _mountJogCommands.DoubleJogSouthWestCommand;

        public ICommand HomeMountCommand => _mountJogCommands.HomeMountCommand;
        public ICommand StopMountCommand => _mountJogCommands.StopMountCommand;

        public ICommand StartCoAlignmentCommand { get; }
        public ICommand SaveCoAlignmentCommand { get; }
        public ICommand ResetCoAlignmentCommand { get; }

        public ICommand StartWebcamCommand { get; }
        public ICommand StopWebcamCommand { get; }
        public ICommand RefreshWebcamsCommand { get; }

        private async Task StartWebcamAsync() {
            if (SelectedWebcam == null) return;
            Log($"[System] Connecting to webcam: {SelectedWebcam.Name}...");
            try {
                await _webcamService.StartCaptureAsync(SelectedWebcam.DevicePath, OnFrameCaptured);
            } catch (Exception ex) {
                Log($"[ERROR] Failed to start webcam capture: {ex.Message}");
            }
        }

        private void StopWebcam() {
            Log("[System] Stopping webcam stream...");
            _webcamService.StopCapture();
            LastFrame = null;
        }

        public void RefreshWebcams() {
            AvailableWebcams.Clear();
            var cameras = _webcamService.GetAvailableCameras();
            foreach (var camera in cameras) {
                AvailableWebcams.Add(camera);
            }
            Log($"[System] Discovered {AvailableWebcams.Count} available webcam(s).");
            
            // Auto-select first camera if none is selected
            if (SelectedWebcam == null && AvailableWebcams.Count > 0) {
                SelectedWebcam = AvailableWebcams[0];
            } else if (SelectedWebcam != null) {
                var matching = AvailableWebcams.FirstOrDefault(w => string.Equals(w.DevicePath, SelectedWebcam.DevicePath, StringComparison.OrdinalIgnoreCase));
                if (matching == null) {
                    SelectedWebcam = AvailableWebcams.Count > 0 ? AvailableWebcams[0] : null;
                } else {
                    SelectedWebcam = matching;
                }
            }
            RaisePropertyChanged(nameof(CanStartWebcam));
        }

        private void OnFrameCaptured(byte[] frameData) {
            if (frameData == null || frameData.Length == 0) return;

            try {
                using (var ms = new MemoryStream(frameData)) {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                    bitmap.Freeze(); // Freeze to allow cross-thread UI access

                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => {
                        LastFrame = bitmap;
                    }));
                }
            } catch (Exception ex) {
                Logger.Debug($"[Horizon Visual Mapper] Frame decoding failed: {ex.Message}");
            }
        }

        private void WebcamService_StateChanged(object sender, WebcamState state) {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => {
                CurrentWebcamState = state;
                if (state == WebcamState.Streaming) {
                    Log("[System] Webcam stream active.");
                } else if (state == WebcamState.Disconnected) {
                    Log("[System] Webcam disconnected.");
                } else if (state == WebcamState.Error) {
                    Log("[ERROR] Webcam connection failed or device was unplugged.");
                    LastFrame = null;
                }
            }));
        }

        public void Dispose() {
            try { _webcamService?.Dispose(); } catch { }
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
