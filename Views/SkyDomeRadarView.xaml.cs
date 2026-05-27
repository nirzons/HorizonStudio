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

            var viewModel = canvas.DataContext as ViewModels.RadarViewModel;
            if (viewModel != null && (!viewModel.Parent.IsMountConnected || viewModel.Parent.IsSlewing || viewModel.Parent.IsActionSlewing)) {
                return; // Prevent clicking to slew if mount is slewing or disconnected
            }

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
            var viewModel = canvas.DataContext as ViewModels.RadarViewModel;
            if (handler != null) {
                bool canClickToSlew = viewModel == null || (viewModel.Parent.IsMountConnected && !viewModel.Parent.IsSlewing && !viewModel.Parent.IsActionSlewing);
                
                canvas.Cursor = (canClickToSlew && handler.IsNearHorizon(pos.X, pos.Y))
                    ? Cursors.Hand
                    : Cursors.Arrow;
            }
        }
    }
}
