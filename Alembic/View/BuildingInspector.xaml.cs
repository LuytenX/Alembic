using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace ACViewer.View
{
    /// &lt;summary&gt;
    /// Interaction logic for BuildingInspector.xaml
    /// &lt;/summary&gt;
    public partial class BuildingInspector : Window
    {
        public BuildingInspector()
        {
            InitializeComponent();
        }

        public void SetBuildingStructure(string structureInfo)
        {
            txtStructureInfo.Text = structureInfo;
        }

        private void BtnCopyAll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(txtStructureInfo.Text);
                lblStatus.Text = "Structure copied to clipboard!";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error copying to clipboard: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveFileDialog saveDialog = new SaveFileDialog
                {
                    Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                    FileName = $"building_structure_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    File.WriteAllText(saveDialog.FileName, txtStructureInfo.Text, Encoding.UTF8);
                    lblStatus.Text = $"Structure exported to {saveDialog.FileName}";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting structure: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}