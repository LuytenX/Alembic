using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

using Microsoft.Win32;

using ACE.DatLoader;
using ACE.DatLoader.Entity;
using ACE.DatLoader.FileTypes;

using ACViewer.Config;
using ACViewer.Data;
using ACViewer.Enum;
using ACViewer.Render;

namespace ACViewer.View
{
    /// <summary>
    /// Interaction logic for MainMenu.xaml
    /// </summary>
    public partial class MainMenu : UserControl
    {
        public static MainWindow MainWindow => MainWindow.Instance;

        public static MainMenu Instance { get; set; }

        public static GameView GameView => GameView.Instance;

        public static Options Options { get; set; }

        public static bool ShowHUD { get; set; }

        public static bool ShowMinimap { get; set; } = true;

        public static bool ShowParticles { get; set; }

        public static bool UseMipMaps
        {
            get => TextureCache.UseMipMaps;
            set => TextureCache.UseMipMaps = value;
        }


        public MainMenu()
        {
            InitializeComponent();
            Instance = this;
        }

        private async void NewMap_Click(object sender, RoutedEventArgs e)
        {
            await MapGenerator.CreateNewMap();
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "DAT files (*.dat)|*.dat|All files (*.*)|*.*";

            var success = openFileDialog.ShowDialog();

            if (success != true) return;

            var filenames = openFileDialog.FileNames;
            
            if (filenames.Length < 1) return;
            
            var filename = filenames[0];

            LoadDATs(filename);
        }

        public void LoadDATs(string filename)
        {
            if (!File.Exists(filename) && !Directory.Exists(filename)) return;
            
            MainWindow.Status.WriteLine("Reading " + filename);

            var worker = new BackgroundWorker();

            worker.DoWork += (sender, doWorkEventArgs) =>
            {
                ReadDATFile(filename);
            };

            worker.RunWorkerCompleted += (sender, runWorkerCompletedEventArgs) =>
            {
                /*var cellFiles = DatManager.CellDat.AllFiles.Count;
                var portalFiles = DatManager.PortalDat.AllFiles.Count;

                MainWindow.Status.WriteLine($"CellFiles={cellFiles}, PortalFiles={portalFiles}");*/
                MainWindow.Status.WriteLine(runWorkerCompletedEventArgs.Error?.Message ?? "Done");
                    

                if (DatManager.CellDat == null || DatManager.PortalDat == null) return;

                GameView.PostInit();
                MapViewer.Instance.Init();
                MainWindow.Instance.PopulateTerrain();
            };
            
            worker.RunWorkerAsync();
        }

        private async void SaveFile_Click(object sender, RoutedEventArgs e)
        {
            if (WorldViewer.Instance != null)
                await WorldViewer.Instance.SaveLandblocksAsync();
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            // get currently selected file from FileExplorer
            var selectedFileID = FileExplorer.Instance.Selected_FileID;

            if (selectedFileID == 0)
            {
                MainWindow.Instance.AddStatusText($"You must first select a file to export");
                return;
            }

            var saveFileDialog = new SaveFileDialog();

            var fileType = selectedFileID >> 24;
            var isModel = fileType == 0x1 || fileType == 0x2;
            var isImage = fileType == 0x5 || fileType == 0x6 || fileType == 08;
            var isSound = fileType == 0xA;

            if (isModel)
            {
                saveFileDialog.Filter = "OBJ files (*.obj)|*.obj|FBX files (*.fbx)|*.fbx|DAE files (*.dae)|*.dae|RAW files (*.raw)|*.raw";
                saveFileDialog.FileName = $"{selectedFileID:X8}.obj";
            }
            else if (isImage)
            {
                saveFileDialog.Filter = "PNG files (*.png)|*.png|RAW files (*.raw)|*.raw";
                saveFileDialog.FileName = $"{selectedFileID:X8}.png";
            }
            else if (isSound)
            {
                var sound = DatManager.PortalDat.ReadFromDat<Wave>(selectedFileID);

                if (sound.Header[0] == 0x55)
                {
                    saveFileDialog.Filter = "MP3 files (*.mp3)|*.mp3|RAW files (*.raw)|*.raw";
                    saveFileDialog.FileName = $"{selectedFileID:X8}.mp3";
                }
                else
                {
                    saveFileDialog.Filter = "WAV files (*.wav)|*.wav|RAW files (*.raw)|*.raw";
                    saveFileDialog.FileName = $"{selectedFileID:X8}.wav";
                }
            }
            else
            {
                saveFileDialog.Filter = "RAW files (*.raw)|*.raw";
                saveFileDialog.FileName = $"{selectedFileID:X8}.raw";
            }

            var success = saveFileDialog.ShowDialog();

            if (success != true) return;

            var saveFilename = saveFileDialog.FileName;

            if (isModel && saveFileDialog.FilterIndex == 1)
                FileExport.ExportModel(selectedFileID, saveFilename);
            else if (isModel && saveFileDialog.FilterIndex > 1)
            {
                // try to get animation id, if applicable
                var rawState = ModelViewer.Instance?.ViewObject?.PhysicsObj?.MovementManager?.MotionInterpreter?.RawState;

                MotionData motionData = null;

                if (rawState != null)
                {
                    var didTable = DIDTables.Get(selectedFileID);   // setup ID

                    if (didTable != null)
                    {
                        motionData = ACE.Server.Physics.Animation.MotionTable.GetMotionData(didTable.MotionTableID, rawState.ForwardCommand, rawState.CurrentStyle) ??
                            ACE.Server.Physics.Animation.MotionTable.GetLinkData(didTable.MotionTableID, rawState.ForwardCommand, rawState.CurrentStyle);
                    }
                }

                //FileExport.ExportModel_Aspose(selectedFileID, motionData, saveFilename);
                FileExport.ExportModel_Assimp(selectedFileID, motionData, saveFilename);
            }
            else if (isImage && saveFileDialog.FilterIndex == 1)
                FileExport.ExportImage(selectedFileID, saveFilename);
            else if (isSound && saveFileDialog.FilterIndex == 1)
                FileExport.ExportSound(selectedFileID, saveFilename);
            else
                FileExport.ExportRaw(DatType.Portal, selectedFileID, saveFilename);
        }

        public static void ReadDATFile(string filename)
        {
            var fi = new System.IO.FileInfo(filename);
            var di = fi.Attributes.HasFlag(FileAttributes.Directory) ? new DirectoryInfo(filename) : fi.Directory;

            ConfigManager.Config.ACFolder = di.FullName;

            var loadCell = true;

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            DatManager.Initialize(di.FullName, true, loadCell);
        }

        private void Options_Click(object sender, RoutedEventArgs e)
        {
            Options = new Options();
            Options.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Options.ShowDialog();
        }

        private void WorldMap_Click(object sender, RoutedEventArgs e)
        {
            if (DatManager.CellDat == null || DatManager.PortalDat == null)
                return;

            if (GameView.ViewMode == ViewMode.Map)
            {
                GameView.ViewMode = ViewMode.World;
                optionWorldMap.IsChecked = false;
            }
            else
            {
                MapViewer.Instance.Init();
                optionWorldMap.IsChecked = true;
            }
        }

        private void ShowMinimap_Click(object sender, RoutedEventArgs e)
        {
            ToggleMinimap();
        }

        private void ShowParticles_Click(object sender, RoutedEventArgs e)
        {
            ToggleParticles();
        }

        private void DisableDungeons_Click(object sender, RoutedEventArgs e)
        {
            ConfigManager.Config.Toggles.DisableDungeons = !ConfigManager.Config.Toggles.DisableDungeons;
            optionDisableDungeons.IsChecked = ConfigManager.Config.Toggles.DisableDungeons;
            
            if (WorldViewer.Instance != null && WorldViewer.Instance.SingleBlock != uint.MaxValue)
                WorldViewer.Instance.LoadLandblock(WorldViewer.Instance.SingleBlock);
        }

        public static bool ToggleHUD(bool updateConfig = true)
        {
            ShowHUD = !ShowHUD;

            if (updateConfig)
            {
                ConfigManager.Config.Toggles.ShowHUD = ShowHUD;
                ConfigManager.SaveConfig();
            }

            return ShowHUD;
        }

        public static bool ToggleParticles(bool updateConfig = true)
        {
            ShowParticles = !ShowParticles;
            Instance.optionShowParticles.IsChecked = ShowParticles;

            if (updateConfig)
            {
                ConfigManager.Config.Toggles.ShowParticles = ShowParticles;
                ConfigManager.SaveConfig();
            }

            if (GameView.ViewMode == ViewMode.World)
            {
                if (ShowParticles && !GameView.Render.ParticlesInitted)
                    GameView.Render.InitEmitters();

                if (!ShowParticles && GameView.Render.ParticlesInitted)
                    GameView.Render.DestroyEmitters();
            }
            return ShowParticles;
        }

        public static bool ToggleMinimap(bool updateConfig = true)
        {
            ShowMinimap = !ShowMinimap;
            Instance.optionShowMinimap.IsChecked = ShowMinimap;

            if (updateConfig)
            {
                ConfigManager.Config.Toggles.ShowMinimap = ShowMinimap;
                ConfigManager.SaveConfig();
            }

            return ShowMinimap;
        }

        private void ShowLocation_Click(object sender, RoutedEventArgs e)
        {
            if (WorldViewer.Instance != null)
                WorldViewer.Instance.ShowLocation();
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            var about = new About();
            about.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            about.ShowDialog();
        }

        private void Teleport_Click(object sender, RoutedEventArgs e)
        {
            var teleport = new Teleport();
            teleport.ShowDialog();
        }


        private string GetFileTypeLabel(uint did)
        {
            var fileTypeID = did >> 24;
            var fileTypeName = "Unknown";

            // Check FileExplorer's FileTypes list
            if (FileExplorer.FileTypes != null)
            {
                // First try exact match (for special IDs like CharGen)
                var exactMatch = FileExplorer.FileTypes.FirstOrDefault(ft => ft.ID == did);
                if (exactMatch != null)
                {
                    fileTypeName = exactMatch.Name;
                }
                else
                {
                    // Then try by high byte
                    var typeMatch = FileExplorer.FileTypes.FirstOrDefault(ft => ft.ID == fileTypeID);
                    if (typeMatch != null)
                    {
                        fileTypeName = typeMatch.Name;
                    }
                    else
                    {
                        // Special cases for Cell and Landblock
                        if ((did & 0xFFFF) == 0xFFFF)
                            fileTypeName = "Landblock";
                        else if ((did & 0xFFFF) == 0xFFFE)
                            fileTypeName = "LandblockInfo";
                        else if (fileTypeID == 0)
                            fileTypeName = "EnvCell";
                    }
                }
            }

            return $"{fileTypeName} - 0x{did:X8}";
        }

        private void PlayerMode_Click(object sender, RoutedEventArgs e)
        {
            if (WorldViewer.Instance.PlayerMode)
            {
                WorldViewer.Instance.ExitPlayerMode();
                optionPlayerMode.IsChecked = false;
            }
            else
            {
                var success = WorldViewer.Instance.EnterPlayerMode();
                optionPlayerMode.IsChecked = success;

                if (success)
                    MainWindow.Instance.AddStatusText("Player mode enabled");
                else
                    MainWindow.Instance.AddStatusText("Failed to enter player mode - no valid position");
            }
        }
    }
}
