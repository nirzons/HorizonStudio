using System;
using System.ComponentModel.Composition;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace NirZonshine.NINA.HorizonStudio {
    /// <summary>
    /// Code-behind for the Options.xaml ResourceDictionary.
    /// Exports the resource dictionary into N.I.N.A.'s theme assembly locator using MEF.
    /// </summary>
    [Export(typeof(ResourceDictionary))]
    public partial class Options : ResourceDictionary {
        public Options() {
            InitializeComponent();
        }

        private void Image_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            var img = sender as Image;
            if (img == null) return;

            var lastFrame = img.Source as BitmapSource;
            if (lastFrame == null) return;

            var pos = e.GetPosition(img);

            // The coordinate space of e.GetPosition(img) is mapped to the parent Grid's layout slot,
            // which is explicitly sized to 600x600 in Options.xaml.
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

            // FIX #19: Cast to IImageClickHandler instead of the concrete VM type.
            // This decouples the code-behind from the ViewModel implementation.
            var handler = img.DataContext as ViewModels.IImageClickHandler;
            if (handler != null) {
                handler.HandleImageClick(clickX, clickY, renderWidth, renderHeight);
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

    public class RenderedImageSizeConverter : IMultiValueConverter {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
            if (values.Length >= 4) {
                double controlWidth = 0.0;
                if (values[0] is double d1) controlWidth = d1;
                else if (values[0] is int i1) controlWidth = i1;

                double controlHeight = 0.0;
                if (values[1] is double d2) controlHeight = d2;
                else if (values[1] is int i2) controlHeight = i2;

                double frameWidth = 0.0;
                if (values[2] is double d3) frameWidth = d3;
                else if (values[2] is int i3) frameWidth = i3;

                double frameHeight = 0.0;
                if (values[3] is double d4) frameHeight = d4;
                else if (values[3] is int i4) frameHeight = i4;

                if (controlWidth <= 0 || controlHeight <= 0 || frameWidth <= 0 || frameHeight <= 0) return 0.0;

                double scaleX = controlWidth / frameWidth;
                double scaleY = controlHeight / frameHeight;
                double scale = Math.Max(scaleX, scaleY);

                string param = parameter as string;
                if (param == "Width") {
                    return frameWidth * scale;
                } else if (param == "Height") {
                    return frameHeight * scale;
                }
            }
            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }

    public class RatioToCoordinateConverter : IMultiValueConverter {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
            if (values.Length >= 2 && values[0] is double ratio && values[1] is double dimension) {
                return ratio * dimension;
            }
            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }
}
