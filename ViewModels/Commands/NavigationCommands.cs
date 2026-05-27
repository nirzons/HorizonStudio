using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Input;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Profile.Interfaces;
using NirZonshine.NINA.HorizonStudio.Domain;

namespace NirZonshine.NINA.HorizonStudio.ViewModels.Commands {
    public partial class NavigationCommands {
        private readonly HorizonMapperDockableVM _vm;
        private readonly ITelescopeMediator _telescopeMediator;
        private readonly IProfileService _profileService;

        public ICommand StartMappingCommand { get; }
        public ICommand StopMappingCommand { get; }
        public ICommand DropPinCommand { get; }
        public ICommand UndoPinCommand { get; }
        public ICommand ClearPinsCommand { get; }
        public ICommand DeletePointCommand { get; }

        public ICommand SlewCCWCommand { get; }
        public ICommand SlewCWCommand { get; }

        public NavigationCommands(HorizonMapperDockableVM vm, ITelescopeMediator telescopeMediator, IProfileService profileService) {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            _telescopeMediator = telescopeMediator ?? throw new ArgumentNullException(nameof(telescopeMediator));
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));

            StartMappingCommand = new RelayCommand(o => StartMapping());
            StopMappingCommand = new RelayCommand(o => StopMapping());
            DropPinCommand = new RelayCommand(o => DropPin(), o => !_vm.IsSyncPreparing);
            UndoPinCommand = new RelayCommand(o => UndoPin(), o => !_vm.IsSyncPreparing);
            ClearPinsCommand = new RelayCommand(o => ClearPins(), o => !_vm.IsSyncPreparing);
            DeletePointCommand = new RelayCommand(o => DeletePoint(), o => _vm.HasActiveNode && !_vm.IsSyncPreparing);

            SlewCCWCommand = new RelayCommand(o => SlewCCW(), o => _vm.Radar != null && _vm.Radar.CanVerifyPoints);
            SlewCWCommand = new RelayCommand(o => SlewCW(), o => _vm.Radar != null && _vm.Radar.CanVerifyPoints);
        }
    }
}
