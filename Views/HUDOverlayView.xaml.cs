using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace NirZonshine.NINA.HorizonStudio.Views {
    /// <summary>
    /// Interaction logic for HUDOverlayView.xaml
    /// </summary>
    public partial class HUDOverlayView : UserControl {
        public HUDOverlayView() {
            InitializeComponent();
        }

        private void Image_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            var img = sender as Image;
            if (img == null) return;

            var lastFrame = img.Source as BitmapSource;
            if (lastFrame == null) return;

            var pos = e.GetPosition(img);

            // The coordinate space of e.GetPosition(img) is mapped to the parent Grid's layout slot,
            // which is explicitly sized to 600x600 in HUDOverlayView.xaml.
            double viewportWidth = 600.0;
            double viewportHeight = 600.0;

            // Calculate UniformToFill stretch scaling and offsets relative to the 600x600 viewport
            double scaleX = viewportWidth / lastFrame.PixelWidth;
            double scaleY = viewportHeight / lastFrame.PixelHeight;
            double scale = Math.Max(scaleX, scaleY);

            double renderWidth = lastFrame.PixelWidth * scale;
            double renderHeight = lastFrame.PixelHeight * scale;

            double offsetX = (viewportWidth - renderWidth) / 2.0;
            double offsetY = (viewportHeight - renderHeight) / 2.0;

            // Click relative to the rendered image content
            double clickX = pos.X - offsetX;
            double clickY = pos.Y - offsetY;

            // Log raw click variables for diagnostic debugging
            global::NINA.Core.Utility.Logger.Info($"[Horizon Studio] Click Debug: pos=({pos.X:F1}, {pos.Y:F1}), imgSize=({img.ActualWidth:F1}x{img.ActualHeight:F1}), viewport=({viewportWidth:F1}x{viewportHeight:F1}), scale=({scaleX:F4}, {scaleY:F4} -> {scale:F4}), render=({renderWidth:F1}x{renderHeight:F1}), offset=({offsetX:F1}, {offsetY:F1}), click=({clickX:F1}, {clickY:F1})");

            // Clamp click coordinates to the rendered bounds
            clickX = Math.Max(0.0, Math.Min(renderWidth, clickX));
            clickY = Math.Max(0.0, Math.Min(renderHeight, clickY));

            // The DataContext is WebcamViewModel (set in Options.xaml), which has HandleImageClick.
            var webcamVm = img.DataContext as ViewModels.WebcamViewModel;
            if (webcamVm != null) {
                webcamVm.HandleImageClick(clickX, clickY, renderWidth, renderHeight);
            }
        }

        private void RadarCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            var canvas = sender as Canvas;
            if (canvas == null) return;

            var pos = e.GetPosition(canvas);

            var handler = canvas.DataContext as ViewModels.IRadarClickHandler;
            if (handler != null) {
                handler.HandleRadarClick(pos.X, pos.Y);
            }
        }

        private void RadarCanvas_MouseMove(object sender, MouseEventArgs e) {
            var canvas = sender as Canvas;
            if (canvas == null) return;

            var pos = e.GetPosition(canvas);

            var handler = canvas.DataContext as ViewModels.IRadarClickHandler;
            if (handler != null) {
                canvas.Cursor = handler.IsNearHorizon(pos.X, pos.Y)
                    ? Cursors.Hand
                    : Cursors.Arrow;
            }
        }
    }
}
