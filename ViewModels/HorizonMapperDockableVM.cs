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
using NirZonshine.NINA.HorizonStudio.Domain;
using NirZonshine.NINA.HorizonStudio.Services;
using NirZonshine.NINA.HorizonStudio.ViewModels.Commands;

namespace NirZonshine.NINA.HorizonStudio.ViewModels {

    /// <summary>
    /// Contract for the code-behind's image-click handler.
    /// Allows Options.xaml.cs to avoid casting DataContext to the concrete VM type.
    /// </summary>
    public interface IImageClickHandler {
        void HandleImageClick(double x, double y, double frameWidth, double frameHeight);
    }

    public interface IRadarClickHandler {
        void HandleRadarClick(double x, double y);
        bool IsNearHorizon(double canvasX, double canvasY);
    }

    [Export(typeof(global::NINA.Equipment.Interfaces.ViewModel.IDockableVM))]
    public class HorizonMapperDockableVM : DockableVM, ICameraConsumer, ITelescopeConsumer, IImageClickHandler, IRadarClickHandler {
        private readonly IProfileService _profileService;
        private readonly ICameraMediator _cameraMediator;
        private readonly ITelescopeMediator _telescopeMediator;
        private readonly IImagingMediator _imagingMediator;
        private readonly System.Windows.Threading.DispatcherTimer _statusTimer;

        private readonly SettingsManager _settingsManager;
        private readonly SafetyManager _safetyManager;
        private readonly IWebcamService _webcamService;

        private bool _disposed = false;
        private ImageSource _lastFrame;
        private double _lastFrameWidth = 0;
        private double _lastFrameHeight = 0;
        private DeviceDescriptor _selectedWebcam;
        private WebcamState _currentWebcamState = WebcamState.Disconnected;
        internal int TaskExecutingFlag = 0;

        // FIX #11: Replace the unbounded string-append Log pattern with a capped ring-buffer.
        // Keeps the last MaxLogLines entries so long mapping sessions don't accumulate
        // megabytes of log string in memory.
        private const int MaxLogLines = 500;
        private readonly Queue<string> _logBuffer = new Queue<string>();

        private double _currentAlt;
        private double _currentAz;
        private double _lastNodeAlt;
        private double _lastNodeAz;
        private int _nodeCount = 0;
        private string _lastNodeText = "None";
        private string _logs = "[System] Welcome to Horizon Visual Mapper. Select camera to begin.";

        private string _statusIndicatorText = "Ready";
        private Brush _statusIndicatorColor = StatusIdleColor;

        private CameraInfo _currentCameraInfo;
        private TelescopeInfo _currentTelescopeInfo;
        private bool _lastIsCameraConnected;
        private bool _lastIsMountConnected;

        private bool _isCoAligning = false;
        private double _webcamImageRotationAngle = 0.0;

        private HorizonNode _syncRefNode = null;
        private bool _isSyncPreparing = false;
        private SyncLandmark _selectedLandmark = null;

        private bool _isMainCameraActive = false;
        private int _detectedStarCount = 0;
        private double _averageHFR = 0.0;
        private double _averageADU = 0.0;
        private CancellationTokenSource _mainCameraCTS = null;
        private Task _mainCameraLoopTask = null;
        private BinningMode _selectedBinning = null;

        internal static readonly Brush StatusIdleColor = CreateFrozenBrush("#72BDFF");
        internal static readonly Brush StatusWarningColor = CreateFrozenBrush("#FBBF24");
        internal static readonly Brush StatusSuccessColor = CreateFrozenBrush("#22C55E");
        internal static readonly Brush StatusFailureColor = CreateFrozenBrush("#EF4444");
        internal static readonly Brush StatusProgressColor = CreateFrozenBrush("#6366F1");

        private MappingCommands _mappingCommands;
        private MountJogCommands _mountJogCommands;

        public ObservableCollection<HorizonNode> HorizonNodes { get; } = new ObservableCollection<HorizonNode>();
        public ObservableCollection<SyncLandmark> SyncLandmarks { get; } = new ObservableCollection<SyncLandmark>();

        [ImportingConstructor]
        public HorizonMapperDockableVM(IProfileService profileService, ICameraMediator cameraMediator, ITelescopeMediator telescopeMediator, IImagingMediator imagingMediator) : base(profileService) {
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            _cameraMediator = cameraMediator ?? throw new ArgumentNullException(nameof(cameraMediator));
            _telescopeMediator = telescopeMediator ?? throw new ArgumentNullException(nameof(telescopeMediator));
            _imagingMediator = imagingMediator ?? throw new ArgumentNullException(nameof(imagingMediator));

            _cameraMediator.RegisterConsumer(this);
            _telescopeMediator.RegisterConsumer(this);

            Title = "Horizon Studio";

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
                RaisePropertyChanged(nameof(RadarObstructionPoints));
                RaisePropertyChanged(nameof(CanVerifyPoints));
            };

            _mappingCommands = new MappingCommands(this, _telescopeMediator, _profileService);
            _mountJogCommands = new MountJogCommands(this, _telescopeMediator, _safetyManager, _profileService);

            _webcamService = new WebcamService();
            _webcamService.StateChanged += WebcamService_StateChanged;

            StartWebcamCommand = new RelayCommand(async _ => await StartWebcamAsync(), _ => CanStartWebcam);
            StopWebcamCommand = new RelayCommand(_ => StopWebcam(), _ => CanStopWebcam);
            RefreshWebcamsCommand = new RelayCommand(_ => RefreshWebcams());

            StartMainCameraCommand = new RelayCommand(async _ => await StartMainCameraAsync(), _ => CanStartMainCamera);
            StopMainCameraCommand = new RelayCommand(_ => StopMainCamera(), _ => CanStopMainCamera);
            SelectVisualFeedSourceCommand = new RelayCommand(p => VisualFeedSource = p?.ToString());

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
                RaisePropertyChanged(nameof(CanVerifyPoints));
                RaisePropertyChanged(nameof(CanJog));
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
                RaisePropertyChanged(nameof(AvailableBinningModes));
                RaisePropertyChanged(nameof(HasMechanicalShutter));
                RaisePropertyChanged(nameof(CanStartMainCamera));
                RaisePropertyChanged(nameof(CanStopMainCamera));
                
                // Try to set SelectedBinning if a camera is connected
                if (deviceInfo != null) {
                    var savedBin = _settingsManager.Binning;
                    SelectedBinning = AvailableBinningModes.FirstOrDefault(b => string.Equals(b.Name, savedBin, StringComparison.OrdinalIgnoreCase))
                                      ?? AvailableBinningModes.FirstOrDefault();
                }
            });
        }

        public void UpdateDeviceInfo(TelescopeInfo deviceInfo) {
            _currentTelescopeInfo = deviceInfo;
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
                RaisePropertyChanged(nameof(IsMountConnected));
                RaisePropertyChanged(nameof(CanStart));
                RaisePropertyChanged(nameof(IsSlewing));
                RaisePropertyChanged(nameof(CanDropPin));
                RaisePropertyChanged(nameof(CanVerifyPoints));
                RaisePropertyChanged(nameof(CanJog));
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
        public bool IsSlewing => (_currentTelescopeInfo?.Slewing ?? _telescopeMediator?.GetInfo()?.Slewing ?? false);

        public bool CanStart => false; // Start/Stop buttons removed, mapping is always running
        public bool CanDropPin {
            get {
                if (!IsRunning || IsSlewing || IsActionSlewing || !IsMountConnected || IsSyncPreparing) {
                    return false;
                }
                var active = ActiveNode;
                if (active == null) {
                    return true;
                }
                
                double dAlt = Math.Abs(CurrentAlt - active.Altitude);
                double dAz = Math.Abs(CurrentAz - active.Azimuth) % 360.0;
                if (dAz > 180.0) dAz = 360.0 - dAz;
                
                // Disable if mount is closer than 0.05 degrees in both axes to the active node
                return (dAlt >= 0.05 || dAz >= 0.05);
            }
        }

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

                if (value is System.Windows.Media.Imaging.BitmapSource bmp) {
                    if (bmp.PixelWidth != _lastFrameWidth || bmp.PixelHeight != _lastFrameHeight) {
                        _lastFrameWidth = bmp.PixelWidth;
                        _lastFrameHeight = bmp.PixelHeight;
                        RaisePropertyChanged(nameof(AlignmentTranslationX));
                        RaisePropertyChanged(nameof(AlignmentTranslationY));
                    }
                }
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

        public bool IsWebcamActive => (VisualFeedSource == "Webcam" && CurrentWebcamState == WebcamState.Streaming) ||
                                      (VisualFeedSource == "MainCamera" && IsMainCameraActive);

        public bool CanStartWebcam => SelectedWebcam != null && (CurrentWebcamState == WebcamState.Disconnected || CurrentWebcamState == WebcamState.Error) && VisualFeedSource == "Webcam";
        public bool CanStopWebcam => (CurrentWebcamState == WebcamState.Streaming || CurrentWebcamState == WebcamState.Connecting) && VisualFeedSource == "Webcam";

        public string VisualFeedSource {
            get => _settingsManager.VisualFeedSource;
            set {
                if (_settingsManager.VisualFeedSource != value) {
                    _settingsManager.VisualFeedSource = value;
                    RaisePropertyChanged(nameof(VisualFeedSource));
                    RaisePropertyChanged(nameof(IsWebcamFeedSelected));
                    RaisePropertyChanged(nameof(IsMainCameraFeedSelected));
                    RaisePropertyChanged(nameof(IsWebcamActive));
                    RaisePropertyChanged(nameof(CanStartWebcam));
                    RaisePropertyChanged(nameof(CanStopWebcam));
                    RaisePropertyChanged(nameof(CanStartMainCamera));
                    RaisePropertyChanged(nameof(CanStopMainCamera));
                    
                    if (value == "MainCamera") {
                        StopWebcam();
                    } else {
                        StopMainCamera();
                    }
                }
            }
        }

        public bool IsWebcamFeedSelected => VisualFeedSource == "Webcam";
        public bool IsMainCameraFeedSelected => VisualFeedSource == "MainCamera";

        public bool IsMainCameraActive {
            get => _isMainCameraActive;
            private set {
                if (_isMainCameraActive != value) {
                    _isMainCameraActive = value;
                    RaisePropertyChanged(nameof(IsMainCameraActive));
                    RaisePropertyChanged(nameof(IsWebcamActive));
                    RaisePropertyChanged(nameof(CanStartMainCamera));
                    RaisePropertyChanged(nameof(CanStopMainCamera));
                }
            }
        }

        public bool IsAutoExposureEnabled {
            get => _settingsManager.IsAutoExposureEnabled;
            set {
                if (_settingsManager.IsAutoExposureEnabled != value) {
                    _settingsManager.IsAutoExposureEnabled = value;
                    RaisePropertyChanged(nameof(IsAutoExposureEnabled));
                }
            }
        }

        public double TargetADU {
            get => _settingsManager.TargetADU;
            set {
                if (_settingsManager.TargetADU != value) {
                    _settingsManager.TargetADU = value;
                    RaisePropertyChanged(nameof(TargetADU));
                }
            }
        }

        public int DetectedStarCount {
            get => _detectedStarCount;
            private set {
                if (_detectedStarCount != value) {
                    _detectedStarCount = value;
                    RaisePropertyChanged(nameof(DetectedStarCount));
                }
            }
        }

        public double AverageHFR {
            get => _averageHFR;
            private set {
                if (_averageHFR != value) {
                    _averageHFR = value;
                    RaisePropertyChanged(nameof(AverageHFR));
                }
            }
        }

        public double AverageADU {
            get => _averageADU;
            private set {
                if (_averageADU != value) {
                    _averageADU = value;
                    RaisePropertyChanged(nameof(AverageADU));
                }
            }
        }

        public bool HasMechanicalShutter => _currentCameraInfo?.HasShutter ?? _cameraMediator?.GetInfo()?.HasShutter ?? false;

        public IEnumerable<BinningMode> AvailableBinningModes =>
            _currentCameraInfo?.BinningModes ?? _cameraMediator?.GetInfo()?.BinningModes ?? Enumerable.Empty<BinningMode>();

        public BinningMode SelectedBinning {
            get => _selectedBinning;
            set {
                if (_selectedBinning != value) {
                    _selectedBinning = value;
                    RaisePropertyChanged(nameof(SelectedBinning));
                    if (value != null) {
                        Binning = value.Name;
                    }
                }
            }
        }

        public bool CanStartMainCamera => IsCameraConnected && !IsMainCameraActive && VisualFeedSource == "MainCamera";
        public bool CanStopMainCamera => IsMainCameraActive && VisualFeedSource == "MainCamera";

        public ICommand StartMainCameraCommand { get; }
        public ICommand StopMainCameraCommand { get; }
        public ICommand SelectVisualFeedSourceCommand { get; }

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

        private bool _isRunning = true; // Always running
        public bool IsRunning {
            get => _isRunning;
            set {
                _isRunning = value;
                RaisePropertyChanged(nameof(IsRunning));
                RaisePropertyChanged(nameof(CanStart));
                RaisePropertyChanged(nameof(CanDropPin));
            }
        }

        public double CurrentAlt {
            get => _currentAlt;
            set {
                _currentAlt = value;
                RaisePropertyChanged(nameof(CurrentAlt));
                RaisePropertyChanged(nameof(TelescopeRadarX));
                RaisePropertyChanged(nameof(TelescopeRadarY));
                RaisePropertyChanged(nameof(CanDropPin));
                if (IsSyncPreparing) {
                    RaisePropertyChanged(nameof(CanConfirmSync));
                    RaisePropertyChanged(nameof(SyncInstructionText));
                    System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() => {
                        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                    }));
                }
            }
        }

        public double CurrentAz {
            get => _currentAz;
            set {
                _currentAz = value;
                RaisePropertyChanged(nameof(CurrentAz));
                RaisePropertyChanged(nameof(TelescopeRadarX));
                RaisePropertyChanged(nameof(TelescopeRadarY));
                RaisePropertyChanged(nameof(CanDropPin));
                if (IsSyncPreparing) {
                    RaisePropertyChanged(nameof(CanConfirmSync));
                    RaisePropertyChanged(nameof(SyncInstructionText));
                    System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() => {
                        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                    }));
                }
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

        public HorizonNode SyncRefNode {
            get => _syncRefNode;
            set {
                _syncRefNode = value;
                RaisePropertyChanged(nameof(SyncRefNode));
                RaisePropertyChanged(nameof(SyncInstructionText));
            }
        }

        public bool IsSyncPreparing {
            get => _isSyncPreparing;
            set {
                _isSyncPreparing = value;
                RaisePropertyChanged(nameof(IsSyncPreparing));
                RaisePropertyChanged(nameof(CanPrepareSync));
                RaisePropertyChanged(nameof(CanConfirmSync));
                RaisePropertyChanged(nameof(CanDropPin));
                RaisePropertyChanged(nameof(CanVerifyPoints));
            }
        }

        public SyncLandmark SelectedLandmark {
            get => _selectedLandmark;
            set {
                if (_selectedLandmark != value) {
                    if (_selectedLandmark != null) {
                        _selectedLandmark.IsSelected = false;
                    }
                    _selectedLandmark = value;
                    if (_selectedLandmark != null) {
                        _selectedLandmark.IsSelected = true;
                        _activeNodeIndex = -1;
                        RaisePropertyChanged(nameof(ActiveNodeIndex));
                    }
                    RaisePropertyChanged(nameof(SelectedLandmark));
                    RaisePropertyChanged(nameof(IsLandmarkSelected));
                    RaisePropertyChanged(nameof(ActiveNode));
                    RaisePropertyChanged(nameof(HasActiveNode));
                    RaisePropertyChanged(nameof(CanPrepareSync));
                    RaisePropertyChanged(nameof(ActiveNodeRadarX));
                    RaisePropertyChanged(nameof(ActiveNodeRadarY));
                    RaisePropertyChanged(nameof(CanDropPin));
                }
            }
        }

        public bool IsLandmarkSelected => SelectedLandmark != null;
        public bool HasLandmarks => SyncLandmarks.Count > 0;

        public bool CanPrepareSync => HasActiveNode && !IsSyncPreparing && IsMountConnected;
        
        public bool CanConfirmSync {
            get {
                if (!IsSyncPreparing || !IsMountConnected || IsSlewing || IsActionSlewing || SyncRefNode == null) {
                    return false;
                }
                // Check if mount has moved enough from the sync reference point (at least 0.05 degrees / 3 arcminutes)
                double dist = GetAngularDistance(CurrentAz, CurrentAlt, SyncRefNode.Azimuth, SyncRefNode.Altitude);
                return dist >= 0.05;
            }
        }

        public string SyncInstructionText {
            get {
                if (SyncRefNode == null) return "⚠️ Sync Mode: Jog mount to center landmark in feed, then click Confirm.";
                double dist = GetAngularDistance(CurrentAz, CurrentAlt, SyncRefNode.Azimuth, SyncRefNode.Altitude);
                if (dist < 0.05) {
                    return "⚠️ Sync Mode: Jog mount to center landmark in feed (Confirm will enable once mount has moved).";
                }
                return "⚠️ Sync Mode: Landmark centered in feed. Click Confirm Sync to warp profile.";
            }
        }

        public bool IsCoAligning {
            get => _isCoAligning;
            set {
                if (_isCoAligning != value) {
                    _isCoAligning = value;
                    RaisePropertyChanged(nameof(IsCoAligning));
                    RaisePropertyChanged(nameof(AlignmentTranslationX));
                    RaisePropertyChanged(nameof(AlignmentTranslationY));
                    UpdateRotationAngle();
                }
            }
        }

        public bool IsCoAligned {
            get => _settingsManager.IsCoAligned;
            set {
                _settingsManager.IsCoAligned = value;
                RaisePropertyChanged(nameof(IsCoAligned));
                RaisePropertyChanged(nameof(AlignmentCenterPoint));
                RaisePropertyChanged(nameof(AlignmentTranslationX));
                RaisePropertyChanged(nameof(AlignmentTranslationY));
            }
        }

        public double AlignmentCenterX {
            get => _settingsManager.AlignmentCenterX;
            set {
                _settingsManager.AlignmentCenterX = value;
                RaisePropertyChanged(nameof(AlignmentCenterX));
                RaisePropertyChanged(nameof(AlignmentCenterPoint));
                RaisePropertyChanged(nameof(AlignmentTranslationX));
                RaisePropertyChanged(nameof(AlignmentTranslationY));
            }
        }

        public double AlignmentCenterY {
            get => _settingsManager.AlignmentCenterY;
            set {
                _settingsManager.AlignmentCenterY = value;
                RaisePropertyChanged(nameof(AlignmentCenterY));
                RaisePropertyChanged(nameof(AlignmentCenterPoint));
                RaisePropertyChanged(nameof(AlignmentTranslationX));
                RaisePropertyChanged(nameof(AlignmentTranslationY));
            }
        }

        public System.Windows.Point AlignmentCenterPoint => new System.Windows.Point(
            IsCoAligned ? AlignmentCenterX : 0.5,
            IsCoAligned ? AlignmentCenterY : 0.5
        );

        public double AlignmentTranslationX {
            get {
                if (IsCoAligning || !IsCoAligned || LastFrame == null) return 0.0;
                double imgWidth = 0;
                double imgHeight = 0;
                if (LastFrame is System.Windows.Media.Imaging.BitmapSource bmp) {
                    imgWidth = bmp.PixelWidth;
                    imgHeight = bmp.PixelHeight;
                }
                if (imgWidth <= 0 || imgHeight <= 0) return 0.0;

                double scaleX = 600.0 / imgWidth;
                double scaleY = 600.0 / imgHeight;
                double scale = Math.Max(scaleX, scaleY);

                double renderWidth = imgWidth * scale;
                return renderWidth * (0.5 - AlignmentCenterX);
            }
        }

        public double AlignmentTranslationY {
            get {
                if (IsCoAligning || !IsCoAligned || LastFrame == null) return 0.0;
                double imgWidth = 0;
                double imgHeight = 0;
                if (LastFrame is System.Windows.Media.Imaging.BitmapSource bmp) {
                    imgWidth = bmp.PixelWidth;
                    imgHeight = bmp.PixelHeight;
                }
                if (imgWidth <= 0 || imgHeight <= 0) return 0.0;

                double scaleX = 600.0 / imgWidth;
                double scaleY = 600.0 / imgHeight;
                double scale = Math.Max(scaleX, scaleY);

                double renderHeight = imgHeight * scale;
                return renderHeight * (0.5 - AlignmentCenterY);
            }
        }

        public bool IsCounterRotationEnabled {
            get => _settingsManager.IsCounterRotationEnabled;
            set {
                _settingsManager.IsCounterRotationEnabled = value;
                RaisePropertyChanged(nameof(IsCounterRotationEnabled));
                UpdateRotationAngle();
            }
        }

        public double CameraRotationOffset {
            get => _settingsManager.CameraRotationOffset;
            set {
                _settingsManager.CameraRotationOffset = value;
                RaisePropertyChanged(nameof(CameraRotationOffset));
                UpdateRotationAngle();
            }
        }

        public bool IsExactPositionEnabled {
            get => _settingsManager.IsExactPositionEnabled;
            set {
                _settingsManager.IsExactPositionEnabled = value;
                RaisePropertyChanged(nameof(IsExactPositionEnabled));
            }
        }

        public bool IsRadarOverlayEnabled {
            get => _settingsManager.IsRadarOverlayEnabled;
            set {
                _settingsManager.IsRadarOverlayEnabled = value;
                RaisePropertyChanged(nameof(IsRadarOverlayEnabled));
            }
        }


        public double? LastRequestedAlt { get; set; }
        public double? LastRequestedAz { get; set; }

        private int _activeNodeIndex = -1;
        public int ActiveNodeIndex {
            get => _activeNodeIndex;
            set {
                if (_activeNodeIndex != value) {
                    _activeNodeIndex = value;
                    if (_activeNodeIndex >= 0) {
                        _selectedLandmark = null;
                        RaisePropertyChanged(nameof(SelectedLandmark));
                        RaisePropertyChanged(nameof(IsLandmarkSelected));
                    }
                    RaisePropertyChanged(nameof(ActiveNodeIndex));
                    RaisePropertyChanged(nameof(ActiveNode));
                    RaisePropertyChanged(nameof(ActiveNodeRadarX));
                    RaisePropertyChanged(nameof(ActiveNodeRadarY));
                    RaisePropertyChanged(nameof(HasActiveNode));
                    RaisePropertyChanged(nameof(CanDropPin));
                    RaisePropertyChanged(nameof(CanPrepareSync));
                }
            }
        }

        public HorizonNode ActiveNode {
            get {
                if (SelectedLandmark != null) {
                    return new HorizonNode(SelectedLandmark.Azimuth, SelectedLandmark.Altitude);
                }
                if (ActiveNodeIndex >= 0 && ActiveNodeIndex < HorizonNodes.Count) {
                    return HorizonNodes[ActiveNodeIndex];
                }
                return null;
            }
        }

        public bool HasActiveNode => ActiveNode != null;

        public double ActiveNodeRadarX => ActiveNode?.RadarX ?? 250.0;
        public double ActiveNodeRadarY => ActiveNode?.RadarY ?? 250.0;

        private bool _isActionSlewing = false;
        public bool IsActionSlewing {
            get => _isActionSlewing;
            set {
                if (_isActionSlewing != value) {
                    _isActionSlewing = value;
                    RaisePropertyChanged(nameof(IsActionSlewing));
                    RaisePropertyChanged(nameof(CanVerifyPoints));
                    RaisePropertyChanged(nameof(CanDropPin));
                    RaisePropertyChanged(nameof(CanStart));
                    RaisePropertyChanged(nameof(CanJog));
                }
            }
        }

        public int VerificationStepSize {
            get => _settingsManager.VerificationStepSize;
            set {
                _settingsManager.VerificationStepSize = value;
                RaisePropertyChanged(nameof(VerificationStepSize));
            }
        }

        public List<int> VerificationStepSizes { get; } = new List<int> { 1, 2, 5, 10, 20, 50 };

        public bool CanVerifyPoints => IsMountConnected && !IsSlewing && !IsActionSlewing && HorizonNodes.Count > 0 && !IsSyncPreparing;

        public bool CanJog => IsMountConnected && !IsSlewing && !IsActionSlewing;

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
                double r = 220.0 * (90.0 - CurrentAlt) / 90.0;
                double rad = CurrentAz * Math.PI / 180.0;
                return 250.0 + r * Math.Sin(rad);
            }
        }

        public double TelescopeRadarY {
            get {
                double r = 220.0 * (90.0 - CurrentAlt) / 90.0;
                double rad = CurrentAz * Math.PI / 180.0;
                return 250.0 - r * Math.Cos(rad);
            }
        }

        public double GetInterpolatedAltitude(double azimuth) {
            var nodes = HorizonNodes;
            if (nodes.Count == 0) return 0.0;
            if (nodes.Count == 1) return nodes[0].Altitude;

            // Sort nodes by azimuth
            var sorted = new List<HorizonNode>(nodes);
            sorted.Sort((a, b) => a.Azimuth.CompareTo(b.Azimuth));

            // Keep azimuth in [0, 360)
            azimuth = (azimuth % 360.0 + 360.0) % 360.0;

            // Find the enclosing interval
            for (int i = 0; i < sorted.Count - 1; i++) {
                if (azimuth >= sorted[i].Azimuth && azimuth <= sorted[i + 1].Azimuth) {
                    double range = sorted[i + 1].Azimuth - sorted[i].Azimuth;
                    if (range == 0.0) return sorted[i].Altitude;
                    double t = (azimuth - sorted[i].Azimuth) / range;
                    return sorted[i].Altitude + t * (sorted[i + 1].Altitude - sorted[i].Altitude);
                }
            }

            // If we are here, azimuth is in the wrap-around gap between the last and first node
            double lastAz = sorted[sorted.Count - 1].Azimuth;
            double firstAz = sorted[0].Azimuth;
            double lastAlt = sorted[sorted.Count - 1].Altitude;
            double firstAlt = sorted[0].Altitude;

            double gapSize = (firstAz - lastAz + 360.0) % 360.0;
            if (gapSize == 0.0) return lastAlt;

            double diff = (azimuth - lastAz + 360.0) % 360.0;
            double tGap = diff / gapSize;
            return lastAlt + tGap * (firstAlt - lastAlt);
        }

        public System.Windows.Media.PointCollection RadarHorizonPoints {
            get {
                var points = new System.Windows.Media.PointCollection();
                if (HorizonNodes.Count == 0) return points;

                var sortedNodes = new List<HorizonNode>(HorizonNodes);
                sortedNodes.Sort((a, b) => a.Azimuth.CompareTo(b.Azimuth));

                if (sortedNodes.Count == 1) {
                    points.Add(new System.Windows.Point(sortedNodes[0].RadarX, sortedNodes[0].RadarY));
                    return points;
                }

                // Trace the horizon line clockwise in 1-degree steps all the way around 360 degrees
                // This ensures a perfectly smooth polar circle/spiral arc that matches N.I.N.A.'s interpolation!
                for (double az = 0.0; az < 360.0; az += 1.0) {
                    double alt = GetInterpolatedAltitude(az);
                    double r = 220.0 * (90.0 - alt) / 90.0;
                    double rad = az * Math.PI / 180.0;
                    double x = 250.0 + r * Math.Sin(rad);
                    double y = 250.0 - r * Math.Cos(rad);
                    points.Add(new System.Windows.Point(x, y));
                }

                // Add the start point at 360° (0°) to close the loop
                double startAlt = GetInterpolatedAltitude(0.0);
                double startR = 220.0 * (90.0 - startAlt) / 90.0;
                points.Add(new System.Windows.Point(250.0, 250.0 - startR));

                return points;
            }
        }

        public System.Windows.Media.PointCollection RadarObstructionPoints {
            get {
                var points = new System.Windows.Media.PointCollection();
                if (HorizonNodes.Count == 0) return points;

                // 1. Trace the outer circle clockwise (Altitude = 0)
                for (double az = 0.0; az <= 360.0; az += 2.0) {
                    double rad = az * Math.PI / 180.0;
                    double x = 250.0 + 220.0 * Math.Sin(rad);
                    double y = 250.0 - 220.0 * Math.Cos(rad);
                    points.Add(new System.Windows.Point(x, y));
                }

                // 2. Trace the horizon line counter-clockwise (decreasing azimuth) back to the start
                for (double az = 360.0; az >= 0.0; az -= 2.0) {
                    double alt = GetInterpolatedAltitude(az);
                    double r = 220.0 * (90.0 - alt) / 90.0;
                    double rad = az * Math.PI / 180.0;
                    double x = 250.0 + r * Math.Sin(rad);
                    double y = 250.0 - r * Math.Cos(rad);
                    points.Add(new System.Windows.Point(x, y));
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

        // FIX #19: Implements IImageClickHandler — Options.xaml.cs now casts to the interface,
        // not to the concrete VM type.
        public void HandleImageClick(double x, double y, double frameWidth, double frameHeight) {
            if (!IsWebcamActive) return;

            if (IsCoAligning) {
                if (frameWidth <= 0 || frameHeight <= 0) return;
                AlignmentCenterX = x / frameWidth;
                AlignmentCenterY = y / frameHeight;
                Log($"[Co-Alignment] Click registered: ({x:F1}, {y:F1}) -> Ratio: ({AlignmentCenterX:F3}, {AlignmentCenterY:F3})");
            }
        }

        public void HandleRadarClick(double x, double y) {
            _mappingCommands.RadarClickSlew(x, y);
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

        public bool IsNearHorizon(double canvasX, double canvasY) {
            double dx = canvasX - 250.0;
            double dy = 250.0 - canvasY;
            double r = Math.Sqrt(dx * dx + dy * dy);
            if (r > 220.0) r = 220.0;

            double rad = Math.Atan2(dx, dy);
            double azimuth = rad * 180.0 / Math.PI;
            azimuth = (azimuth % 360.0 + 360.0) % 360.0;
            double altitude = 90.0 - (90.0 * r / 220.0);
            if (altitude < 0.0) altitude = 0.0;
            if (altitude > 90.0) altitude = 90.0;

            foreach (var landmark in SyncLandmarks) {
                double distToSpecial = GetAngularDistance(azimuth, altitude, landmark.Azimuth, landmark.Altitude);
                if (distToSpecial < 2.5) {
                    return true;
                }
            }

            if (HorizonNodes.Count == 0) return true;

            double horizonAlt = GetInterpolatedAltitude(azimuth);
            return Math.Abs(altitude - horizonAlt) <= 5.0;
        }

        private void UpdateRotationAngle() {
            if (IsCoAligning) {
                WebcamImageRotationAngle = 0.0;
                return;
            }

            if (!IsMountConnected) {
                WebcamImageRotationAngle = 0.0;
                return;
            }

            double totalRotation = CameraRotationOffset;

            if (IsCounterRotationEnabled) {
                try {
                    double lat = _telescopeMediator.GetInfo()?.SiteLatitude ?? _profileService?.ActiveProfile?.AstrometrySettings?.Latitude ?? 0.0;
                    double altRad = CurrentAlt * Math.PI / 180.0;
                    double azRad = CurrentAz * Math.PI / 180.0;
                    double latRad = lat * Math.PI / 180.0;

                    // Hour Angle (HA) calculation
                    // Used as a fallback to determine whether the mount is pointing to the East or West of the meridian.
                    double yHA = -Math.Sin(azRad) * Math.Cos(altRad);
                    double xHA = Math.Sin(altRad) * Math.Cos(latRad) - Math.Cos(altRad) * Math.Sin(latRad) * Math.Cos(azRad);
                    double haDeg = Math.Atan2(yHA, xHA) * 180.0 / Math.PI;

                    // Determine if the mount is physically pointing East (requiring the 180° meridian flip correction).
                    // We query the actual physical SideOfPier from the mount:
                    // - PierWest (value 1) means the telescope is physically on the West side of the pier, pointing East.
                    // - PierEast (value 0) means the telescope is physically on the East side of the pier, pointing West.
                    bool isPointingEast = false;
                    var side = _currentTelescopeInfo?.SideOfPier;
                    if (side != null && !side.ToString().Contains("Unknown")) {
                        isPointingEast = side.ToString().Contains("West");
                    } else {
                        // Fallback to mathematical Hour Angle if pier side is unknown or null.
                        isPointingEast = (haDeg < -0.1);
                    }

                    // Parallactic Angle (q) calculation
                    // On a polar-aligned equatorial mount, the camera sensor stays aligned with the equatorial coordinate grid.
                    // The field rotation angle of the local horizon relative to the camera sensor is exactly the Parallactic Angle (q).
                    // Zenith is at angle q relative to the equatorial North direction on the sensor.
                    double yQ = Math.Sin(azRad);
                    double xQ = Math.Cos(altRad) * Math.Tan(latRad) - Math.Sin(altRad) * Math.Cos(azRad);
                    double qDeg = Math.Atan2(yQ, xQ) * 180.0 / Math.PI;

                    // Apply the counter-rotation to level the horizon.
                    // We subtract qDeg to rotate the camera image counter-clockwise by qDeg to level the local horizontal plane.
                    totalRotation -= qDeg;

                    // Apply the 180-degree flip if physically pointing East.
                    if (isPointingEast) {
                        totalRotation += 180.0;
                    }

                    // Normalize the angle to [-180, 180] degrees for robust UI rendering.
                    totalRotation = (totalRotation + 180.0) % 360.0;
                    if (totalRotation < 0.0) {
                        totalRotation += 360.0;
                    }
                    totalRotation -= 180.0;
                } catch (Exception ex) {
                    Logger.Error($"[Horizon Studio] Rotation calculation failed: {ex.Message}");
                }
            }

            WebcamImageRotationAngle = totalRotation;
        }

        internal void SetStatus(string text, Brush color) {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => {
                StatusIndicatorText = text;
                StatusIndicatorColor = color;
            }));
        }

        // FIX #11: Log appends to a capped Queue<string> (max MaxLogLines entries).
        // This prevents unbounded memory growth during long mapping sessions.
        public void Log(string message) {
            var formatted = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Logger.Info($"[Horizon Studio] {message}");
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => {
                _logBuffer.Enqueue(formatted);
                if (_logBuffer.Count > MaxLogLines) {
                    _logBuffer.Dequeue();
                }
                Logs = string.Join("\n", _logBuffer);
            }));
        }

        // --- Commands ---

        public ICommand StartMappingCommand => _mappingCommands.StartMappingCommand;
        public ICommand StopMappingCommand => _mappingCommands.StopMappingCommand;
        public ICommand DropPinCommand => _mappingCommands.DropPinCommand;
        public ICommand UndoPinCommand => _mappingCommands.UndoPinCommand;
        public ICommand ClearPinsCommand => _mappingCommands.ClearPinsCommand;
        public ICommand SaveHorizonCommand => _mappingCommands.SaveHorizonCommand;
        public ICommand LoadHorizonCommand => _mappingCommands.LoadHorizonCommand;
        public ICommand DeletePointCommand => _mappingCommands.DeletePointCommand;

        public ICommand PrepareSyncCommand => _mappingCommands.PrepareSyncCommand;
        public ICommand ConfirmSyncCommand => _mappingCommands.ConfirmSyncCommand;
        public ICommand CancelSyncCommand => _mappingCommands.CancelSyncCommand;
        public ICommand AddLandmarkCommand => _mappingCommands.AddLandmarkCommand;
        public ICommand DeleteLandmarkCommand => _mappingCommands.DeleteLandmarkCommand;
        public ICommand SlewToLandmarkCommand => _mappingCommands.SlewToLandmarkCommand;
        public ICommand SelectLandmarkCommand => _mappingCommands.SelectLandmarkCommand;
        public ICommand RenameLandmarkCommand => _mappingCommands.RenameLandmarkCommand;
        public ICommand ClearAllLandmarksCommand => _mappingCommands.ClearAllLandmarksCommand;

        public ICommand SlewCCWCommand => _mappingCommands.SlewCCWCommand;
        public ICommand SlewCWCommand => _mappingCommands.SlewCWCommand;

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

        public ICommand JogN2W1Command => _mountJogCommands.JogN2W1Command;
        public ICommand JogN2E1Command => _mountJogCommands.JogN2E1Command;
        public ICommand JogN1W2Command => _mountJogCommands.JogN1W2Command;
        public ICommand JogN1E2Command => _mountJogCommands.JogN1E2Command;
        public ICommand JogS1W2Command => _mountJogCommands.JogS1W2Command;
        public ICommand JogS1E2Command => _mountJogCommands.JogS1E2Command;
        public ICommand JogS2W1Command => _mountJogCommands.JogS2W1Command;
        public ICommand JogS2E1Command => _mountJogCommands.JogS2E1Command;

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

        private async Task StartMainCameraAsync() {
            if (!IsCameraConnected) {
                Log("[Main Camera] Error: Primary camera not connected in N.I.N.A.");
                return;
            }
            if (IsMainCameraActive) return;

            Log("[Main Camera] Starting looping exposures...");
            IsMainCameraActive = true;
            _mainCameraCTS = new CancellationTokenSource();
            _mainCameraLoopTask = Task.Run(async () => await CaptureMainCameraLoopAsync(_mainCameraCTS.Token), _mainCameraCTS.Token);
            RaisePropertyChanged(nameof(CanStartMainCamera));
            RaisePropertyChanged(nameof(CanStopMainCamera));
        }

        private void StopMainCamera() {
            if (!IsMainCameraActive) return;
            Log("[Main Camera] Stopping looping exposures...");
            try {
                _mainCameraCTS?.Cancel();
            } catch { }

            try {
                _cameraMediator.AbortExposure();
            } catch (Exception ex) {
                Log($"[Main Camera] Warning: Failed to abort camera exposure: {ex.Message}");
            }

            IsMainCameraActive = false;
            LastFrame = null;
            SetStatus("Ready", StatusIdleColor);
            RaisePropertyChanged(nameof(CanStartMainCamera));
            RaisePropertyChanged(nameof(CanStopMainCamera));
        }

        private async Task CaptureMainCameraLoopAsync(CancellationToken token) {
            try {
                while (!token.IsCancellationRequested) {
                    if (!IsCameraConnected) {
                        Log("[Main Camera] Primary camera disconnected. Suspending loop.");
                        SetStatus("Camera Disconnected", StatusWarningColor);
                        await Task.Delay(2000, token);
                        continue;
                    }

                    // Check if camera is free to capture
                    if (!_cameraMediator.IsFreeToCapture(this)) {
                        SetStatus("Waiting for Camera...", StatusWarningColor);
                        await Task.Delay(1000, token);
                        continue;
                    }

                    try {
                        // Register capture block
                        _cameraMediator.RegisterCaptureBlock(this);

                        // Create capture sequence
                        var currentBin = SelectedBinning ?? AvailableBinningModes.FirstOrDefault() ?? new BinningMode((short)1, (short)1);
                        var sequence = new CaptureSequence {
                            ExposureTime = ExposureTime,
                            Gain = Gain,
                            Binning = currentBin,
                            ImageType = "LIGHT",
                            Enabled = true
                        };

                        SetStatus("Exposing...", StatusProgressColor);
                        
                        // Capture image using N.I.N.A. imaging mediator
                        var progress = new Progress<global::NINA.Core.Model.ApplicationStatus>();
                        var exposureData = await _imagingMediator.CaptureImage(sequence, token, progress, "HorizonMapping");

                        if (exposureData != null) {
                            var imageData = await exposureData.ToImageData(progress, token);
                            if (imageData != null) {
                                // Calculate statistics (Mean ADU)
                                var stats = await imageData.Statistics;
                                if (stats != null) {
                                    AverageADU = Math.Round(stats.Mean, 1);

                                    // Apply Auto-Exposure adjustment
                                    if (IsAutoExposureEnabled) {
                                        double mean = stats.Mean;
                                        double currentExp = sequence.ExposureTime;
                                        double newExp = currentExp;

                                        if (mean > 60000) {
                                            newExp = currentExp / 5.0;
                                        } else if (mean < 500) {
                                            newExp = currentExp * 4.0;
                                        } else {
                                            newExp = currentExp * (TargetADU / mean);
                                        }

                                        double minExp = _currentCameraInfo?.ExposureMin ?? _cameraMediator?.GetInfo()?.ExposureMin ?? 0.001;
                                        double maxExp = 5.0; // Daytime safety clamp
                                        newExp = Math.Max(minExp, Math.Min(maxExp, newExp));

                                        // Round to 3 decimal places
                                        ExposureTime = Math.Round(newExp, 3);
                                    }
                                }

                                // Render image for display
                                var rendered = imageData.RenderImage();
                                if (rendered != null) {
                                    _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => {
                                        LastFrame = rendered.Image;
                                    }));

                                    // Run star detection
                                    try {
                                        var updatedRendered = await rendered.DetectStars(false, global::NINA.Core.Enum.StarSensitivityEnum.Normal, global::NINA.Core.Enum.NoiseReductionEnum.Normal, token, null);
                                        var analysis = updatedRendered?.RawImageData?.StarDetectionAnalysis;
                                        if (analysis != null) {
                                            DetectedStarCount = analysis.DetectedStars;
                                            AverageHFR = Math.Round(analysis.HFR, 2);
                                        }
                                    } catch (Exception starEx) {
                                        Logger.Debug($"[Horizon Studio] Star detection skipped/failed: {starEx.Message}");
                                    }
                                }
                            }
                        }
                    } catch (OperationCanceledException) {
                        break;
                    } catch (Exception ex) {
                        Log($"[Main Camera ERROR] Exposure failed: {ex.Message}");
                        SetStatus("Exposure Error", StatusFailureColor);
                        await Task.Delay(2000, token);
                    } finally {
                        try {
                            _cameraMediator.ReleaseCaptureBlock(this);
                        } catch { }
                    }

                    // Short delay between exposures to check for cancellation
                    try {
                        await Task.Delay(500, token);
                    } catch (OperationCanceledException) {
                        break;
                    }
                }
            } finally {
                try {
                    _mainCameraCTS?.Dispose();
                } catch { }
                _mainCameraCTS = null;

                System.Windows.Application.Current.Dispatcher.Invoke(() => {
                    IsMainCameraActive = false;
                    SetStatus("Ready", StatusIdleColor);
                    RaisePropertyChanged(nameof(CanStartMainCamera));
                    RaisePropertyChanged(nameof(CanStopMainCamera));
                });
            }
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
                Logger.Debug($"[Horizon Studio] Frame decoding failed: {ex.Message}");
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

        // FIX #10: Added _disposed guard to prevent double-disposal.
        // Added all missing event unsubscriptions to prevent memory leaks where
        // the GC cannot collect this VM because service objects hold live references
        // back to it via event delegates.
        public void Dispose() {
            if (_disposed) return;
            _disposed = true;

            // Unsubscribe all events before disposing services
            _settingsManager.PropertyChanged -= SettingsManager_PropertyChanged;
            _safetyManager.PropertyChanged -= SafetyManager_PropertyChanged;
            _safetyManager.SafetyLockoutTriggered -= SafetyManager_SafetyLockoutTriggered;
            _webcamService.StateChanged -= WebcamService_StateChanged;

            try { StopMainCamera(); } catch { }
            try { _webcamService?.Dispose(); } catch { }
            try { _mappingCommands?.StopMapping(); } catch { }
            try { _statusTimer?.Stop(); } catch { }
            try { _safetyManager?.Dispose(); } catch { }
            try { _settingsManager?.Dispose(); } catch { }
            try { _cameraMediator.RemoveConsumer(this); } catch { }
            try { _telescopeMediator.RemoveConsumer(this); } catch { }
        }

        public void NotifyPropertyChanged(string propertyName) {
            RaisePropertyChanged(propertyName);
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
