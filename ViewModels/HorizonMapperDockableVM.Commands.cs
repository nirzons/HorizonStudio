using System.Windows.Input;

namespace NirZonshine.NINA.HorizonStudio.ViewModels {
    public partial class HorizonMapperDockableVM {
        // CanDropPin: Mapping must be started, mount connected, AND not slewing
        public bool CanDropPin => IsRunning && IsMountConnected && !IsSlewing;

        // Forward commands to specific sub-command classes
        public ICommand StartMappingCommand => _navigationCommands.StartMappingCommand;
        public ICommand StopMappingCommand => _navigationCommands.StopMappingCommand;
        public ICommand DropPinCommand => _navigationCommands.DropPinCommand;
        public ICommand UndoPinCommand => _navigationCommands.UndoPinCommand;
        public ICommand ClearPinsCommand => _navigationCommands.ClearPinsCommand;
        public ICommand SaveHorizonCommand => _fileCommands.SaveHorizonCommand;
        public ICommand LoadHorizonCommand => _fileCommands.LoadHorizonCommand;
        public ICommand DeletePointCommand => _navigationCommands.DeletePointCommand;

        public ICommand PrepareSyncCommand => _syncCommands.PrepareSyncCommand;
        public ICommand ConfirmSyncCommand => _syncCommands.ConfirmSyncCommand;
        public ICommand CancelSyncCommand => _syncCommands.CancelSyncCommand;
        public ICommand AddLandmarkCommand => _landmarkCommands.AddLandmarkCommand;
        public ICommand DeleteLandmarkCommand => _landmarkCommands.DeleteLandmarkCommand;
        public ICommand SlewToLandmarkCommand => _landmarkCommands.SlewToLandmarkCommand;
        public ICommand SelectLandmarkCommand => _landmarkCommands.SelectLandmarkCommand;
        public ICommand RenameLandmarkCommand => _landmarkCommands.RenameLandmarkCommand;
        public ICommand ClearAllLandmarksCommand => _landmarkCommands.ClearAllLandmarksCommand;

        // Forward Camera sub-ViewModel commands so CameraConfigCard (DataContext=parent) can bind them
        public ICommand SelectVisualFeedSourceCommand => Camera?.SelectVisualFeedSourceCommand;
        public ICommand StartMainCameraCommand => Camera?.StartMainCameraCommand;
        public ICommand StopMainCameraCommand => Camera?.StopMainCameraCommand;

        public ICommand StartWebcamCommand => Webcam?.StartWebcamCommand;
        public ICommand StopWebcamCommand => Webcam?.StopWebcamCommand;
        public ICommand RefreshWebcamsCommand => Webcam?.RefreshWebcamsCommand;

        public ICommand StartCoAlignmentCommand => Webcam?.StartCoAlignmentCommand;
        public ICommand SaveCoAlignmentCommand => Webcam?.SaveCoAlignmentCommand;
        public ICommand ResetCoAlignmentCommand => Webcam?.ResetCoAlignmentCommand;

        public ICommand SlewCCWCommand => _navigationCommands.SlewCCWCommand;
        public ICommand SlewCWCommand => _navigationCommands.SlewCWCommand;

        public ICommand JogNorthCommand => _mountJogCommands.JogNorthCommand;
        public ICommand JogSouthCommand => _mountJogCommands.JogSouthCommand;
        public ICommand JogEastCommand => _mountJogCommands.JogEastCommand;
        public ICommand JogWestCommand => _mountJogCommands.JogWestCommand;

        public ICommand JogNorthEastCommand => _mountJogCommands.JogNorthEastCommand;
        public ICommand JogNorthWestCommand => _mountJogCommands.JogNorthWestCommand;
        public ICommand JogSouthEastCommand => _mountJogCommands.JogSouthEastCommand;
        public ICommand JogSouthWestCommand => _mountJogCommands.JogSouthWestCommand;

        public ICommand DoubleJogNorthCommand => _mountJogCommands.DoubleJogNorthCommand;
        public ICommand DoubleJogSouthCommand => _mountJogCommands.DoubleJogSouthCommand;
        public ICommand DoubleJogEastCommand => _mountJogCommands.DoubleJogEastCommand;
        public ICommand DoubleJogWestCommand => _mountJogCommands.DoubleJogWestCommand;

        public ICommand DoubleJogNorthEastCommand => _mountJogCommands.DoubleJogNorthEastCommand;
        public ICommand DoubleJogNorthWestCommand => _mountJogCommands.DoubleJogNorthWestCommand;
        public ICommand DoubleJogSouthEastCommand => _mountJogCommands.DoubleJogSouthEastCommand;
        public ICommand DoubleJogSouthWestCommand => _mountJogCommands.DoubleJogSouthWestCommand;

        public ICommand HomeMountCommand => _mountJogCommands.HomeMountCommand;
        public ICommand StopMountCommand => _mountJogCommands.StopMountCommand;

        public ICommand JogN2W1Command => _mountJogCommands.JogN2W1Command;
        public ICommand JogN2E1Command => _mountJogCommands.JogN2E1Command;
        public ICommand JogN1W2Command => _mountJogCommands.JogN1W2Command;
        public ICommand JogN1E2Command => _mountJogCommands.JogN1E2Command;
        public ICommand JogS1W2Command => _mountJogCommands.JogS1W2Command;
        public ICommand JogS1E2Command => _mountJogCommands.JogS1E2Command;
        public ICommand JogS2W1Command => _mountJogCommands.JogS2W1Command;
        public ICommand JogS2E1Command => _mountJogCommands.JogS2E1Command;
    }
}
