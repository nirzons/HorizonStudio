using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile.Interfaces;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace NirZonshine.NINA.HorizonStudio {
    /// <summary>
    /// Exports the IPluginManifest interface for N.I.N.A.'s plugin loader.
    /// General plugin metadata and lifecycle hooks live here.
    /// </summary>
    [Export(typeof(IPluginManifest))]
    public class HorizonStudio : PluginBase, INotifyPropertyChanged {

        // FIX #18: Removed unused IOptionsVM and IImageSaveMediator parameters.
        // The plugin manifest only needs IProfileService for base class initialization.
        [ImportingConstructor]
        public HorizonStudio(IProfileService profileService) {
        }

        public override Task Teardown() {
            return base.Teardown();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null) {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
