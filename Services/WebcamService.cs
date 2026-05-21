using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FlashCap;
using NINA.Core.Utility;

namespace NirZonshine.NINA.HorizonVisualMapper.Services {
    public class WebcamService : IWebcamService, IDisposable {
        private readonly object _stateLock = new object();
        private CaptureDevice _captureDevice;
        private WebcamState _currentState = WebcamState.Disconnected;
        private string _connectedCameraPath = string.Empty;
        private bool _disposed = false;

        public event EventHandler<WebcamState> StateChanged;

        public WebcamState CurrentState {
            get {
                lock (_stateLock) {
                    return _currentState;
                }
            }
            private set {
                lock (_stateLock) {
                    if (_currentState != value) {
                        _currentState = value;
                        StateChanged?.Invoke(this, _currentState);
                    }
                }
            }
        }

        public string ConnectedCameraPath {
            get {
                lock (_stateLock) {
                    return _connectedCameraPath;
                }
            }
            private set {
                lock (_stateLock) {
                    _connectedCameraPath = value;
                }
            }
        }

        public IEnumerable<DeviceDescriptor> GetAvailableCameras() {
            try {
                var devices = new CaptureDevices();
                return devices.EnumerateDescriptors()
                    .Select(d => new DeviceDescriptor(d.Name, d.Identity.ToString()))
                    .ToList();
            } catch (Exception ex) {
                Logger.Error($"[Horizon Visual Mapper] Failed to enumerate webcams: {ex.Message}");
                return Enumerable.Empty<DeviceDescriptor>();
            }
        }

        public async Task StartCaptureAsync(string devicePath, Action<byte[]> onFrameCaptured) {
            if (string.IsNullOrEmpty(devicePath)) {
                throw new ArgumentException("Device path cannot be null or empty.", nameof(devicePath));
            }
            if (onFrameCaptured == null) {
                throw new ArgumentNullException(nameof(onFrameCaptured));
            }

            // Stop any existing capture
            await StopCaptureAsync();

            lock (_stateLock) {
                if (_disposed) throw new ObjectDisposedException(nameof(WebcamService));
                CurrentState = WebcamState.Connecting;
                ConnectedCameraPath = devicePath;
            }

            try {
                var devices = new CaptureDevices();
                var descriptor = devices.EnumerateDescriptors()
                    .FirstOrDefault(d => string.Equals(d.Identity.ToString(), devicePath, StringComparison.OrdinalIgnoreCase));

                if (descriptor == null) {
                    throw new InvalidOperationException($"Webcam with path '{devicePath}' was not found.");
                }

                // Choose a characteristic (resolution/format) - first valid non-unknown format
                var characteristics = descriptor.Characteristics
                    .FirstOrDefault(c => c.PixelFormat != PixelFormats.Unknown);

                if (characteristics == null) {
                    throw new InvalidOperationException("No valid video formats/characteristics supported by this webcam.");
                }

                Logger.Info($"[Horizon Visual Mapper] Opening webcam: {descriptor.Name} ({characteristics.Width}x{characteristics.Height}, {characteristics.PixelFormat})");

                // Open the device with TranscodeFormats.Auto so it automatically converts YUV to standard RGB DIB (BMP)
                var device = await descriptor.OpenAsync(
                    characteristics,
                    TranscodeFormats.Auto,
                    async bufferScope => {
                        try {
                            if (CurrentState != WebcamState.Streaming) return;
                            
                            // Extract frame data as raw image bytes (BMP/JPEG)
                            byte[] frameData = bufferScope.Buffer.ExtractImage();
                            if (frameData != null && frameData.Length > 0) {
                                onFrameCaptured(frameData);
                            }
                        } catch (Exception ex) {
                            Logger.Error($"[Horizon Visual Mapper] Error in frame capture callback: {ex.Message}");
                        }
                    });

                lock (_stateLock) {
                    if (_disposed) {
                        device.Dispose();
                        CurrentState = WebcamState.Disconnected;
                        ConnectedCameraPath = string.Empty;
                        return;
                    }
                    _captureDevice = device;
                }

                // Start active streaming
                await _captureDevice.StartAsync();
                CurrentState = WebcamState.Streaming;
                Logger.Info("[Horizon Visual Mapper] Webcam stream started successfully.");

            } catch (Exception ex) {
                Logger.Error($"[Horizon Visual Mapper] Failed to start webcam: {ex.Message}");
                lock (_stateLock) {
                    _captureDevice = null;
                    CurrentState = WebcamState.Error;
                    ConnectedCameraPath = string.Empty;
                }
                throw;
            }
        }

        public void StopCapture() {
            CaptureDevice deviceToDispose = null;
            lock (_stateLock) {
                if (_captureDevice != null) {
                    deviceToDispose = _captureDevice;
                    _captureDevice = null;
                }
                CurrentState = WebcamState.Disconnected;
                ConnectedCameraPath = string.Empty;
            }

            if (deviceToDispose != null) {
                try {
                    Logger.Info("[Horizon Visual Mapper] Stopping webcam stream (Synchronous)...");
                    deviceToDispose.Dispose();
                    Logger.Info("[Horizon Visual Mapper] Webcam stream stopped successfully.");
                } catch (Exception ex) {
                    Logger.Error($"[Horizon Visual Mapper] Error during synchronous webcam stop: {ex.Message}");
                }
            }
        }

        public async Task StopCaptureAsync() {
            CaptureDevice deviceToDispose = null;
            lock (_stateLock) {
                if (_captureDevice != null) {
                    deviceToDispose = _captureDevice;
                    _captureDevice = null;
                }
                CurrentState = WebcamState.Disconnected;
                ConnectedCameraPath = string.Empty;
            }

            if (deviceToDispose != null) {
                try {
                    Logger.Info("[Horizon Visual Mapper] Stopping webcam stream (Asynchronous)...");
                    await deviceToDispose.StopAsync();
                    deviceToDispose.Dispose();
                    Logger.Info("[Horizon Visual Mapper] Webcam stream stopped successfully.");
                } catch (Exception ex) {
                    Logger.Error($"[Horizon Visual Mapper] Error during asynchronous webcam stop: {ex.Message}");
                }
            }
        }

        public void Dispose() {
            lock (_stateLock) {
                if (_disposed) return;
                _disposed = true;
            }
            StopCapture();
        }
    }
}
