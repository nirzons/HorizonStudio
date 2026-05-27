using System;
using System.Collections.ObjectModel;
using NirZonshine.NINA.HorizonStudio.Domain;
using NirZonshine.NINA.HorizonStudio.Services;

namespace NirZonshine.NINA.HorizonStudio.ViewModels {
    public class LandmarkViewModel : SubViewModelBase {
        private readonly HorizonMapperDockableVM _parent;
        private SyncLandmark _selectedLandmark = null;

        public HorizonMapperDockableVM Parent => _parent;

        public ObservableCollection<SyncLandmark> SyncLandmarks => _parent.SyncLandmarks;

        public SyncLandmark SelectedLandmark {
            get => _selectedLandmark;
            set {
                if (_selectedLandmark != value) {
                    if (_parent != null && _parent.IsSyncPreparing) {
                        RaisePropertyChanged(nameof(SelectedLandmark));
                        return;
                    }
                    if (_selectedLandmark != null) {
                        _selectedLandmark.IsSelected = false;
                    }
                    _selectedLandmark = value;
                    if (_selectedLandmark != null) {
                        _selectedLandmark.IsSelected = true;
                        _parent.ActiveNodeIndex = -1;
                    }
                    RaisePropertyChanged(nameof(SelectedLandmark));
                    RaisePropertyChanged(nameof(IsLandmarkSelected));
                    _parent.NotifyLandmarkSelectionChanged();
                }
            }
        }

        public bool IsLandmarkSelected => SelectedLandmark != null;
        public bool HasLandmarks => SyncLandmarks.Count > 0;

        public bool CanPrepareSync => _parent.HasActiveNode && !_parent.IsSyncPreparing && _parent.IsMountConnected;

        public bool CanConfirmSync {
            get {
                if (!_parent.IsSyncPreparing || !_parent.IsMountConnected || _parent.IsSlewing || _parent.IsActionSlewing || _parent.SyncRefNode == null) {
                    return false;
                }
                double dist = AstronomyHelper.GetAngularDistance(_parent.CurrentAz, _parent.CurrentAlt, _parent.SyncRefNode.Azimuth, _parent.SyncRefNode.Altitude);
                return dist >= 0.05;
            }
        }

        public string SyncBannerBackground {
            get {
                if (_parent.IsSyncPreparing && _parent.SyncRefNode != null) {
                    double dist = AstronomyHelper.GetAngularDistance(_parent.CurrentAz, _parent.CurrentAlt, _parent.SyncRefNode.Azimuth, _parent.SyncRefNode.Altitude);
                    if (dist > 5.0) return "#3D1C1C"; // Alarming Red
                }
                return "#1E1B4B"; // Standard Deep Indigo
            }
        }

        public string SyncBannerBorderBrush {
            get {
                if (_parent.IsSyncPreparing && _parent.SyncRefNode != null) {
                    double dist = AstronomyHelper.GetAngularDistance(_parent.CurrentAz, _parent.CurrentAlt, _parent.SyncRefNode.Azimuth, _parent.SyncRefNode.Altitude);
                    if (dist > 5.0) return "#EF4444"; // Alarming Bright Red
                }
                return "#4338CA"; // Standard Indigo
            }
        }

        public string SyncBannerForeground {
            get {
                if (_parent.IsSyncPreparing && _parent.SyncRefNode != null) {
                    double dist = AstronomyHelper.GetAngularDistance(_parent.CurrentAz, _parent.CurrentAlt, _parent.SyncRefNode.Azimuth, _parent.SyncRefNode.Altitude);
                    if (dist > 5.0) return "#FFE0E0"; // Light Red/White
                }
                return "#FFE0E7FF"; // Standard White/Indigo
            }
        }

        public string SyncInstructionText {
            get {
                if (_parent.SyncRefNode == null) return "⚠️ Sync Mode: Jog mount to center the landmark on the webcam crosshairs, then click Confirm.";
                double dist = AstronomyHelper.GetAngularDistance(_parent.CurrentAz, _parent.CurrentAlt, _parent.SyncRefNode.Azimuth, _parent.SyncRefNode.Altitude);
                if (dist < 0.05) {
                    return "⚠️ Sync Mode: Jog mount to center the landmark on the webcam crosshairs (Confirm will enable once mount has moved).";
                }
                if (dist > 5.0) {
                    return $"🚨 DANGER: Position offset ({dist:F2}°) exceeds safe limit of 5.0°! Check if the correct landmark is centered.";
                }
                return "⚠️ Sync Mode: Landmark centered on webcam crosshairs. Click Confirm Sync to warp profile.";
            }
        }

        public LandmarkViewModel(HorizonMapperDockableVM parent) {
            _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        }

        public void NotifyParentPropertiesChanged() {
            RaisePropertyChanged(nameof(CanPrepareSync));
            RaisePropertyChanged(nameof(CanConfirmSync));
            RaisePropertyChanged(nameof(SyncInstructionText));
            RaisePropertyChanged(nameof(SyncBannerBackground));
            RaisePropertyChanged(nameof(SyncBannerBorderBrush));
            RaisePropertyChanged(nameof(SyncBannerForeground));
        }

        public void NotifyLandmarksCollectionChanged() {
            RaisePropertyChanged(nameof(HasLandmarks));
        }
    }
}
