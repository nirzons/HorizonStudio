using System;
using NINA.Core.Utility;

namespace NirZonshine.NINA.HorizonStudio.ViewModels {
    public partial class WebcamViewModel {
        public void UpdateRotationAngle() {
            if (IsCoAligning) {
                WebcamImageRotationAngle = 0.0;
                return;
            }

            if (!_parent.IsMountConnected) {
                WebcamImageRotationAngle = 0.0;
                return;
            }

            double totalRotation = CameraRotationOffset;

            if (IsCounterRotationEnabled) {
                try {
                    double lat = _parent.TelescopeMediator.GetInfo()?.SiteLatitude ?? _parent.ProfileService?.ActiveProfile?.AstrometrySettings?.Latitude ?? 0.0;
                    double altRad = _parent.CurrentAlt * Math.PI / 180.0;
                    double azRad = _parent.CurrentAz * Math.PI / 180.0;
                    double latRad = lat * Math.PI / 180.0;

                    double yHA = -Math.Sin(azRad) * Math.Cos(altRad);
                    double xHA = Math.Sin(altRad) * Math.Cos(latRad) - Math.Cos(altRad) * Math.Sin(latRad) * Math.Cos(azRad);
                    double haDeg = Math.Atan2(yHA, xHA) * 180.0 / Math.PI;

                    bool isPointingEast = false;
                    var side = _parent.TelescopeInfo?.SideOfPier;
                    if (side != null) {
                        if (side.ToString().Contains("Unknown")) {
                            isPointingEast = (haDeg < -0.1);
                        } else {
                            try {
                                isPointingEast = (side == global::NINA.Core.Enum.PierSide.pierWest);
                            } catch {
                                isPointingEast = side.ToString().Contains("West");
                            }
                        }
                    } else {
                        isPointingEast = (haDeg < -0.1);
                    }

                    double yQ = Math.Sin(azRad);
                    double xQ = Math.Cos(altRad) * Math.Tan(latRad) - Math.Sin(altRad) * Math.Cos(azRad);
                    double qDeg = Math.Atan2(yQ, xQ) * 180.0 / Math.PI;

                    totalRotation -= qDeg;

                    if (isPointingEast) {
                        totalRotation += 180.0;
                    }

                    totalRotation = (totalRotation + 180.0) % 360.0;
                    if (totalRotation < 0.0) {
                        totalRotation += 360.0;
                    }
                    totalRotation -= 180.0;
                } catch (Exception ex) {
                    Logger.Error($"[Horizon Studio] Rotation calculation failed: {ex.Message}");
                }
            }

            WebcamImageRotationAngle = totalRotation;
        }

        private void StartCoAlignment() {
            System.Windows.MessageBox.Show(
                "To perform optical axis co-alignment, you need to view both your primary telescope camera and this webcam feed at the same time.\n\n" +
                "How to view both simultaneously in N.I.N.A.:\n" +
                "1. Click and hold this 'Horizon Visual Mapper' tab header.\n" +
                "2. Drag it over N.I.N.A.'s native 'Imaging' panel.\n" +
                "3. Hover near the left, right, or bottom edge until a blue docking preview box highlights.\n" +
                "4. Release your mouse button to dock the webcam feed side-by-side with your main camera view.\n\n" +
                "Co-Alignment Steps:\n" +
                "1. Center a distinct landmark (e.g., a chimney peak or antenna tip) in your primary telescope camera.\n" +
                "2. Click that exact same landmark in the live webcam feed below.\n" +
                "3. Click 'Save Alignment' to save your custom optical alignment center.\n\n" +
                "Click OK to begin co-alignment.",
                "Co-Alignment Assistant Setup Guide",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);

            IsCoAligning = true;
            _parent.Log("[Co-Alignment] Co-Alignment assistant started. Center a target in your main telescope camera, then click it on the webcam stream.");
        }

        private void SaveCoAlignment() {
            IsCoAligning = false;
            IsCoAligned = true;
            _parent.Log($"[Co-Alignment] Saved custom co-alignment center: ({AlignmentCenterX:F3}, {AlignmentCenterY:F3})");
        }

        private void ResetCoAlignment() {
            IsCoAligning = false;
            IsCoAligned = false;
            AlignmentCenterX = 0.5;
            AlignmentCenterY = 0.5;
            _parent.Log("[Co-Alignment] Reset co-alignment to the geometric center.");
        }
    }
}
