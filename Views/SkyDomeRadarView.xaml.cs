using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NirZonshine.NINA.HorizonStudio.Views {
    /// <summary>
    /// Interaction logic for SkyDomeRadarView.xaml
    /// </summary>
    public partial class SkyDomeRadarView : UserControl {
        public SkyDomeRadarView() {
            InitializeComponent();
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
