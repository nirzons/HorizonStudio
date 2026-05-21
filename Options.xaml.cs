using System.ComponentModel.Composition;
using System.Windows;

namespace NirZonshine.NINA.HorizonVisualMapper {
    /// <summary>
    /// Code-behind for the Options.xaml ResourceDictionary.
    /// Exports the resource dictionary into N.I.N.A.'s theme assembly locator using MEF.
    /// </summary>
    [Export(typeof(ResourceDictionary))]
    public partial class Options : ResourceDictionary {
        public Options() {
            InitializeComponent();
        }
    }
}
