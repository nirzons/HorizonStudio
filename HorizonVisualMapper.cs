using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace NirZonshine.NINA.HorizonVisualMapper {
    /// <summary>
    /// This class exports the IPluginManifest interface and will be used for the general plugin information and options
    /// </summary>
    [Export(typeof(IPluginManifest))]
    public class HorizonVisualMapper : PluginBase, INotifyPropertyChanged {
        
        [ImportingConstructor]
        public HorizonVisualMapper(IProfileService profileService, IOptionsVM options, IImageSaveMediator imageSaveMediator) {
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
