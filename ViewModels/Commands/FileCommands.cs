using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Input;
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

            SaveHorizonCommand = new RelayCommand(o => SaveHorizon());
            LoadHorizonCommand = new RelayCommand(o => LoadHorizon());
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

                _vm.Log($"[Save] Writing {rawNodes.Count} pinned nodes (N.I.N.A. interpolates natively).");

                string suggestedName = $"CustomHorizon_{DateTime.Now:yyyyMMdd_HHmm}.hrz";

                var dialog = new SaveFileDialog {
                    Title = "Save N.I.N.A. Horizon Profile",
                    Filter = "N.I.N.A. Horizon Files (*.hrz)|*.hrz|Legacy Horizon Files (*.hrzn)|*.hrzn|CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                    DefaultExt = ".hrz",
                    FileName = suggestedName
                };

                if (dialog.ShowDialog() == true) {
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
            } catch (Exception ex) {
                _vm.Log($"[Error] Failed to save horizon: {ex.Message}");
                global::NINA.Core.Utility.Notification.Notification.ShowError($"Failed to save horizon: {ex.Message}");
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
                        _vm.SelectedLandmark = _vm.SyncLandmarks[0];
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
