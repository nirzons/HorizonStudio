using System;
using System.Collections.ObjectModel;
using NirZonshine.NINA.HorizonStudio.Domain;

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
                double dist = GetAngularDistance(_parent.CurrentAz, _parent.CurrentAlt, _parent.SyncRefNode.Azimuth, _parent.SyncRefNode.Altitude);
                return dist >= 0.05;
            }
        }

        public string SyncInstructionText {
            get {
                if (_parent.SyncRefNode == null) return "⚠️ Sync Mode: Jog mount to center landmark in feed, then click Confirm.";
                double dist = GetAngularDistance(_parent.CurrentAz, _parent.CurrentAlt, _parent.SyncRefNode.Azimuth, _parent.SyncRefNode.Altitude);
                if (dist < 0.05) {
                    return "⚠️ Sync Mode: Jog mount to center landmark in feed (Confirm will enable once mount has moved).";
                }
                return "⚠️ Sync Mode: Landmark centered in feed. Click Confirm Sync to warp profile.";
            }
        }

        public LandmarkViewModel(HorizonMapperDockableVM parent) {
            _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        }

        public void NotifyParentPropertiesChanged() {
            RaisePropertyChanged(nameof(CanPrepareSync));
            RaisePropertyChanged(nameof(CanConfirmSync));
            RaisePropertyChanged(nameof(SyncInstructionText));
        }

        public void NotifyLandmarksCollectionChanged() {
            RaisePropertyChanged(nameof(HasLandmarks));
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
    }
}
