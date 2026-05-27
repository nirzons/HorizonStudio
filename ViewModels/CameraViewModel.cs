using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Model;
using NirZonshine.NINA.HorizonStudio.Services;

namespace NirZonshine.NINA.HorizonStudio.ViewModels {
    public class CameraViewModel : SubViewModelBase {
        private readonly HorizonMapperDockableVM _parent;
        
        private bool _isMainCameraActive = false;
        private int _detectedStarCount = 0;
        private double _averageHFR = 0.0;
        private double _averageADU = 0.0;
        private CancellationTokenSource _mainCameraCTS = null;
        private Task _mainCameraLoopTask = null;
        private BinningMode _selectedBinning = null;

        public HorizonMapperDockableVM Parent => _parent;

        public bool IsMainCameraActive {
            get => _isMainCameraActive;
            private set {
                if (_isMainCameraActive != value) {
                    _isMainCameraActive = value;
                    RaisePropertyChanged(nameof(IsMainCameraActive));
                    _parent.Webcam?.RaisePropertyChanged(nameof(WebcamViewModel.IsWebcamActive));
                    RaisePropertyChanged(nameof(CanStartMainCamera));
                    RaisePropertyChanged(nameof(CanStopMainCamera));
                }
            }
        }

        public bool IsAutoExposureEnabled {
            get => _parent.SettingsManager.IsAutoExposureEnabled;
            set {
                if (_parent.SettingsManager.IsAutoExposureEnabled != value) {
                    _parent.SettingsManager.IsAutoExposureEnabled = value;
                    RaisePropertyChanged(nameof(IsAutoExposureEnabled));
                }
            }
        }

        public double TargetADU {
            get => _parent.SettingsManager.TargetADU;
            set {
                if (_parent.SettingsManager.TargetADU != value) {
                    _parent.SettingsManager.TargetADU = value;
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

        public bool HasMechanicalShutter => _parent.HasMechanicalShutter;

        public IEnumerable<BinningMode> AvailableBinningModes => _parent.AvailableBinningModes;

        public BinningMode SelectedBinning {
            get => _selectedBinning;
            set {
                if (_selectedBinning != value) {
                    _selectedBinning = value;
                    RaisePropertyChanged(nameof(SelectedBinning));
                    if (value != null) {
                        _parent.Binning = value.Name;
                    }
                }
            }
        }

        public bool CanStartMainCamera => _parent.IsCameraConnected && !IsMainCameraActive && _parent.Webcam?.VisualFeedSource == "MainCamera";
        public bool CanStopMainCamera => IsMainCameraActive && _parent.Webcam?.VisualFeedSource == "MainCamera";

        public ICommand StartMainCameraCommand { get; }
        public ICommand StopMainCameraCommand { get; }
        public ICommand SelectVisualFeedSourceCommand { get; }

        public CameraViewModel(HorizonMapperDockableVM parent) {
            _parent = parent ?? throw new ArgumentNullException(nameof(parent));

            StartMainCameraCommand = new RelayCommand(async _ => await StartMainCameraAsync(), _ => CanStartMainCamera);
            StopMainCameraCommand = new RelayCommand(_ => StopMainCamera(), _ => CanStopMainCamera);
            SelectVisualFeedSourceCommand = new RelayCommand(p => {
                if (_parent.Webcam != null) {
                    _parent.Webcam.VisualFeedSource = p?.ToString();
                }
            });
        }

        public void NotifyParentPropertiesChanged() {
            RaisePropertyChanged(nameof(HasMechanicalShutter));
            RaisePropertyChanged(nameof(AvailableBinningModes));
            RaisePropertyChanged(nameof(CanStartMainCamera));
            RaisePropertyChanged(nameof(CanStopMainCamera));
        }

        public void NotifyVisualFeedSourceChanged() {
            RaisePropertyChanged(nameof(CanStartMainCamera));
            RaisePropertyChanged(nameof(CanStopMainCamera));
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }

        public async Task StartMainCameraAsync() {
            if (!_parent.IsCameraConnected) {
                _parent.Log("[Main Camera] Error: Primary camera not connected in N.I.N.A.");
                return;
            }
            if (IsMainCameraActive) return;

            _parent.Log("[Main Camera] Starting looping exposures...");
            IsMainCameraActive = true;
            _mainCameraCTS = new CancellationTokenSource();
            _mainCameraLoopTask = Task.Run(async () => await CaptureMainCameraLoopAsync(_mainCameraCTS.Token), _mainCameraCTS.Token);
            RaisePropertyChanged(nameof(CanStartMainCamera));
            RaisePropertyChanged(nameof(CanStopMainCamera));
        }

        public void StopMainCamera() {
            if (!IsMainCameraActive) return;
            _parent.Log("[Main Camera] Stopping looping exposures...");
            try {
                _mainCameraCTS?.Cancel();
            } catch { }

            try {
                _parent.CameraMediator.AbortExposure();
            } catch (Exception ex) {
                _parent.Log($"[Main Camera] Warning: Failed to abort camera exposure: {ex.Message}");
            }

            IsMainCameraActive = false;
            if (_parent.Webcam != null) {
                _parent.Webcam.LastFrame = null;
            }
            _parent.SetStatus("Ready", HorizonMapperDockableVM.StatusIdleColor);
            RaisePropertyChanged(nameof(CanStartMainCamera));
            RaisePropertyChanged(nameof(CanStopMainCamera));
        }

        private async Task CaptureMainCameraLoopAsync(CancellationToken token) {
            try {
                while (!token.IsCancellationRequested) {
                    if (!_parent.IsCameraConnected) {
                        _parent.Log("[Main Camera] Primary camera disconnected. Suspending loop.");
                        _parent.SetStatus("Camera Disconnected", HorizonMapperDockableVM.StatusWarningColor);
                        await Task.Delay(2000, token);
                        continue;
                    }

                    if (!_parent.CameraMediator.IsFreeToCapture(_parent)) {
                        _parent.SetStatus("Waiting for Camera...", HorizonMapperDockableVM.StatusWarningColor);
                        await Task.Delay(1000, token);
                        continue;
                    }

                    try {
                        _parent.CameraMediator.RegisterCaptureBlock(_parent);

                        var currentBin = SelectedBinning ?? AvailableBinningModes.FirstOrDefault() ?? new BinningMode((short)1, (short)1);
                        var sequence = new CaptureSequence {
                            ExposureTime = _parent.ExposureTime,
                            Gain = _parent.Gain,
                            Binning = currentBin,
                            ImageType = "LIGHT",
                            Enabled = true
                        };

                        _parent.SetStatus("Exposing...", HorizonMapperDockableVM.StatusProgressColor);
                        
                        var progress = new Progress<global::NINA.Core.Model.ApplicationStatus>();
                        var exposureData = await _parent.ImagingMediator.CaptureImage(sequence, token, progress, "HorizonMapping");

                        if (exposureData != null) {
                            var imageData = await exposureData.ToImageData(progress, token);
                            if (imageData != null) {
                                var stats = await imageData.Statistics;
                                if (stats != null) {
                                    AverageADU = Math.Round(stats.Mean, 1);

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

                                        double minExp = _parent.CameraInfo?.ExposureMin ?? _parent.CameraMediator?.GetInfo()?.ExposureMin ?? 0.001;
                                        double maxExp = 5.0;
                                        newExp = Math.Max(minExp, Math.Min(maxExp, newExp));

                                        _parent.ExposureTime = Math.Round(newExp, 3);
                                    }
                                }

                                var rendered = imageData.RenderImage();
                                if (rendered != null) {
                                    ThreadHelper.RunOnUI(() => {
                                        if (_parent.Webcam != null) {
                                            _parent.Webcam.LastFrame = rendered.Image;
                                        }
                                    });

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
                        _parent.Log($"[Main Camera ERROR] Exposure failed: {ex.Message}");
                        _parent.SetStatus("Exposure Error", HorizonMapperDockableVM.StatusFailureColor);
                        await Task.Delay(2000, token);
                    } finally {
                        try {
                            _parent.CameraMediator.ReleaseCaptureBlock(_parent);
                        } catch { }
                    }

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

                ThreadHelper.RunOnUI(() => {
                    IsMainCameraActive = false;
                    _parent.SetStatus("Ready", HorizonMapperDockableVM.StatusIdleColor);
                    RaisePropertyChanged(nameof(CanStartMainCamera));
                    RaisePropertyChanged(nameof(CanStopMainCamera));
                });
            }
        }
    }
}
