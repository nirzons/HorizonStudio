using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NINA.Profile;
using NINA.Profile.Interfaces;
using NINA.Core.Utility;

namespace NirZonshine.NINA.HorizonVisualMapper.Services {
    public class SettingsManager : INotifyPropertyChanged, IDisposable {
        private bool _disposed;
        private readonly IProfileService _profileService;
        private readonly Guid _pluginGuid = Guid.Parse("ef99cb7e-3c22-491c-b26a-54315222bf9b");

        public event PropertyChangedEventHandler PropertyChanged;

        // Default Values
        private double _exposureTime = 0.5;
        private int _gain = 0;
        private string _binning = "1x1";
        private double _focalLengthOverride = 0.0;
        private double _safetyThreshold = 15.0;
        private double _stepSizeManual = 1.0;
        private string _selectedUvcCamera = string.Empty;
        private string _activeHorizonFilePath = string.Empty;
        private bool _enableSolarSafety = true;
        private bool _enableZenithSafety = true;
        private double _backlashCompensationAmount = 0.05;
        private bool _horizonLockEnabled = true;
        private string _calibrationDataJson = string.Empty;

        public SettingsManager(IProfileService profileService) {
            _profileService = profileService;
            if (_profileService != null) {
                _profileService.ProfileChanged += ProfileService_ProfileChanged;
            }
            LoadSettings();
        }

        private void ProfileService_ProfileChanged(object sender, EventArgs e) {
            System.Windows.Application.Current.Dispatcher.Invoke(() => {
                LoadSettings();
            });
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

        public double StepSizeManual {
            get => _stepSizeManual;
            set { if (_stepSizeManual != value) { _stepSizeManual = value; SaveSetting(nameof(StepSizeManual), value); OnPropertyChanged(); } }
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

        public double BacklashCompensationAmount {
            get => _backlashCompensationAmount;
            set { if (_backlashCompensationAmount != value) { _backlashCompensationAmount = value; SaveSetting(nameof(BacklashCompensationAmount), value); OnPropertyChanged(); } }
        }

        public bool HorizonLockEnabled {
            get => _horizonLockEnabled;
            set { if (_horizonLockEnabled != value) { _horizonLockEnabled = value; SaveSetting(nameof(HorizonLockEnabled), value); OnPropertyChanged(); } }
        }

        public string CalibrationDataJson {
            get => _calibrationDataJson;
            set { if (_calibrationDataJson != value) { _calibrationDataJson = value; SaveSetting(nameof(CalibrationDataJson), value); OnPropertyChanged(); } }
        }

        private void LoadSettings() {
            try {
                if (_profileService == null) return;
                var accessor = new PluginOptionsAccessor(_profileService, _pluginGuid);

                _exposureTime = accessor.GetValueDouble(nameof(ExposureTime), 0.5);
                _gain = accessor.GetValueInt32(nameof(Gain), 0);
                _binning = accessor.GetValueString(nameof(Binning), "1x1");
                _focalLengthOverride = accessor.GetValueDouble(nameof(FocalLengthOverride), 0.0);
                _safetyThreshold = accessor.GetValueDouble(nameof(SafetyThreshold), 15.0);
                _stepSizeManual = accessor.GetValueDouble(nameof(StepSizeManual), 1.0);
                _selectedUvcCamera = accessor.GetValueString(nameof(SelectedUvcCamera), string.Empty);
                _activeHorizonFilePath = accessor.GetValueString(nameof(ActiveHorizonFilePath), string.Empty);
                _enableSolarSafety = accessor.GetValueBoolean(nameof(EnableSolarSafety), true);
                _enableZenithSafety = accessor.GetValueBoolean(nameof(EnableZenithSafety), true);
                _backlashCompensationAmount = accessor.GetValueDouble(nameof(BacklashCompensationAmount), 0.05);
                _horizonLockEnabled = accessor.GetValueBoolean(nameof(HorizonLockEnabled), true);
                _calibrationDataJson = accessor.GetValueString(nameof(CalibrationDataJson), string.Empty);

                OnPropertyChanged(string.Empty);
            } catch (Exception ex) {
                Logger.Error($"[Horizon Visual Mapper] Failed to load settings: {ex.Message}");
            }
        }

        private void SaveSetting(string key, object value) {
            try {
                if (_profileService == null) return;
                var accessor = new PluginOptionsAccessor(_profileService, _pluginGuid);
                
                if (value is double d) accessor.SetValueDouble(key, d);
                else if (value is int i) accessor.SetValueInt32(key, i);
                else if (value is bool b) accessor.SetValueBoolean(key, b);
                else if (value is string s) accessor.SetValueString(key, s);
            } catch (Exception ex) {
                Logger.Error($"[Horizon Visual Mapper] Failed to save setting '{key}': {ex.Message}");
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
