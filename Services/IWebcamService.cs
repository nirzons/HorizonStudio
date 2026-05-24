using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NirZonshine.NINA.HorizonStudio.Services {
    public interface IWebcamService : IDisposable {
        IEnumerable<DeviceDescriptor> GetAvailableCameras();
        WebcamState CurrentState { get; }
        string ConnectedCameraPath { get; }
        
        event EventHandler<WebcamState> StateChanged;
        
        Task StartCaptureAsync(string devicePath, Action<byte[]> onFrameCaptured);
        void StopCapture();
        Task StopCaptureAsync();
    }
}
