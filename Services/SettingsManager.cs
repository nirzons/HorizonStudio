using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NINA.Profile;
using NINA.Profile.Interfaces;
using NINA.Core.Utility;

namespace NirZonshine.NINA.HorizonStudio.Services {
    public class SettingsManager : INotifyPropertyChanged, IDisposable {
        private bool _disposed;
        private readonly IProfileService _profileService;
        private readonly Guid _pluginGuid = Guid.Parse("ef99cb7e-3c22-491c-b26a-54315222bf9b");

        // FIX #13: Cache a single accessor instance instead of creating one on every SaveSetting call.
        // A new accessor is created after a profile change since the underlying profile object may differ.
        private PluginOptionsAccessor _accessor;

        public event PropertyChangedEventHandler PropertyChanged;

        // Default Values
        private double _exposureTime = 0.5;
        private int _gain = 0;
        private string _binning = "1x1";
        private double _focalLengthOverride = 0.0;
        private double _safetyThreshold = 15.0;
        private double _stepSizeAlt = 1.0;
        private double _stepSizeAz = 1.0;
        private string _selectedUvcCamera = string.Empty;
        private string _activeHorizonFilePath = string.Empty;
        private bool _enableSolarSafety = true;
        private bool _enableZenithSafety = true;
        private bool _horizonLockEnabled = true;
        private string _calibrationDataJson = string.Empty;
        private double _alignmentCenterX = 0.5;
        private double _alignmentCenterY = 0.5;
        private bool _isCoAligned = false;
        private bool _isCounterRotationEnabled = false;
        private double _cameraRotationOffset = 0.0;
        private bool _isExactPositionEnabled = false;

        public SettingsManager(IProfileService profileService) {
            _profileService = profileService;
            if (_profileService != null) {
                _accessor = new PluginOptionsAccessor(_profileService, _pluginGuid);
                _profileService.ProfileChanged += ProfileService_ProfileChanged;
            }
            LoadSettings();
        }

        // FIX #8: Use InvokeAsync (non-blocking) with a null-check to avoid hangs during
        // plugin teardown when Application.Current may be null.
        private void ProfileService_ProfileChanged(object sender, EventArgs e) {
            var app = System.Windows.Application.Current;
            if (app == null) {
                // Application is shutting down; run inline
                RefreshAccessorAndLoad();
                return;
            }
            app.Dispatcher.InvokeAsync(RefreshAccessorAndLoad);
        }

        private void RefreshAccessorAndLoad() {
            // FIX #13: Recreate the accessor after a profile switch so it points at the new profile.
            if (_profileService != null) {
                _accessor = new PluginOptionsAccessor(_profileService, _pluginGuid);
            }
            LoadSettings();
        }

        public double ExposureTime {
            get => _exposureTime;
            set { if (_exposureTime != value) { _exposureTime = value; SaveSetting(nameof(ExposureTime), value); OnPropertyChanged(); } }
        }

        public int Gain {
            get => _gain;
            set { if (_gain != value) { _gain = value; SaveSetting(nameof(Gain), value); OnPropertyChanged(); } }
        }

        public string Binning {
            get => _binning;
            set { if (_binning != value) { _binning = value; SaveSetting(nameof(Binning), value); OnPropertyChanged(); } }
        }

        public double FocalLengthOverride {
            get => _focalLengthOverride;
            set { if (_focalLengthOverride != value) { _focalLengthOverride = value; SaveSetting(nameof(FocalLengthOverride), value); OnPropertyChanged(); } }
        }

        public double SafetyThreshold {
            get => _safetyThreshold;
            set { if (_safetyThreshold != value) { _safetyThreshold = value; SaveSetting(nameof(SafetyThreshold), value); OnPropertyChanged(); } }
        }

        public double StepSizeAlt {
            get => _stepSizeAlt;
            set { if (_stepSizeAlt != value) { _stepSizeAlt = value; SaveSetting(nameof(StepSizeAlt), value); OnPropertyChanged(); } }
        }

        public double StepSizeAz {
            get => _stepSizeAz;
            set { if (_stepSizeAz != value) { _stepSizeAz = value; SaveSetting(nameof(StepSizeAz), value); OnPropertyChanged(); } }
        }

        public string SelectedUvcCamera {
            get => _selectedUvcCamera;
            set { if (_selectedUvcCamera != value) { _selectedUvcCamera = value; SaveSetting(nameof(SelectedUvcCamera), value); OnPropertyChanged(); } }
        }

        public string ActiveHorizonFilePath {
            get => _activeHorizonFilePath;
            set { if (_activeHorizonFilePath != value) { _activeHorizonFilePath = value; SaveSetting(nameof(ActiveHorizonFilePath), value); OnPropertyChanged(); } }
        }

        public bool EnableSolarSafety {
            get => _enableSolarSafety;
            set { if (_enableSolarSafety != value) { _enableSolarSafety = value; SaveSetting(nameof(EnableSolarSafety), value); OnPropertyChanged(); } }
        }

        public bool EnableZenithSafety {
            get => _enableZenithSafety;
            set { if (_enableZenithSafety != value) { _enableZenithSafety = value; SaveSetting(nameof(EnableZenithSafety), value); OnPropertyChanged(); } }
        }

        public bool HorizonLockEnabled {
            get => _horizonLockEnabled;
            set { if (_horizonLockEnabled != value) { _horizonLockEnabled = value; SaveSetting(nameof(HorizonLockEnabled), value); OnPropertyChanged(); } }
        }

        public string CalibrationDataJson {
            get => _calibrationDataJson;
            set { if (_calibrationDataJson != value) { _calibrationDataJson = value; SaveSetting(nameof(CalibrationDataJson), value); OnPropertyChanged(); } }
        }

        public double AlignmentCenterX {
            get => _alignmentCenterX;
            set { if (_alignmentCenterX != value) { _alignmentCenterX = value; SaveSetting(nameof(AlignmentCenterX), value); OnPropertyChanged(); } }
        }

        public double AlignmentCenterY {
            get => _alignmentCenterY;
            set { if (_alignmentCenterY != value) { _alignmentCenterY = value; SaveSetting(nameof(AlignmentCenterY), value); OnPropertyChanged(); } }
        }

        public bool IsCoAligned {
            get => _isCoAligned;
            set { if (_isCoAligned != value) { _isCoAligned = value; SaveSetting(nameof(IsCoAligned), value); OnPropertyChanged(); } }
        }

        public bool IsCounterRotationEnabled {
            get => _isCounterRotationEnabled;
            set { if (_isCounterRotationEnabled != value) { _isCounterRotationEnabled = value; SaveSetting(nameof(IsCounterRotationEnabled), value); OnPropertyChanged(); } }
        }

        public double CameraRotationOffset {
            get => _cameraRotationOffset;
            set { if (_cameraRotationOffset != value) { _cameraRotationOffset = value; SaveSetting(nameof(CameraRotationOffset), value); OnPropertyChanged(); } }
        }

        public bool IsExactPositionEnabled {
            get => _isExactPositionEnabled;
            set { if (_isExactPositionEnabled != value) { _isExactPositionEnabled = value; SaveSetting(nameof(IsExactPositionEnabled), value); OnPropertyChanged(); } }
        }

        private void LoadSettings() {
            try {
                if (_accessor == null) return;

                _exposureTime = _accessor.GetValueDouble(nameof(ExposureTime), 0.5);
                _gain = _accessor.GetValueInt32(nameof(Gain), 0);
                _binning = _accessor.GetValueString(nameof(Binning), "1x1");
                _focalLengthOverride = _accessor.GetValueDouble(nameof(FocalLengthOverride), 0.0);
                _safetyThreshold = _accessor.GetValueDouble(nameof(SafetyThreshold), 15.0);
                _stepSizeAlt = _accessor.GetValueDouble(nameof(StepSizeAlt), 1.0);
                _stepSizeAz = _accessor.GetValueDouble(nameof(StepSizeAz), 1.0);
                _selectedUvcCamera = _accessor.GetValueString(nameof(SelectedUvcCamera), string.Empty);
                _activeHorizonFilePath = _accessor.GetValueString(nameof(ActiveHorizonFilePath), string.Empty);
                _enableSolarSafety = _accessor.GetValueBoolean(nameof(EnableSolarSafety), true);
                _enableZenithSafety = _accessor.GetValueBoolean(nameof(EnableZenithSafety), true);
                _horizonLockEnabled = _accessor.GetValueBoolean(nameof(HorizonLockEnabled), true);
                _calibrationDataJson = _accessor.GetValueString(nameof(CalibrationDataJson), string.Empty);
                _alignmentCenterX = _accessor.GetValueDouble(nameof(AlignmentCenterX), 0.5);
                _alignmentCenterY = _accessor.GetValueDouble(nameof(AlignmentCenterY), 0.5);
                _isCoAligned = _accessor.GetValueBoolean(nameof(IsCoAligned), false);
                _isCounterRotationEnabled = _accessor.GetValueBoolean(nameof(IsCounterRotationEnabled), false);
                _cameraRotationOffset = _accessor.GetValueDouble(nameof(CameraRotationOffset), 0.0);
                _isExactPositionEnabled = _accessor.GetValueBoolean(nameof(IsExactPositionEnabled), false);

                OnPropertyChanged(string.Empty);
            } catch (Exception ex) {
                Logger.Error($"[Horizon Studio] Failed to load settings: {ex.Message}");
            }
        }

        // FIX #13: Uses the cached _accessor instead of constructing a new one per call.
        private void SaveSetting(string key, object value) {
            try {
                if (_accessor == null) return;

                if (value is double d) _accessor.SetValueDouble(key, d);
                else if (value is int i) _accessor.SetValueInt32(key, i);
                else if (value is bool b) _accessor.SetValueBoolean(key, b);
                else if (value is string s) _accessor.SetValueString(key, s);
            } catch (Exception ex) {
                Logger.Error($"[Horizon Studio] Failed to save setting '{key}': {ex.Message}");
            }
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            if (_profileService != null) {
                _profileService.ProfileChanged -= ProfileService_ProfileChanged;
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
