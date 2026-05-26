using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;
using System.Threading;
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
using System.Linq;
using NirZonshine.NINA.HorizonStudio.Domain;
using NirZonshine.NINA.HorizonStudio.Services;
using NavigationCommandsAlias = NirZonshine.NINA.HorizonStudio.ViewModels.Commands.NavigationCommands;
using NirZonshine.NINA.HorizonStudio.ViewModels.Commands;

namespace NirZonshine.NINA.HorizonStudio.ViewModels {

    public interface IImageClickHandler {
        void HandleImageClick(double x, double y, double frameWidth, double frameHeight);
    }

    public interface IRadarClickHandler {
        void HandleRadarClick(double x, double y);
        bool IsNearHorizon(double canvasX, double canvasY);
    }
    [Export(typeof(global::NINA.Equipment.Interfaces.ViewModel.IDockableVM))]
    public partial class HorizonMapperDockableVM : DockableVM, ICameraConsumer, ITelescopeConsumer, IImageClickHandler, IRadarClickHandler {
        internal readonly IProfileService ProfileService;
        internal readonly ICameraMediator CameraMediator;
        internal readonly ITelescopeMediator TelescopeMediator;
        internal readonly IImagingMediator ImagingMediator;
        private readonly System.Windows.Threading.DispatcherTimer _statusTimer;

        internal readonly SettingsManager SettingsManager;
        internal readonly SafetyManager SafetyManager;
        internal readonly IWebcamService WebcamService;

        private bool _disposed = false;
        internal int TaskExecutingFlag = 0;
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
        private bool _lastIsSlewing;
        private HorizonNode _syncRefNode = null;
        private bool _isSyncPreparing = false;
        private bool _isRunning = true;
        private bool _isActionSlewing = false;
        internal static readonly Brush StatusIdleColor = CreateFrozenBrush("#72BDFF");
        internal static readonly Brush StatusWarningColor = CreateFrozenBrush("#FBBF24");
        internal static readonly Brush StatusSuccessColor = CreateFrozenBrush("#22C55E");
        internal static readonly Brush StatusFailureColor = CreateFrozenBrush("#EF4444");
        internal static readonly Brush StatusProgressColor = CreateFrozenBrush("#6366F1");
        private FileCommands _fileCommands;
        private LandmarkCommands _landmarkCommands;
        private SyncCommands _syncCommands;
        private NavigationCommandsAlias _navigationCommands;
        private MountJogCommands _mountJogCommands;

        public CameraViewModel Camera { get; }
        public WebcamViewModel Webcam { get; }
        public RadarViewModel Radar { get; }
        public LandmarkViewModel Landmark { get; }

        public ObservableCollection<HorizonNode> HorizonNodes { get; } = new ObservableCollection<HorizonNode>();
        public ObservableCollection<SyncLandmark> SyncLandmarks { get; } = new ObservableCollection<SyncLandmark>();
        public Stack<HorizonNode> PinHistory { get; } = new Stack<HorizonNode>();

        [ImportingConstructor]
        public HorizonMapperDockableVM(IProfileService profileService, ICameraMediator cameraMediator, ITelescopeMediator telescopeMediator, IImagingMediator imagingMediator) : base(profileService) {
            ProfileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            CameraMediator = cameraMediator ?? throw new ArgumentNullException(nameof(cameraMediator));
            TelescopeMediator = telescopeMediator ?? throw new ArgumentNullException(nameof(telescopeMediator));
            ImagingMediator = imagingMediator ?? throw new ArgumentNullException(nameof(imagingMediator));

            CameraMediator.RegisterConsumer(this);
            TelescopeMediator.RegisterConsumer(this);

            Title = "Horizon Studio";

            SettingsManager = new SettingsManager(ProfileService);
            SettingsManager.PropertyChanged += SettingsManager_PropertyChanged;

            SafetyManager = new SafetyManager(ProfileService, TelescopeMediator, SettingsManager);
            SafetyManager.PropertyChanged += SafetyManager_PropertyChanged;
            SafetyManager.SafetyLockoutTriggered += SafetyManager_SafetyLockoutTriggered;

            _lastIsCameraConnected = IsCameraConnected;
            _lastIsMountConnected = IsMountConnected;
            _lastIsSlewing = IsSlewing;

            _statusTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _statusTimer.Tick += StatusTimer_Tick;
            _statusTimer.Start();

            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse("M2,14 C2,6 6,2 14,2"));
            group.Children.Add(Geometry.Parse("M14,14 L12,11 L10,14 Z"));
            group.Children.Add(Geometry.Parse("M6,14 C6,10 8,8 10,14"));
            group.Freeze();
            ImageGeometry = group;

            HorizonNodes.CollectionChanged += (s, e) => {
                Radar?.NotifyHorizonNodesChanged();
            };

            Camera = new CameraViewModel(this);
            Webcam = new WebcamViewModel(this);
            Radar = new RadarViewModel(this);
            Landmark = new LandmarkViewModel(this);

            _fileCommands = new FileCommands(this, ProfileService);
            _landmarkCommands = new LandmarkCommands(this, TelescopeMediator, ProfileService);
            _syncCommands = new SyncCommands(this, TelescopeMediator);
            _navigationCommands = new NavigationCommandsAlias(this, TelescopeMediator, ProfileService);
            _mountJogCommands = new MountJogCommands(this, TelescopeMediator, SafetyManager, ProfileService);

            WebcamService = new WebcamService();
            WebcamService.StateChanged += WebcamService_StateChanged;

            Webcam.RefreshWebcams();
            var savedPath = SettingsManager.SelectedUvcCamera;
            if (!string.IsNullOrEmpty(savedPath)) {
                Webcam.SelectedWebcam = Webcam.AvailableWebcams.FirstOrDefault(w => string.Equals(w.DevicePath, savedPath, StringComparison.OrdinalIgnoreCase));
            }
            if (Webcam.SelectedWebcam == null && Webcam.AvailableWebcams.Count > 0) {
                Webcam.SelectedWebcam = Webcam.AvailableWebcams[0];
            }
        }

        public void UpdateDeviceInfo(CameraInfo deviceInfo) {
            _currentCameraInfo = deviceInfo;
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
                RaisePropertyChanged(nameof(IsCameraConnected));
                Camera?.NotifyParentPropertiesChanged();
                if (deviceInfo != null && Camera != null) {
                    var savedBin = SettingsManager.Binning;
                    Camera.SelectedBinning = Camera.AvailableBinningModes.FirstOrDefault(b => string.Equals(b.Name, savedBin, StringComparison.OrdinalIgnoreCase))
                                      ?? Camera.AvailableBinningModes.FirstOrDefault();
                }
            });
        }

        public void UpdateDeviceInfo(TelescopeInfo deviceInfo) {
            _currentTelescopeInfo = deviceInfo;
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
                RaisePropertyChanged(nameof(IsMountConnected));
                RaisePropertyChanged(nameof(IsSlewing));
                Camera?.NotifyParentPropertiesChanged();
                Radar?.NotifyParentPropertiesChanged();
                Landmark?.NotifyParentPropertiesChanged();
                if (IsMountConnected && deviceInfo != null) {
                    CurrentAlt = deviceInfo.Altitude;
                    CurrentAz = deviceInfo.Azimuth;
                    Webcam?.UpdateRotationAngle();
                }
            });
        }

        public override bool IsTool => true;



        public bool IsRunning {
            get => _isRunning;
            set {
                _isRunning = value;
                RaisePropertyChanged(nameof(IsRunning));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public double CurrentAlt {
            get => _currentAlt;
            set {
                _currentAlt = value;
                RaisePropertyChanged(nameof(CurrentAlt));
                Radar?.NotifyParentPropertiesChanged();
                if (IsSyncPreparing) {
                    Landmark?.NotifyParentPropertiesChanged();
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
                _currentAz = (_currentAz % 360.0 + 360.0) % 360.0;
                RaisePropertyChanged(nameof(CurrentAz));
                Radar?.NotifyParentPropertiesChanged();
                if (IsSyncPreparing) {
                    Landmark?.NotifyParentPropertiesChanged();
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
            set { _syncRefNode = value; RaisePropertyChanged(nameof(SyncRefNode)); Landmark?.NotifyParentPropertiesChanged(); }
        }

        public bool IsSyncPreparing {
            get => _isSyncPreparing;
            set { _isSyncPreparing = value; RaisePropertyChanged(nameof(IsSyncPreparing)); Landmark?.NotifyParentPropertiesChanged(); Radar?.NotifyParentPropertiesChanged(); }
        }

        public SyncLandmark SelectedLandmark {
            get => Landmark?.SelectedLandmark;
            set { if (Landmark != null) Landmark.SelectedLandmark = value; }
        }

        public bool IsLandmarkSelected => Landmark?.IsLandmarkSelected ?? false;
        public bool HasLandmarks => SyncLandmarks.Count > 0;

        public bool CanPrepareSync => Landmark?.CanPrepareSync ?? false;
        public bool CanConfirmSync => Landmark?.CanConfirmSync ?? false;

        public string SyncInstructionText => Landmark?.SyncInstructionText ?? string.Empty;

        public double? LastRequestedAlt { get; set; }
        public double? LastRequestedAz { get; set; }

        private int _activeNodeIndex = -1;
        public int ActiveNodeIndex {
            get => _activeNodeIndex;
            set {
                if (_activeNodeIndex != value) {
                    _activeNodeIndex = value;
                    if (_activeNodeIndex >= 0) {
                        SelectedLandmark = null;
                    }
                    RaisePropertyChanged(nameof(ActiveNodeIndex));
                    RaisePropertyChanged(nameof(ActiveNode));
                    RaisePropertyChanged(nameof(HasActiveNode));
                    Radar?.NotifyActiveNodeChanged();
                    Landmark?.NotifyParentPropertiesChanged();
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

        public bool IsActionSlewing {
            get => _isActionSlewing;
            set {
                if (_isActionSlewing != value) {
                    _isActionSlewing = value;
                    RaisePropertyChanged(nameof(IsActionSlewing));
                    Radar?.NotifyParentPropertiesChanged();
                    Landmark?.NotifyParentPropertiesChanged();
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public int VerificationStepSize {
            get => SettingsManager.VerificationStepSize;
            set { SettingsManager.VerificationStepSize = value; RaisePropertyChanged(nameof(VerificationStepSize)); }
        }

        public List<int> VerificationStepSizes => Radar?.VerificationStepSizes;

        public bool CanVerifyPoints => Radar?.CanVerifyPoints ?? false;
        public bool CanJog => Radar?.CanJog ?? false;

        public bool IsExactPositionEnabled {
            get => SettingsManager.IsExactPositionEnabled;
            set {
                SettingsManager.IsExactPositionEnabled = value;
                RaisePropertyChanged(nameof(IsExactPositionEnabled));
            }
        }


        public void NotifyLandmarkSelectionChanged() {
            RaisePropertyChanged(nameof(SelectedLandmark));
            RaisePropertyChanged(nameof(IsLandmarkSelected));
            RaisePropertyChanged(nameof(ActiveNode));
            RaisePropertyChanged(nameof(HasActiveNode));
            Radar?.NotifyActiveNodeChanged();
            Landmark?.NotifyParentPropertiesChanged();
        }

        public void ClearPins() {
            _navigationCommands?.ClearPins();
        }

        public double GetInterpolatedAltitude(double azimuth) {
            return Radar?.GetInterpolatedAltitude(azimuth) ?? 0.0;
        }

        public void HandleImageClick(double x, double y, double frameWidth, double frameHeight) {
            Webcam?.HandleImageClick(x, y, frameWidth, frameHeight);
        }

        public void HandleRadarClick(double x, double y) {
            _navigationCommands?.RadarClickSlew(x, y);
        }

        public bool IsNearHorizon(double canvasX, double canvasY) {
            return Radar?.IsNearHorizon(canvasX, canvasY) ?? false;
        }

        public void NotifyPropertyChanged(string propertyName) {
            RaisePropertyChanged(propertyName);
        }
        public void Dispose() {
            if (_disposed) return;
            _disposed = true;

            SettingsManager.PropertyChanged -= SettingsManager_PropertyChanged;
            SafetyManager.PropertyChanged -= SafetyManager_PropertyChanged;
            SafetyManager.SafetyLockoutTriggered -= SafetyManager_SafetyLockoutTriggered;
            WebcamService.StateChanged -= WebcamService_StateChanged;
            try { Camera?.StopMainCamera(); } catch { }
            try { Webcam?.StopWebcam(); } catch { }
            try { WebcamService?.Dispose(); } catch { }
            try { _navigationCommands?.StopMapping(); } catch { }
            try { _statusTimer?.Stop(); } catch { }
            try { (_statusTimer as IDisposable)?.Dispose(); } catch { }
            try { SafetyManager?.Dispose(); } catch { }
            try { SettingsManager?.Dispose(); } catch { }
            try { CameraMediator.RemoveConsumer(this); } catch { }
            try { TelescopeMediator.RemoveConsumer(this); } catch { }
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
