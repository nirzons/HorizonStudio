using System;
using System.Linq;
using System.Threading;
using System.Windows.Input;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Profile.Interfaces;
using NirZonshine.NINA.HorizonStudio.Domain;

namespace NirZonshine.NINA.HorizonStudio.ViewModels.Commands {
    public class LandmarkCommands {
        private readonly HorizonMapperDockableVM _vm;
        private readonly ITelescopeMediator _telescopeMediator;
        private readonly IProfileService _profileService;

        public ICommand AddLandmarkCommand { get; }
        public ICommand DeleteLandmarkCommand { get; }
        public ICommand SlewToLandmarkCommand { get; }
        public ICommand SelectLandmarkCommand { get; }
        public ICommand RenameLandmarkCommand { get; }
        public ICommand ClearAllLandmarksCommand { get; }

        public LandmarkCommands(HorizonMapperDockableVM vm, ITelescopeMediator telescopeMediator, IProfileService profileService) {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            _telescopeMediator = telescopeMediator ?? throw new ArgumentNullException(nameof(telescopeMediator));
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));

            AddLandmarkCommand = new RelayCommand(o => AddLandmark(), o => _vm.IsMountConnected);
            DeleteLandmarkCommand = new RelayCommand(o => DeleteLandmark(), o => _vm.SelectedLandmark != null);
            SlewToLandmarkCommand = new RelayCommand(o => SlewToLandmark(), o => _vm.SelectedLandmark != null && _vm.IsMountConnected && !_vm.IsSlewing && !_vm.IsActionSlewing);
            SelectLandmarkCommand = new RelayCommand(o => SelectLandmark(o as SyncLandmark), o => o is SyncLandmark && !_vm.IsSyncPreparing);
            RenameLandmarkCommand = new RelayCommand(o => RenameLandmark(o as string), o => _vm.SelectedLandmark != null);
            ClearAllLandmarksCommand = new RelayCommand(o => ClearAllLandmarks(), o => _vm.HasLandmarks);
        }

        public void AddLandmark() {
            if (!_vm.IsMountConnected) {
                _vm.Log("[Error] Cannot add landmark: Mount is not connected.");
                return;
            }
            double az = _vm.CurrentAz;
            double alt = _vm.CurrentAlt;

            int count = _vm.SyncLandmarks.Count + 1;
            string name = $"Landmark {count}";
            while (_vm.SyncLandmarks.Any(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase))) {
                count++;
                name = $"Landmark {count}";
            }

            var landmark = new SyncLandmark(name, az, alt);
            _vm.SyncLandmarks.Add(landmark);
            _vm.SelectedLandmark = landmark;
            _vm.NotifyPropertyChanged("HasLandmarks");
            _vm.Landmark?.NotifyLandmarksCollectionChanged();
            _vm.Log($"[Landmarks] Added landmark '{name}' at Az: {az:F2}°, Alt: {alt:F2}°");
        }

        public void DeleteLandmark() {
            if (_vm.SelectedLandmark == null) return;
            var name = _vm.SelectedLandmark.Name;
            _vm.SyncLandmarks.Remove(_vm.SelectedLandmark);
            _vm.SelectedLandmark = null;
            _vm.NotifyPropertyChanged("HasLandmarks");
            _vm.Landmark?.NotifyLandmarksCollectionChanged();
            _vm.Log($"[Landmarks] Removed landmark '{name}'.");
        }

        public void SlewToLandmark() {
            if (_vm.SelectedLandmark == null) return;
            if (!_vm.IsMountConnected) {
                _vm.Log("[Error] Slew blocked: Mount is not connected.");
                return;
            }
            if (_telescopeMediator.GetInfo()?.Slewing == true || _vm.IsActionSlewing) {
                _vm.Log("[Error] Slew blocked: Mount is currently slewing.");
                return;
            }

            double targetAlt = _vm.SelectedLandmark.Altitude;
            double targetAz = _vm.SelectedLandmark.Azimuth;

            try {
                if (_telescopeMediator.GetInfo()?.Connected == true) {
                    _telescopeMediator.SetTrackingEnabled(false);
                    _vm.Log("Sidereal tracking automatically suspended for landmark slew.");
                }
            } catch (Exception ex) {
                _vm.Log($"[Warning] Failed to auto-disable tracking: {ex.Message}");
            }

            _vm.LastRequestedAlt = targetAlt;
            _vm.LastRequestedAz = targetAz;
            _vm.IsActionSlewing = true;

            System.Threading.Tasks.Task.Run(async () => {
                try {
                    _vm.Log($"[Landmark Slew] Slewing mount to landmark '{_vm.SelectedLandmark?.Name}' - Alt: {targetAlt:F2}°, Az: {targetAz:F2}°");

                    double lat = _telescopeMediator.GetInfo()?.SiteLatitude ?? _profileService?.ActiveProfile?.AstrometrySettings?.Latitude ?? 0.0;
                    double lon = _telescopeMediator.GetInfo()?.SiteLongitude ?? _profileService?.ActiveProfile?.AstrometrySettings?.Longitude ?? 0.0;

                    var topo = new global::NINA.Astrometry.TopocentricCoordinates(
                        global::NINA.Astrometry.Angle.ByDegree(targetAz),
                        global::NINA.Astrometry.Angle.ByDegree(targetAlt),
                        global::NINA.Astrometry.Angle.ByDegree(lat),
                        global::NINA.Astrometry.Angle.ByDegree(lon)
                    );

                    DateTime startTime = DateTime.UtcNow;
                    await _telescopeMediator.SlewToCoordinatesAsync(topo, CancellationToken.None);
                    DateTime endTime = DateTime.UtcNow;

                    if (_vm.IsExactPositionEnabled) {
                        double errorAlt = _vm.CurrentAlt - targetAlt;
                        double errorAz = _vm.CurrentAz - targetAz;

                        if (errorAz > 180.0) errorAz -= 360.0;
                        if (errorAz < -180.0) errorAz += 360.0;

                        if (Math.Abs(errorAlt) > 0.01 || Math.Abs(errorAz) > 0.01) {
                            double slewSeconds = (endTime - startTime).TotalSeconds;
                            if (slewSeconds < 1.0) slewSeconds = 1.0;

                            double rateAlt = errorAlt / slewSeconds;
                            double rateAz = errorAz / slewSeconds;

                            double predictedAlt = targetAlt - (rateAlt * 8.0);
                            double predictedAz = (targetAz - (rateAz * 8.0) + 360.0) % 360.0;

                            _vm.Log($"[Exact Position] Drift error detected. Applied Predictive Lead. Initiating Micro-Jump...");

                            var microTopo = new global::NINA.Astrometry.TopocentricCoordinates(
                                global::NINA.Astrometry.Angle.ByDegree(predictedAz),
                                global::NINA.Astrometry.Angle.ByDegree(predictedAlt),
                                global::NINA.Astrometry.Angle.ByDegree(lat),
                                global::NINA.Astrometry.Angle.ByDegree(lon)
                            );

                            await _telescopeMediator.SlewToCoordinatesAsync(microTopo, CancellationToken.None);
                        }
                    }

                    _vm.Log("Slew to landmark completed.");
                    _telescopeMediator.SetTrackingEnabled(false);
                } catch (Exception ex) {
                    _vm.Log($"[Error] Slew to landmark failed: {ex.Message}");
                } finally {
                    System.Windows.Application.Current.Dispatcher.Invoke(() => {
                        _vm.IsActionSlewing = false;
                    });
                }
            });
        }

        public void SelectLandmark(SyncLandmark landmark) {
            if (landmark == null) return;
            _vm.SelectedLandmark = landmark;
            _vm.Log($"[Landmarks] Selected landmark '{landmark.Name}' - Alt: {landmark.Altitude:F2}°, Az: {landmark.Azimuth:F2}°");
        }

        public void RenameLandmark(string newName = null) {
            if (_vm.SelectedLandmark == null) return;
            string oldName = _vm.SelectedLandmark.Name;

            if (string.IsNullOrEmpty(newName)) {
                newName = RenameDialog.Show(oldName, "Rename Landmark");
            }

            if (!string.IsNullOrEmpty(newName) && !string.Equals(oldName, newName, StringComparison.Ordinal)) {
                if (_vm.SyncLandmarks.Any(l => l != _vm.SelectedLandmark && string.Equals(l.Name, newName, StringComparison.OrdinalIgnoreCase))) {
                    System.Windows.MessageBox.Show($"A landmark with the name '{newName}' already exists.", "Duplicate Name", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }
                _vm.SelectedLandmark.Name = newName;
                _vm.Log($"[Landmarks] Renamed landmark '{oldName}' to '{newName}'.");
            }
        }

        public void ClearAllLandmarks() {
            if (_vm.SyncLandmarks.Count == 0) return;
            _vm.SyncLandmarks.Clear();
            _vm.SelectedLandmark = null;
            _vm.NotifyPropertyChanged("HasLandmarks");
            _vm.Landmark?.NotifyLandmarksCollectionChanged();
            _vm.Log("[Landmarks] Cleared all landmarks.");
        }
    }
}
