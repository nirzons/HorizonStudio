using System;

namespace NirZonshine.NINA.HorizonVisualMapper.Services {
    public class DeviceDescriptor : IEquatable<DeviceDescriptor> {
        public string Name { get; }
        public string DevicePath { get; }

        public DeviceDescriptor(string name, string devicePath) {
            Name = name ?? string.Empty;
            DevicePath = devicePath ?? string.Empty;
        }

        public override string ToString() {
            return Name;
        }

        public bool Equals(DeviceDescriptor other) {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return string.Equals(DevicePath, other.DevicePath, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj) {
            return Equals(obj as DeviceDescriptor);
        }

        public override int GetHashCode() {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(DevicePath);
        }
    }
}
