using System.Collections.Generic;
using System.Linq;
using NINA.Core.Model.Equipment;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Model;

namespace NirZonshine.NINA.HorizonStudio.ViewModels {
    public partial class HorizonMapperDockableVM {
        public bool IsCameraConnected => (_currentCameraInfo?.Connected ?? CameraMediator?.GetInfo()?.Connected ?? false);
        public bool IsMountConnected => (_currentTelescopeInfo?.Connected ?? TelescopeMediator?.GetInfo()?.Connected ?? false);
        public bool IsSlewing => (_currentTelescopeInfo?.Slewing ?? TelescopeMediator?.GetInfo()?.Slewing ?? false);

        public bool HasMechanicalShutter => _currentCameraInfo?.HasShutter ?? CameraMediator?.GetInfo()?.HasShutter ?? false;
        public IEnumerable<BinningMode> AvailableBinningModes => _currentCameraInfo?.BinningModes ?? CameraMediator?.GetInfo()?.BinningModes ?? Enumerable.Empty<BinningMode>();
        public CameraInfo CameraInfo => _currentCameraInfo;
        public TelescopeInfo TelescopeInfo => _currentTelescopeInfo;

        public double ExposureTime {
            get => SettingsManager.ExposureTime;
            set => SettingsManager.ExposureTime = value;
        }

        public int Gain {
            get => SettingsManager.Gain;
            set => SettingsManager.Gain = value;
        }

        public string Binning {
            get => SettingsManager.Binning;
            set => SettingsManager.Binning = value;
        }

        public double FocalLengthOverride {
            get => SettingsManager.FocalLengthOverride;
            set => SettingsManager.FocalLengthOverride = value;
        }

        public double SafetyThreshold {
            get => SettingsManager.SafetyThreshold;
            set => SettingsManager.SafetyThreshold = value;
        }

        public double StepSizeAlt {
            get => SettingsManager.StepSizeAlt;
            set { SettingsManager.StepSizeAlt = value; OnPropertyChanged(); }
        }

        public double StepSizeAz {
            get => SettingsManager.StepSizeAz;
            set { SettingsManager.StepSizeAz = value; OnPropertyChanged(); }
        }

        public bool EnableSolarSafety {
            get => SettingsManager.EnableSolarSafety;
            set => SettingsManager.EnableSolarSafety = value;
        }

        public bool EnableZenithSafety {
            get => SettingsManager.EnableZenithSafety;
            set => SettingsManager.EnableZenithSafety = value;
        }

        public bool HorizonLockEnabled {
            get => SettingsManager.HorizonLockEnabled;
            set => SettingsManager.HorizonLockEnabled = value;
        }

        public bool IsRadarOverlayEnabled {
            get => SettingsManager.IsRadarOverlayEnabled;
            set => SettingsManager.IsRadarOverlayEnabled = value;
        }

        public bool IsSolarSafetyAlert => SafetyManager.IsSolarSafetyAlert;
        public bool IsZenithSafetyAlert => SafetyManager.IsZenithSafetyAlert;
        public string SafetyMessage => SafetyManager.SafetyMessage;
    }
}
