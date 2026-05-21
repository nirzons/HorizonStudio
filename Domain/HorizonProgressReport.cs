using System;
using System.Windows.Media;

namespace NirZonshine.NINA.HorizonVisualMapper.Domain {
    /// <summary>
    /// Data Transfer Object (DTO) conveying background mapping and hardware status reports
    /// to the reactive WPF view model dispatcher.
    /// </summary>
    public class HorizonProgressReport {
        public string LogMessage { get; set; }
        public string StatusText { get; set; }
        public string StatusColorHex { get; set; }
        
        public double? CurrentAltitude { get; set; }
        public double? CurrentAzimuth { get; set; }
        
        public string LastMappedNodeText { get; set; }
        public int? TotalNodeCount { get; set; }
        
        public ImageSource CameraFrame { get; set; }
        
        public bool? IsSlewActive { get; set; }
        public bool? IsSolarSafetyTriggered { get; set; }
        public bool? IsZenithSafetyTriggered { get; set; }
    }
}
