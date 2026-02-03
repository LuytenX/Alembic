using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

using ACViewer.Config;

namespace ACViewer.View
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public static MainWindow Instance { get; set; }
        private bool _suppressSync;

        public MainWindow()
        {
            InitializeComponent();
            Instance = this;
            DataContext = this;

            //WpfGame.UseASingleSharedGraphicsDevice = true;

            LoadConfig();

        }


        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public bool EditMode
        {
            get => Picker.EditMode;
            set 
            { 
                if (value)
                {
                    PaintMode = false;
                    ObjectMode = false;
                }
                Picker.EditMode = value; 
                OnPropertyChanged();
                AddStatusText($"Terrain Height Mode: {(value ? "ON" : "OFF")}"); 
            }
        }

        public bool PaintMode
        {
            get => Picker.PaintMode;
            set 
            { 
                if (value)
                {
                    EditMode = false;
                    ObjectMode = false;
                }
                Picker.PaintMode = value; 
                OnPropertyChanged();
                AddStatusText($"Paint Mode: {(value ? "ON" : "OFF")}"); 
            }
        }

        public bool ObjectMode
        {
            get => Picker.ObjectMode;
            set
            {
                if (value)
                {
                    EditMode = false;
                    PaintMode = false;
                }
                Picker.ObjectMode = value;
                OnPropertyChanged();
                AddStatusText($"Object Mode: {(value ? "ON" : "OFF")}");
            }
        }

        public bool FreeRoam
        {
            get => WorldViewer.FreeRoam;
            set
            {
                WorldViewer.FreeRoam = value;
                OnPropertyChanged();
                AddStatusText($"Free Roam: {(value ? "ON" : "OFF")}");
            }
        }

        public bool WorldView
        {
            get => WorldViewer.WorldView;
            set
            {
                WorldViewer.WorldView = value;
                OnPropertyChanged();
                AddStatusText($"World View: {(value ? "ON" : "OFF")}");
            }
        }

        private string _currentObjectIdHex = "00000000";
        public string CurrentObjectIdHex
        {
            get => _currentObjectIdHex;
            set
            {
                _currentObjectIdHex = value;
                if (uint.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out uint id))
                {
                    Picker.CurrentObjectId = id;
                }
                OnPropertyChanged();
            }
        }

        public double BrushSize
        {
            get => Picker.BrushSize;
            set 
            { 
                Picker.BrushSize = (float)value; 
                OnPropertyChanged();
            }
        }

        public double BrushStrength
        {
            get => Picker.BrushStrength;
            set
            {
                Picker.BrushStrength = (int)value;
                OnPropertyChanged();
            }
        }

        public bool EyeDropperMode
        {
            get => Picker.EyeDropperMode;
            set
            {
                Picker.EyeDropperMode = value;
                if (value)
                    PaintMode = true;
                OnPropertyChanged();
            }
        }

        public bool BuildingInspectionMode
        {
            get => Picker.BuildingInspectionMode;
            set
            {
                Picker.BuildingInspectionMode = value;
                OnPropertyChanged();
                AddStatusText($"Building Inspection Mode: {(value ? "ON" : "OFF")}");
            }
        }

        public void SetTerrainTypeFromEyeDropper(ushort terrainType)
        {
            // Update the terrain ID text box with the sampled terrain type
            Dispatcher.Invoke(() =>
            {
                TerrainIdText.Text = terrainType.ToString("X4");
                // Also update the combo box selection if possible
                var terrainItem = TerrainSelector.Items.Cast<object>().FirstOrDefault(item =>
                {
                    var terrain = item as dynamic; // This is a simplification
                    try
                    {
                        return terrain != null && terrain.Type == terrainType;
                    }
                    catch { return false; }
                });
                if (terrainItem != null)
                {
                    TerrainSelector.SelectedItem = terrainItem;
                }
            });
        }

        public void SetObjectIdFromEyeDropper(uint objectId)
        {
            // Update the object ID text box with the sampled object ID
            Dispatcher.Invoke(() =>
            {
                CurrentObjectIdHex = objectId.ToString("X8");
            });
        }



        public void PopulateTerrain()
        {
            if (TerrainSelector != null && ACE.DatLoader.DatManager.PortalDat?.RegionDesc?.TerrainInfo?.TerrainTypes != null)
            {
                Dispatcher.Invoke(() =>
                {
                    var datTypes = ACE.DatLoader.DatManager.PortalDat.RegionDesc.TerrainInfo.TerrainTypes;
                    var texMerge = ACE.DatLoader.DatManager.PortalDat.RegionDesc.TerrainInfo.LandSurfaces.TexMerge;

                    var items = new List<object>();
                    for (int i = 0; i < 32; i++)
                    {
                        var enumName = ((ACE.Server.Physics.Common.LandDefs.TerrainType)i).ToString();
                        var datName = i < datTypes.Count ? datTypes[i].TerrainName : null;

                        var displayName = datName;
                        if (string.IsNullOrEmpty(displayName) || displayName.Contains("Reserved", StringComparison.OrdinalIgnoreCase))
                            displayName = enumName;

                        // Try to find TexGID from the TexMerge mapping
                        uint texGID = 0;
                        if (texMerge != null && texMerge.TerrainDesc != null)
                        {
                            var tmDesc = texMerge.TerrainDesc.FirstOrDefault(t => (int)t.TerrainType == i);
                            if (tmDesc != null && tmDesc.TerrainTex != null)
                                texGID = tmDesc.TerrainTex.TexGID;
                        }

                        var item = new { TerrainName = $"{i:D2}: {displayName} ({(texGID != 0 ? "0x" + texGID.ToString("X8") : "None")})", Index = (ushort)i };
                        items.Add(item);
                    }

                                        TerrainSelector.ItemsSource = items;
                                        _suppressSync = true;
                                        TerrainSelector.SelectedIndex = 0;
                                        if (TerrainIdText != null) TerrainIdText.Text = "00";
                                        _suppressSync = false;
                                    });
                                }
                            }
                    
                                    private void TerrainSelector_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
                                    {
                                        if (_suppressSync) return;
                            
                                        if (TerrainSelector.SelectedIndex != -1)
                                        {
                                            PaintMode = true;
                            
                                            // Shift the index by 2 to get the base hex value (bits 2-6)
                                            ushort rawVal = (ushort)(TerrainSelector.SelectedIndex << 2);
                                            Picker.CurrentTerrainType = rawVal;
                                            
                                            _suppressSync = true;
                                            if (TerrainIdText != null) TerrainIdText.Text = rawVal.ToString("X2");
                                            _suppressSync = false;
                                        }
                                    }                    
                            private void TerrainIdText_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
                            {
                                if (_suppressSync) return;
                    
                                // Parse as Hex
                                if (ushort.TryParse(TerrainIdText.Text, System.Globalization.NumberStyles.HexNumber, null, out ushort val))
                                {
                                    Picker.CurrentTerrainType = val;
                    
                                    _suppressSync = true;
                                    // Update dropdown to show the base terrain type (bits 2-6)
                                    int index = (val >> 2) & 0x1F;
                                    if (index < TerrainSelector.Items.Count)
                                        TerrainSelector.SelectedIndex = index;
                                    _suppressSync = false;
                                }
                            }
        private static Config.Config Config => ConfigManager.Config;
        
        private static void LoadConfig()
        {
            ConfigManager.LoadConfig();

            if (Config.AutomaticallyLoadDATsOnStartup)
            {
                MainMenu.Instance.LoadDATs(Config.ACFolder);
            }

            if (ConfigManager.HasDBInfo)

            // Synchronize menu checkmark with config state
            MainMenu.Instance.optionShowMinimap.IsChecked = ConfigManager.Config.Toggles.ShowMinimap;
            // The Draw loop already uses MainMenu.ShowMinimap which is synced via ToggleMinimap if called,
            // or we can just set the static property here.
            MainMenu.ShowMinimap = ConfigManager.Config.Toggles.ShowMinimap;

            if (ConfigManager.Config.Toggles.ShowParticles)
                MainMenu.ToggleParticles(false);


            if (ConfigManager.Config.Theme != null)
                ThemeManager.SetTheme(ConfigManager.Config.Theme);
        }

        private DateTime lastUpdateTime { get; set; }

        private static readonly TimeSpan maxUpdateInterval = TimeSpan.FromMilliseconds(1000);

        private readonly List<string> statusLines = new List<string>();

        private static readonly int maxLines = 100;

        private bool pendingUpdate { get; set; }

        public bool SuppressStatusText { get; set; }

        public async void AddStatusText(string line)
        {
            if (SuppressStatusText) return;
            
            statusLines.Add(line);

            var timeSinceLastUpdate = DateTime.Now - lastUpdateTime;

            if (timeSinceLastUpdate < maxUpdateInterval)
            {
                if (pendingUpdate)
                    return;

                pendingUpdate = true;
                await Task.Delay((int)maxUpdateInterval.TotalMilliseconds);
                pendingUpdate = false;
            }

            if (statusLines.Count > maxLines)
                statusLines.RemoveRange(0, statusLines.Count - maxLines);

            Status.Text = string.Join("\n", statusLines);
            Status.ScrollToEnd();

            lastUpdateTime = DateTime.Now;
        }

        public ICommand FindCommand { get; } = new ActionCommand(() =>
        {
            var finder = new Finder();
            finder.ShowDialog();
        });

        public ICommand TeleportCommand { get; } = new ActionCommand(() =>
        {
            var teleport = new Teleport();
            teleport.ShowDialog();
        });

        public ICommand OpenMapCommand { get; } = new ActionCommand(() =>
        {
            WorldViewer.Instance?.LoadAllLandblocks();
        });

        public ICommand JumpCommand { get; } = new ActionCommand(() =>
        {
            if (Instance != null && !string.IsNullOrEmpty(Instance.JumpLbidText.Text))
                WorldViewer.Instance?.JumpToLandblock(Instance.JumpLbidText.Text);
        });

        public static bool DebugMode { get; set; }
        
        public ICommand DebugCommand { get; } = new ActionCommand(() =>
        {
            DebugMode = !DebugMode;

            Console.WriteLine($"Debug mode {(DebugMode ? "enabled" : "disabled")}");
        });

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {

            Config.WindowPos.X = (int)Left;
            Config.WindowPos.Y = (int)Top;
            Config.WindowPos.Width = (int)Width;
            Config.WindowPos.Height = (int)Height;
            Config.WindowPos.VSplit = (int)VSplit.Width.Value;
            Config.WindowPos.HSplit = (int)HSplit.Height.Value;
            Config.WindowPos.IsMaximized = WindowState == WindowState.Maximized;

            ConfigManager.SaveConfig();
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            if (Config.WindowPos.X == int.MinValue)
                return;

            // Update to new default if it was the old one
            if (Config.WindowPos.VSplit == 360 || Config.WindowPos.VSplit == 0)
                Config.WindowPos.VSplit = 180;

            Left =  Config.WindowPos.X;
            Top = Config.WindowPos.Y;
            Width = Config.WindowPos.Width;
            Height = Config.WindowPos.Height;
            VSplit.Width = new GridLength(Config.WindowPos.VSplit, GridUnitType.Pixel);
            HSplit.Height = new GridLength(Config.WindowPos.HSplit, GridUnitType.Pixel);

            if (Config.WindowPos.IsMaximized)
                WindowState = WindowState.Maximized;
        }
    }
}
