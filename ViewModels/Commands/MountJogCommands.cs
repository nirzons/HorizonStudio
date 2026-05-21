using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Profile.Interfaces;
using NirZonshine.NINA.HorizonVisualMapper.Services;

namespace NirZonshine.NINA.HorizonVisualMapper.ViewModels.Commands {
    public class MountJogCommands {
        private readonly HorizonMapperDockableVM _vm;
        private readonly ITelescopeMediator _telescopeMediator;
        private readonly SafetyManager _safetyManager;
        private readonly IProfileService _profileService;

        public ICommand JogNorthCommand { get; }
        public ICommand JogSouthCommand { get; }
        public ICommand JogEastCommand { get; }
        public ICommand JogWestCommand { get; }

        public MountJogCommands(HorizonMapperDockableVM vm, ITelescopeMediator telescopeMediator, SafetyManager safetyManager, IProfileService profileService) {
            _vm = vm;
            _telescopeMediator = telescopeMediator;
            _safetyManager = safetyManager;
            _profileService = profileService;

            JogNorthCommand = new RelayCommand(o => SlewJog(_vm.StepSizeManual, 0));
            JogSouthCommand = new RelayCommand(o => SlewJog(-_vm.StepSizeManual, 0));
            JogEastCommand = new RelayCommand(o => SlewJog(0, _vm.StepSizeManual));
            JogWestCommand = new RelayCommand(o => SlewJog(0, -_vm.StepSizeManual));
        }

        private void SlewJog(double altOffset, double azOffset) {
            if (!_vm.IsRunning) {
                _vm.Log("[Error] Jogging blocked: Click 'Start Mapping' to initiate the session.");
                return;
            }

            if (!_vm.IsMountConnected) {
                _vm.Log("[Error] Jogging blocked: Telescope mount is not connected.");
                return;
            }

            Task.Run(async () => {
                try {
                    double currentAlt = _vm.CurrentAlt;
                    double currentAz = _vm.CurrentAz;

                    double targetAlt = currentAlt + altOffset;
                    double targetAz = (currentAz + azOffset + 360.0) % 360.0;

                    // 1. Verify safety limits
                    if (!_safetyManager.IsTargetPositionSafe(targetAlt, targetAz, out string violation)) {
                        _vm.Log($"[SAFETY BLOCK] Jog rejected: {violation}");
                        return;
                    }

                    // 2. Perform backlash compensated slew
                    _vm.Log($"Jogging mount by Alt: {altOffset:F2}°, Az: {azOffset:F2}° -> Target Alt: {targetAlt:F2}°, Az: {targetAz:F2}°");
                    
                    double lat = _telescopeMediator.GetInfo()?.SiteLatitude ?? _profileService?.ActiveProfile?.AstrometrySettings?.Latitude ?? 0.0;
                    double lon = _telescopeMediator.GetInfo()?.SiteLongitude ?? _profileService?.ActiveProfile?.AstrometrySettings?.Longitude ?? 0.0;

                    var topo = new global::NINA.Astrometry.TopocentricCoordinates(
                        global::NINA.Astrometry.Angle.ByDegree(targetAz),
                        global::NINA.Astrometry.Angle.ByDegree(targetAlt),
                        global::NINA.Astrometry.Angle.ByDegree(lat),
                        global::NINA.Astrometry.Angle.ByDegree(lon)
                    );

                    await _telescopeMediator.SlewToCoordinatesAsync(topo, CancellationToken.None);

                    // 3. Immediately disable sidereal tracking so the mount remains stationary at the target Alt/Az
                    try {
                        _telescopeMediator.SetTrackingEnabled(false);
                        _vm.Log($"Slew completed. Mount tracking disabled to maintain position.");
                    } catch (Exception ex) {
                        _vm.Log($"[Warning] Failed to disable tracking after jog: {ex.Message}");
                    }
                } catch (Exception ex) {
                    _vm.Log($"[Error] Slew Jog failed: {ex.Message}");
                }
            });
        }
    }
}
