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

        public ICommand DoubleJogNorthCommand { get; }
        public ICommand DoubleJogSouthCommand { get; }
        public ICommand DoubleJogEastCommand { get; }
        public ICommand DoubleJogWestCommand { get; }
        
        public ICommand DoubleJogNorthEastCommand { get; }
        public ICommand DoubleJogNorthWestCommand { get; }
        public ICommand DoubleJogSouthEastCommand { get; }
        public ICommand DoubleJogSouthWestCommand { get; }
        
        public ICommand StopMountCommand { get; }

        public ICommand JogN2W1Command { get; }
        public ICommand JogN2E1Command { get; }
        public ICommand JogN1W2Command { get; }
        public ICommand JogN1E2Command { get; }
        public ICommand JogS1W2Command { get; }
        public ICommand JogS1E2Command { get; }
        public ICommand JogS2W1Command { get; }
        public ICommand JogS2E1Command { get; }

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

            DoubleJogNorthCommand = new RelayCommand(o => SlewJog(_vm.StepSizeAlt * 2.0, 0));
            DoubleJogSouthCommand = new RelayCommand(o => SlewJog(-_vm.StepSizeAlt * 2.0, 0));
            DoubleJogEastCommand = new RelayCommand(o => SlewJog(0, _vm.StepSizeAz * 2.0));
            DoubleJogWestCommand = new RelayCommand(o => SlewJog(0, -_vm.StepSizeAz * 2.0));

            DoubleJogNorthEastCommand = new RelayCommand(o => SlewJog(_vm.StepSizeAlt * 2.0, _vm.StepSizeAz * 2.0));
            DoubleJogNorthWestCommand = new RelayCommand(o => SlewJog(_vm.StepSizeAlt * 2.0, -_vm.StepSizeAz * 2.0));
            DoubleJogSouthEastCommand = new RelayCommand(o => SlewJog(-_vm.StepSizeAlt * 2.0, _vm.StepSizeAz * 2.0));
            DoubleJogSouthWestCommand = new RelayCommand(o => SlewJog(-_vm.StepSizeAlt * 2.0, -_vm.StepSizeAz * 2.0));

            JogN2W1Command = new RelayCommand(o => SlewJog(_vm.StepSizeAlt * 2.0, -_vm.StepSizeAz));
            JogN2E1Command = new RelayCommand(o => SlewJog(_vm.StepSizeAlt * 2.0, _vm.StepSizeAz));
            JogN1W2Command = new RelayCommand(o => SlewJog(_vm.StepSizeAlt, -_vm.StepSizeAz * 2.0));
            JogN1E2Command = new RelayCommand(o => SlewJog(_vm.StepSizeAlt, _vm.StepSizeAz * 2.0));
            JogS1W2Command = new RelayCommand(o => SlewJog(-_vm.StepSizeAlt, -_vm.StepSizeAz * 2.0));
            JogS1E2Command = new RelayCommand(o => SlewJog(-_vm.StepSizeAlt, _vm.StepSizeAz * 2.0));
            JogS2W1Command = new RelayCommand(o => SlewJog(-_vm.StepSizeAlt * 2.0, -_vm.StepSizeAz));
            JogS2E1Command = new RelayCommand(o => SlewJog(-_vm.StepSizeAlt * 2.0, _vm.StepSizeAz));

            HomeMountCommand = new RelayCommand(o => HomeMount());
            StopMountCommand = new RelayCommand(o => StopMount());
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

            double targetAlt;
            double targetAz;

            if (_vm.IsExactPositionEnabled && _vm.LastRequestedAlt.HasValue && _vm.LastRequestedAz.HasValue) {
                targetAlt = _vm.LastRequestedAlt.Value + altOffset;
                targetAz = (_vm.LastRequestedAz.Value + azOffset + 360.0) % 360.0;
            } else {
                targetAlt = _vm.CurrentAlt + altOffset;
                targetAz = (_vm.CurrentAz + azOffset + 360.0) % 360.0;
            }

            // Always update LastRequested
            _vm.LastRequestedAlt = targetAlt;
            _vm.LastRequestedAz = targetAz;

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

                    DateTime startTime = DateTime.UtcNow;
                    await _telescopeMediator.SlewToCoordinatesAsync(topo, CancellationToken.None);
                    DateTime endTime = DateTime.UtcNow;
                    
                    // 3. Exact Position Micro-Jump
                    if (_vm.IsExactPositionEnabled) {
                        double errorAlt = _vm.CurrentAlt - targetAlt;
                        double errorAz = _vm.CurrentAz - targetAz;
                        
                        // Handle azimuth wrap-around error calculation (signed)
                        if (errorAz > 180.0) errorAz -= 360.0;
                        if (errorAz < -180.0) errorAz += 360.0;

                        if (Math.Abs(errorAlt) > 0.01 || Math.Abs(errorAz) > 0.01) {
                            double slewSeconds = (endTime - startTime).TotalSeconds;
                            if (slewSeconds < 1.0) slewSeconds = 1.0; // Prevent div/0 anomalies

                            // Calculate drift degrees per second
                            double rateAlt = errorAlt / slewSeconds;
                            double rateAz = errorAz / slewSeconds;

                            // Predict target to cancel out 8.0s of settle-time drift
                            double predictedAlt = targetAlt - (rateAlt * 8.0);
                            double predictedAz = (targetAz - (rateAz * 8.0) + 360.0) % 360.0;

                            _vm.Log($"[Exact Position] Drift error detected (Alt Error: {errorAlt:F3}°, Az Error: {errorAz:F3}°). Applied Predictive Lead. Initiating Micro-Jump...");
                            
                            var microTopo = new global::NINA.Astrometry.TopocentricCoordinates(
                                global::NINA.Astrometry.Angle.ByDegree(predictedAz),
                                global::NINA.Astrometry.Angle.ByDegree(predictedAlt),
                                global::NINA.Astrometry.Angle.ByDegree(lat),
                                global::NINA.Astrometry.Angle.ByDegree(lon)
                            );

                            await _telescopeMediator.SlewToCoordinatesAsync(microTopo, CancellationToken.None);
                        }
                    }

                    _vm.Log("Slew completed.");
                    
                    // NINA/ASCOM often automatically re-enables tracking after an equatorial slew.
                    // Since the user is doing an Alt-Az jog via our panel, they intend to stay at that Alt-Az.
                    // We must force tracking OFF so the mount doesn't drift away equatorially.
                    _telescopeMediator.SetTrackingEnabled(false);
                } catch (Exception ex) {
                    _vm.Log($"[Error] Slew Jog failed: {ex.Message}");
                }
            });
        }

        private void StopMount() {
            if (!_vm.IsMountConnected) return;
            Task.Run(() => {
                try {
                    _vm.Log("Stopping telescope movement...");
                    _telescopeMediator.StopSlew();
                    _vm.Log("Telescope motion halted successfully.");
                } catch (Exception ex) {
                    _vm.Log($"[Error] StopSlew failed: {ex.Message}");
                }
            });
        }
    }
}
