using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;

namespace ACViewer
{
    public class ThemeManager
    {
        private static string CurrentTheme { get; set; }

        private static Collection<ResourceDictionary> MergedDictionaries => Application.Current.Resources.MergedDictionaries;


        public static void SetTheme(string themeName)
        {
            if (string.IsNullOrEmpty(themeName) || themeName.Equals("Default"))
                themeName = "Dark Grey";
            
            CurrentTheme = themeName.Replace(" ", "");

            var themeDictionary = new ResourceDictionary() { Source = new Uri($"/View/Themes/ColorDictionaries/{CurrentTheme}Theme.xaml", UriKind.Relative) };

            if (MergedDictionaries.Count < 1)
                MergedDictionaries.Add(themeDictionary);
            else
                MergedDictionaries[0] = themeDictionary;

            var controlColors = new ResourceDictionary() { Source = new Uri("/View/Themes/ControlColors.xaml", UriKind.Relative) };

            if (MergedDictionaries.Count < 2)
                MergedDictionaries.Add(controlColors);
            else
                MergedDictionaries[1] = controlColors;

            var controls = new ResourceDictionary { Source = new Uri("/View/Themes/Controls.xaml", UriKind.Relative) };

            if (MergedDictionaries.Count < 3)
                MergedDictionaries.Add(controls);
            else
                MergedDictionaries[2] = controls;

            RefreshFontColor();
        }

        public static void RefreshFontColor()
        {
            var fontColor = Config.ConfigManager.Config.BackgroundColors.FontColor;
            var brush = new SolidColorBrush(Color.FromArgb(255, fontColor.R, fontColor.G, fontColor.B));
            
            Application.Current.Resources["ABrush.Foreground.Static"] = brush;
        }
    }
}
