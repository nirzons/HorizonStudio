using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using NINA.Profile.Interfaces;
using NirZonshine.NINA.HorizonStudio.Domain;

namespace NirZonshine.NINA.HorizonStudio.ViewModels.Commands {
    public class FileCommands {
        private readonly HorizonMapperDockableVM _vm;
        private readonly IProfileService _profileService;

        public ICommand SaveHorizonCommand { get; }
        public ICommand LoadHorizonCommand { get; }

        public FileCommands(HorizonMapperDockableVM vm, IProfileService profileService) {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));

            SaveHorizonCommand = new RelayCommand(o => SaveHorizon(), o => !_vm.IsSyncPreparing);
            LoadHorizonCommand = new RelayCommand(o => LoadHorizon(), o => !_vm.IsSyncPreparing);
        }

        public void SaveHorizon() {
            if (_vm.HorizonNodes.Count < 3) {
                _vm.Log("[Error] Cannot save horizon: You need to drop at least 3 pins.");
                global::NINA.Core.Utility.Notification.Notification.ShowError("Need at least 3 points to save a horizon.");
                return;
            }

            try {
                var rawNodes = _vm.HorizonNodes
                    .Select(n => (Az: n.Azimuth, Alt: n.Altitude))
                    .OrderBy(n => n.Az)
                    .ToList();

                double maxGap = 0;
                int splitIndex = -1;
                for (int i = 0; i < rawNodes.Count - 1; i++) {
                    double gap = rawNodes[i + 1].Az - rawNodes[i].Az;
                    if (gap > maxGap) {
                        maxGap = gap;
                        splitIndex = i;
                    }
                }

                if (maxGap > 180 && splitIndex != -1) {
                    _vm.Log($"[Save] Detected boundary wrap (gap {maxGap:F1}°). Unwrapping nodes...");
                    for (int i = 0; i <= splitIndex; i++) {
                        rawNodes[i] = (rawNodes[i].Az + 360.0, rawNodes[i].Alt);
                    }
                    rawNodes = rawNodes.OrderBy(n => n.Az).ToList();
                }

                _vm.Log($"[Save] Writing {rawNodes.Count} pinned nodes.");

                string suggestedName = $"CustomHorizon_{DateTime.Now:yyyyMMdd_HHmm}.hrz";

                var dialog = new SaveFileDialog {
                    Title = "Save Horizon Profile",
                    Filter = "N.I.N.A. Horizon Files (*.hrz)|*.hrz|SkySafari Horizon Image (*.png)|*.png|Legacy Horizon Files (*.hrzn)|*.hrzn|CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                    DefaultExt = ".hrz",
                    FileName = suggestedName
                };

                if (dialog.ShowDialog() == true) {
                    string ext = Path.GetExtension(dialog.FileName).ToLower();
                    if (ext == ".png") {
                        SaveSkySafariPng(dialog.FileName);
                    } else {
                        var fileLines = new List<string>();

                        foreach (var landmark in _vm.SyncLandmarks) {
                            fileLines.Add($"# HorizonStudio_Landmark: Id={landmark.Id};Name={landmark.Name};Az={landmark.Azimuth.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)};Alt={landmark.Altitude.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}");
                        }
                        fileLines.Add("# Az Alt");

                        foreach (var n in rawNodes) {
                            double normalizedAz = (n.Az % 360.0 + 360.0) % 360.0;
                            fileLines.Add($"{normalizedAz:F4} {n.Alt:F4}");
                        }
                        File.WriteAllLines(dialog.FileName, fileLines);

                        _vm.Log($"[Save] Successfully saved {rawNodes.Count} nodes to {dialog.FileName}");
                        global::NINA.Core.Utility.Notification.Notification.ShowSuccess($"Horizon profile saved successfully to {Path.GetFileName(dialog.FileName)}!");
                    }
                }
            } catch (Exception ex) {
                _vm.Log($"[Error] Failed to save horizon: {ex.Message}");
                global::NINA.Core.Utility.Notification.Notification.ShowError($"Failed to save horizon: {ex.Message}");
            }
        }

        private void SaveSkySafariPng(string fileName) {
            try {
                _vm.Log($"[Save] Generating SkySafari compliant 32-bit custom horizon image...");
                int width = 2048;
                int height = 1024;
                int bytesPerPixel = 4;
                int stride = width * bytesPerPixel;
                byte[] pixelBuffer = new byte[height * stride];

                for (int x = 0; x < width; x++) {
                    // Map X coordinate to Azimuth (0 to 360 degrees)
                    double azimuth = x * 360.0 / width;
                    
                    // Retrieve interpolated altitude from ViewModel
                    double altitude = _vm.GetInterpolatedAltitude(azimuth);
                    
                    // Map altitude to Y coordinate: Zenith (+90) is Y=0, Horizon (0) is Y=512, Nadir (-90) is Y=1023
                    double yHorizon = 512.0 - (altitude * 512.0 / 90.0);
                    int yHorizonInt = (int)Math.Round(yHorizon);
                    yHorizonInt = Math.Max(0, Math.Min(height - 1, yHorizonInt));

                    for (int y = 0; y < height; y++) {
                        int pixelOffset = (y * stride) + (x * bytesPerPixel);

                        if (y >= yHorizonInt - 1 && y <= yHorizonInt) {
                            // Fine line of solid red right at the actual horizon boundary (2 pixels thick)
                            pixelBuffer[pixelOffset] = 0;       // Blue
                            pixelBuffer[pixelOffset + 1] = 0;   // Green
                            pixelBuffer[pixelOffset + 2] = 255; // Red
                            pixelBuffer[pixelOffset + 3] = 255; // Alpha (Solid Opaque)
                        } else if (y > yHorizonInt) {
                            // Ground/Obstruction region: Beautiful, semi-transparent dark blue at 70% opacity
                            // Format: BGRA (Blue, Green, Red, Alpha)
                            pixelBuffer[pixelOffset] = 70;      // Blue (high blue component for astronomical feel)
                            pixelBuffer[pixelOffset + 1] = 20;  // Green (deep dark hue)
                            pixelBuffer[pixelOffset + 2] = 15;  // Red (deep dark hue)
                            pixelBuffer[pixelOffset + 3] = 178; // Alpha (70% opaque / 30% transparent)
                        } else {
                            // Sky region: Completely transparent (Alpha = 0)
                            pixelBuffer[pixelOffset] = 0;
                            pixelBuffer[pixelOffset + 1] = 0;
                            pixelBuffer[pixelOffset + 2] = 0;
                            pixelBuffer[pixelOffset + 3] = 0;   // Alpha (Transparent)
                        }
                    }
                }

                // Create a standard WPF BitmapSource from the pixel array
                var bitmap = BitmapSource.Create(
                    width,
                    height,
                    96,
                    96,
                    PixelFormats.Bgra32,
                    null,
                    pixelBuffer,
                    stride
                );

                // Encode and write to disk as a 32-bit PNG
                using (var stream = new FileStream(fileName, FileMode.Create)) {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    encoder.Save(stream);
                }

                _vm.Log($"[Save] Successfully exported SkySafari panorama image to {fileName}");
                global::NINA.Core.Utility.Notification.Notification.ShowSuccess($"SkySafari panorama saved successfully to {Path.GetFileName(fileName)}!");
            } catch (Exception ex) {
                _vm.Log($"[Error] Failed to generate SkySafari PNG: {ex.Message}");
                global::NINA.Core.Utility.Notification.Notification.ShowError($"Failed to generate SkySafari PNG: {ex.Message}");
            }
        }

        public void LoadHorizon() {
            if (_vm.HorizonNodes.Count > 0) {
                var result = System.Windows.MessageBox.Show(
                    "Loading a new profile will clear your currently placed horizon pins. Do you want to continue?",
                    "Clear Existing Horizon Pins?",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning
                );
                if (result != System.Windows.MessageBoxResult.Yes) {
                    return;
                }
            }

            var dialog = new OpenFileDialog {
                Title = "Load N.I.N.A. Horizon Profile",
                Filter = "N.I.N.A. Horizon Files (*.hrz)|*.hrz|Legacy Horizon Files (*.hrzn)|*.hrzn|CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                DefaultExt = ".hrz"
            };

            if (dialog.ShowDialog() == true) {
                try {
                    var lines = File.ReadAllLines(dialog.FileName);
                    var newNodes = new List<HorizonNode>();
                    int lineCount = 0;

                    _vm.SyncLandmarks.Clear();
                    _vm.SelectedLandmark = null;
                    foreach (var line in lines) {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("# HorizonStudio_Landmark:")) {
                            var idMatch = Regex.Match(trimmed, @"Id=(?<id>[^;]+)");
                            var nameMatch = Regex.Match(trimmed, @"Name=(?<name>[^;]+)");
                            var azMatch = Regex.Match(trimmed, @"Az=(?<az>[\d.-]+)");
                            var altMatch = Regex.Match(trimmed, @"Alt=(?<alt>[\d.-]+)");

                            if (azMatch.Success && altMatch.Success &&
                                double.TryParse(azMatch.Groups["az"].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedAz) &&
                                double.TryParse(altMatch.Groups["alt"].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedAlt)) {
                                
                                string id = idMatch.Success ? idMatch.Groups["id"].Value : Guid.NewGuid().ToString();
                                string name = nameMatch.Success ? nameMatch.Groups["name"].Value : $"Landmark {_vm.SyncLandmarks.Count + 1}";
                                var landmark = new SyncLandmark(id, name, parsedAz, parsedAlt);
                                _vm.SyncLandmarks.Add(landmark);
                            }
                        } else if (trimmed.StartsWith("# HorizonStudio_Metadata:")) {
                            var metaMatch = Regex.Match(trimmed, @"LandmarkAz=(?<az>[\d.-]+).*LandmarkAlt=(?<alt>[-]?[\d.-]+)");
                            if (metaMatch.Success &&
                                double.TryParse(metaMatch.Groups["az"].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedAz) &&
                                double.TryParse(metaMatch.Groups["alt"].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedAlt)) {
                                
                                var landmark = new SyncLandmark(Guid.NewGuid().ToString(), "Landmark 1", parsedAz, parsedAlt);
                                _vm.SyncLandmarks.Add(landmark);
                                _vm.Log($"[Load] Extracted legacy single landmark from file metadata: Az {parsedAz:F2}°, Alt {parsedAlt:F2}°");
                            }
                        }
                        if (!string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith("#")) break;
                    }

                    if (_vm.SyncLandmarks.Count == 0) {
                        string fileName = Path.GetFileName(dialog.FileName);
                        var match = Regex.Match(fileName, @"_sync_Az(?<az>\d+(\.\d+)?)(_Alt|-Alt)(?<alt>[-]?\d+(\.\d+)?)", RegexOptions.IgnoreCase);
                        if (!match.Success) {
                            match = Regex.Match(fileName, @"_sync_(?<az>\d+(\.\d+)?)(_|-)(?<alt>[-]?\d+(\.\d+)?)", RegexOptions.IgnoreCase);
                        }
                        if (match.Success &&
                            double.TryParse(match.Groups["az"].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double legacyAz) &&
                            double.TryParse(match.Groups["alt"].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double legacyAlt)) {
                            
                            var landmark = new SyncLandmark(Guid.NewGuid().ToString(), "Landmark 1", legacyAz, legacyAlt);
                            _vm.SyncLandmarks.Add(landmark);
                            _vm.Log($"[Load] Extracted legacy special sync landmark from legacy filename: Az {legacyAz:F2}°, Alt {legacyAlt:F2}°");
                        }
                    }

                    if (_vm.SyncLandmarks.Count > 0) {
                        _vm.NotifyPropertyChanged("HasLandmarks");
                        _vm.Landmark?.NotifyLandmarksCollectionChanged();
                    }

                    foreach (var line in lines) {
                        lineCount++;
                        var trimmed = line.Trim();
                        if (string.IsNullOrWhiteSpace(trimmed)) continue;
                        if (trimmed.StartsWith("#")) continue;

                        var parts = trimmed.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2) {
                            if (double.TryParse(parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double az) &&
                                double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double alt)) {
                                
                                newNodes.Add(new HorizonNode(az, alt));
                            } else {
                                _vm.Log($"[Warning] Failed to parse line {lineCount} in horizon profile: '{line}'");
                            }
                        }
                    }

                    if (newNodes.Count == 0) {
                        _vm.Log("[Error] Failed to load horizon: No valid coordinates found in file.");
                        global::NINA.Core.Utility.Notification.Notification.ShowError("Failed to load horizon: No valid coordinates found in file.");
                        return;
                    }

                    _vm.ClearPins();

                    newNodes = newNodes.OrderBy(n => n.Azimuth).ToList();

                    foreach (var node in newNodes) {
                        _vm.HorizonNodes.Add(node);
                    }
                    _vm.NodeCount = _vm.HorizonNodes.Count;

                    var top = _vm.HorizonNodes.Last();
                    _vm.LastNodeAlt = top.Altitude;
                    _vm.LastNodeAz = top.Azimuth;
                    _vm.LastNodeText = top.ToString();

                    _vm.Log($"[Load] Successfully loaded {newNodes.Count} nodes from {Path.GetFileName(dialog.FileName)}");
                    global::NINA.Core.Utility.Notification.Notification.ShowSuccess($"Horizon profile loaded: {Path.GetFileName(dialog.FileName)}!");

                } catch (Exception ex) {
                    _vm.Log($"[Error] Failed to load horizon profile: {ex.Message}");
                    global::NINA.Core.Utility.Notification.Notification.ShowError($"Failed to load horizon profile: {ex.Message}");
                }
            }
        }
    }
}
