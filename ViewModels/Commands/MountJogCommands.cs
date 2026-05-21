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
        
        public ICommand JogNorthEastCommand { get; }
        public ICommand JogNorthWestCommand { get; }
        public ICommand JogSouthEastCommand { get; }
        public ICommand JogSouthWestCommand { get; }
        
        public ICommand HomeMountCommand { get; }

        public MountJogCommands(HorizonMapperDockableVM vm, ITelescopeMediator telescopeMediator, SafetyManager safetyManager, IProfileService profileService) {
            _vm = vm;
            _telescopeMediator = telescopeMediator;
            _safetyManager = safetyManager;
            _profileService = profileService;

            JogNorthCommand = new RelayCommand(o => SlewJog(_vm.StepSizeAlt, 0));
            JogSouthCommand = new RelayCommand(o => SlewJog(-_vm.StepSizeAlt, 0));
            JogEastCommand = new RelayCommand(o => SlewJog(0, _vm.StepSizeAz));
            JogWestCommand = new RelayCommand(o => SlewJog(0, -_vm.StepSizeAz));

            JogNorthEastCommand = new RelayCommand(o => SlewJog(_vm.StepSizeAlt, _vm.StepSizeAz));
            JogNorthWestCommand = new RelayCommand(o => SlewJog(_vm.StepSizeAlt, -_vm.StepSizeAz));
            JogSouthEastCommand = new RelayCommand(o => SlewJog(-_vm.StepSizeAlt, _vm.StepSizeAz));
            JogSouthWestCommand = new RelayCommand(o => SlewJog(-_vm.StepSizeAlt, -_vm.StepSizeAz));

            HomeMountCommand = new RelayCommand(o => HomeMount());
        }

        private void HomeMount() {
            if (!_vm.IsMountConnected) {
                _vm.Log("[Error] Cannot home: Telescope mount is not connected.");
                return;
            }
            
            Task.Run(async () => {
                try {
                    _vm.Log("Locating mount home position...");
                    await _telescopeMediator.FindHome(null, CancellationToken.None);
                    _vm.Log("Mount home sequence initiated.");
                } catch (Exception ex) {
                    _vm.Log($"[Error] FindHome failed: {ex.Message}");
                }
            });
        }

        private void SlewJog(double altOffset, double azOffset) {
            if (!_vm.IsMountConnected) {
                _vm.Log("[Error] Jogging blocked: Telescope mount is not connected.");
                return;
            }

            double currentAlt = _vm.CurrentAlt;
            double currentAz = _vm.CurrentAz;

            double targetAlt = currentAlt + altOffset;
            double targetAz = (currentAz + azOffset + 360.0) % 360.0;

            // 1. Verify safety limits
            if (!_safetyManager.IsTargetPositionSafe(targetAlt, targetAz, out string violation)) {
                _vm.Log($"[SAFETY BLOCK] Jog rejected: {violation}");
                global::NINA.Core.Utility.Notification.Notification.ShowError(
                    $"Safety Lockout: {violation}\nDisable 'Solar Safety Zone' to bypass.");
                return;
            }

            Task.Run(async () => {
                try {
                    // 2. Perform slew
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
                    _vm.Log("Slew completed.");
                } catch (Exception ex) {
                    _vm.Log($"[Error] Slew Jog failed: {ex.Message}");
                }
            });
        }
    }
}
