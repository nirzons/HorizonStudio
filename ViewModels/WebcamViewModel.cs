using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NINA.Core.Utility;
using NirZonshine.NINA.HorizonStudio.Services;

namespace NirZonshine.NINA.HorizonStudio.ViewModels {
    public partial class WebcamViewModel : SubViewModelBase {
        private readonly HorizonMapperDockableVM _parent;

        private ImageSource _lastFrame;
        private double _lastFrameWidth = 0;
        private double _lastFrameHeight = 0;
        private DeviceDescriptor _selectedWebcam;
        private WebcamState _currentWebcamState = WebcamState.Disconnected;
        
        private bool _isCoAligning = false;
        private double _webcamImageRotationAngle = 0.0;

        public HorizonMapperDockableVM Parent => _parent;

        public ImageSource LastFrame {
            get => _lastFrame;
            set {
                _lastFrame = value;
                RaisePropertyChanged(nameof(LastFrame));

                if (value is BitmapSource bmp) {
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
                    _parent.SettingsManager.SelectedUvcCamera = _selectedWebcam?.DevicePath ?? string.Empty;
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
                                      (VisualFeedSource == "MainCamera" && _parent.Camera.IsMainCameraActive);

        public bool CanStartWebcam => SelectedWebcam != null && (CurrentWebcamState == WebcamState.Disconnected || CurrentWebcamState == WebcamState.Error) && VisualFeedSource == "Webcam";
        public bool CanStopWebcam => (CurrentWebcamState == WebcamState.Streaming || CurrentWebcamState == WebcamState.Connecting) && VisualFeedSource == "Webcam";

        public string VisualFeedSource {
            get => _parent.SettingsManager.VisualFeedSource;
            set {
                if (_parent.SettingsManager.VisualFeedSource != value) {
                    _parent.SettingsManager.VisualFeedSource = value;
                    RaisePropertyChanged(nameof(VisualFeedSource));
                    RaisePropertyChanged(nameof(IsWebcamFeedSelected));
                    RaisePropertyChanged(nameof(IsMainCameraFeedSelected));
                    RaisePropertyChanged(nameof(IsWebcamActive));
                    RaisePropertyChanged(nameof(CanStartWebcam));
                    RaisePropertyChanged(nameof(CanStopWebcam));
                    _parent.Camera.NotifyVisualFeedSourceChanged();
                    
                    if (value == "MainCamera") {
                        StopWebcam();
                    } else {
                        _parent.Camera.StopMainCamera();
                    }
                }
            }
        }

        public bool IsWebcamFeedSelected => VisualFeedSource == "Webcam";
        public bool IsMainCameraFeedSelected => VisualFeedSource == "MainCamera";

        public Brush WebcamStatusIndicatorColor {
            get {
                switch (CurrentWebcamState) {
                    case WebcamState.Streaming: return HorizonMapperDockableVM.StatusSuccessColor;
                    case WebcamState.Connecting: return HorizonMapperDockableVM.StatusProgressColor;
                    case WebcamState.Error: return HorizonMapperDockableVM.StatusFailureColor;
                    default: return HorizonMapperDockableVM.StatusIdleColor;
                }
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
            get => _parent.SettingsManager.IsCoAligned;
            set {
                _parent.SettingsManager.IsCoAligned = value;
                RaisePropertyChanged(nameof(IsCoAligned));
                RaisePropertyChanged(nameof(AlignmentCenterPoint));
                RaisePropertyChanged(nameof(AlignmentTranslationX));
                RaisePropertyChanged(nameof(AlignmentTranslationY));
            }
        }

        public double AlignmentCenterX {
            get => _parent.SettingsManager.AlignmentCenterX;
            set {
                _parent.SettingsManager.AlignmentCenterX = value;
                RaisePropertyChanged(nameof(AlignmentCenterX));
                RaisePropertyChanged(nameof(AlignmentCenterPoint));
                RaisePropertyChanged(nameof(AlignmentTranslationX));
                RaisePropertyChanged(nameof(AlignmentTranslationY));
            }
        }

        public double AlignmentCenterY {
            get => _parent.SettingsManager.AlignmentCenterY;
            set {
                _parent.SettingsManager.AlignmentCenterY = value;
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
                // Main camera is always the true optical center — co-alignment only applies to USB webcam
                if (IsMainCameraFeedSelected) return 0.0;
                if (IsCoAligning || !IsCoAligned || LastFrame == null) return 0.0;
                double imgWidth = 0;
                double imgHeight = 0;
                if (LastFrame is BitmapSource bmp) {
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
                // Main camera is always the true optical center — co-alignment only applies to USB webcam
                if (IsMainCameraFeedSelected) return 0.0;
                if (IsCoAligning || !IsCoAligned || LastFrame == null) return 0.0;
                double imgWidth = 0;
                double imgHeight = 0;
                if (LastFrame is BitmapSource bmp) {
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
            get => _parent.SettingsManager.IsCounterRotationEnabled;
            set {
                _parent.SettingsManager.IsCounterRotationEnabled = value;
                RaisePropertyChanged(nameof(IsCounterRotationEnabled));
                UpdateRotationAngle();
            }
        }

        public double CameraRotationOffset {
            get => _parent.SettingsManager.CameraRotationOffset;
            set {
                _parent.SettingsManager.CameraRotationOffset = value;
                RaisePropertyChanged(nameof(CameraRotationOffset));
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

        public ICommand StartWebcamCommand { get; }
        public ICommand StopWebcamCommand { get; }
        public ICommand RefreshWebcamsCommand { get; }

        public ICommand StartCoAlignmentCommand { get; }
        public ICommand SaveCoAlignmentCommand { get; }
        public ICommand ResetCoAlignmentCommand { get; }

        public WebcamViewModel(HorizonMapperDockableVM parent) {
            _parent = parent ?? throw new ArgumentNullException(nameof(parent));

            StartWebcamCommand = new RelayCommand(async _ => await StartWebcamAsync(), _ => CanStartWebcam);
            StopWebcamCommand = new RelayCommand(_ => StopWebcam(), _ => CanStopWebcam);
            RefreshWebcamsCommand = new RelayCommand(_ => RefreshWebcams());

            StartCoAlignmentCommand = new RelayCommand(_ => StartCoAlignment());
            SaveCoAlignmentCommand = new RelayCommand(_ => SaveCoAlignment());
            ResetCoAlignmentCommand = new RelayCommand(_ => ResetCoAlignment());
        }

        public async Task StartWebcamAsync() {
            if (SelectedWebcam == null) return;
            _parent.Log($"[System] Connecting to webcam: {SelectedWebcam.Name}...");
            try {
                await _parent.WebcamService.StartCaptureAsync(SelectedWebcam.DevicePath, OnFrameCaptured);
            } catch (Exception ex) {
                _parent.Log($"[ERROR] Failed to start webcam capture: {ex.Message}");
            }
        }

        public void StopWebcam() {
            _parent.Log("[System] Stopping webcam stream...");
            _parent.WebcamService.StopCapture();
            LastFrame = null;
        }

        public void RefreshWebcams() {
            AvailableWebcams.Clear();
            var cameras = _parent.WebcamService.GetAvailableCameras();
            foreach (var camera in cameras) {
                AvailableWebcams.Add(camera);
            }
            _parent.Log($"[System] Discovered {AvailableWebcams.Count} available webcam(s).");

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
                    bitmap.Freeze();

                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => {
                        LastFrame = bitmap;
                    }));
                }
            } catch (Exception ex) {
                Logger.Debug($"[Horizon Studio] Frame decoding failed: {ex.Message}");
            }
        }

        public void OnWebcamStateChanged(WebcamState state) {
            CurrentWebcamState = state;
            if (state == WebcamState.Streaming) {
                _parent.Log("[System] Webcam stream active.");
            } else if (state == WebcamState.Disconnected) {
                _parent.Log("[System] Webcam disconnected.");
            } else if (state == WebcamState.Error) {
                _parent.Log("[ERROR] Webcam connection failed or device was unplugged.");
                LastFrame = null;
            }
        }



        public void HandleImageClick(double x, double y, double frameWidth, double frameHeight) {
            if (!IsWebcamActive) return;

            if (IsCoAligning) {
                if (frameWidth <= 0 || frameHeight <= 0) return;
                AlignmentCenterX = x / frameWidth;
                AlignmentCenterY = y / frameHeight;
                _parent.Log($"[Co-Alignment] Click registered: ({x:F1}, {y:F1}) -> Ratio: ({AlignmentCenterX:F3}, {AlignmentCenterY:F3})");
            }
        }
    }
}
