﻿using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Windows;

namespace WoWExportTools
{
    /// <summary>
    /// Interaction logic for ConfigurationWindow.xaml
    /// </summary>
    public partial class ConfigurationWindow : Window
    {
        private bool wowFound = false;
        private string wowLoc;
        private bool _editMode;
        private bool needsRestart = false;

        public ConfigurationWindow(bool editMode = false)
        {
            _editMode = editMode;

            InitializeComponent();

            if (editMode)
            {
                // Load existing configuration values
                var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                basedirLabel.Content = config.AppSettings.Settings["basedir"].Value;
                outdirLabel.Content = config.AppSettings.Settings["outdir"].Value;

                OBJCheckbox.IsChecked = true;

                if (string.IsNullOrWhiteSpace(config.AppSettings.Settings["basedir"].Value))
                {
                    onlineMode.IsChecked = true;
                    localMode.IsChecked = false;
                }
                else
                {
                    onlineMode.IsChecked = false;
                    localMode.IsChecked = true;
                }

                ADTExportTab.IsEnabled = true;
                WMOExportTab.IsEnabled = true;
                M2ExportTab.IsEnabled = true;

                if (string.IsNullOrWhiteSpace(config.AppSettings.Settings["textureMetadata"].Value) || config.AppSettings.Settings["textureMetadata"].Value == "False")
                {
                    textureMetadata.IsChecked = false;
                }
                else
                {
                    textureMetadata.IsChecked = true;
                }
            }
            else
            {
                // Initial config
                try
                {
                    using (var key = Registry.LocalMachine.OpenSubKey("SOFTWARE\\WOW6432Node\\Blizzard Entertainment\\World of Warcraft"))
                    {
                        if (key != null)
                        {
                            var obj = key.GetValue("InstallPath");
                            if (obj != null)
                            {
                                wowLoc = (string)obj;
                                wowLoc = wowLoc.Replace("_retail_", "").Replace("_classic_", "").Replace("_ptr_", "").Replace("_classic_beta_", "").Replace("_beta_", "");
                                wowFound = true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Unable to get WoW install path from registry. Falling back to online mode! Error: " + ex.Message);
                }


                if (wowFound)
                {
                    basedirLabel.Content = wowLoc;
                    localMode.IsChecked = true;
                    onlineMode.IsChecked = false;
                }
                else
                {
                    onlineMode.IsChecked = true;
                    localMode.IsChecked = false;
                }
            }
        }

        private void Mode_Checked(object sender, RoutedEventArgs e)
        {
            if(basedirLabel == null) { return; }
            if ((bool) onlineMode.IsChecked)
            {
                onlineLabel.Visibility = Visibility.Visible;
                localLabel.Visibility = Visibility.Hidden;
                basedirBrowse.Visibility = Visibility.Hidden;
                basedirLabel.Visibility = Visibility.Hidden;
            }
            else
            {
                onlineLabel.Visibility = Visibility.Hidden;
                localLabel.Visibility = Visibility.Visible;
                basedirBrowse.Visibility = Visibility.Visible;
                basedirLabel.Visibility = Visibility.Visible;
            }
        }

        private void BasedirBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            var result = dialog.ShowDialog();
            if (result.ToString() == "OK")
            {
                if (File.Exists(Path.Combine(dialog.SelectedPath, ".build.info")))
                {
                    basedirLabel.Content = dialog.SelectedPath;
                }
                else
                {
                    basedirLabel.Content = "Could not find a WoW client there!";
                }
            }
        }

        private void ProgramSelect_Loaded(object sender, RoutedEventArgs e)
        {
            programSelect.Items.Add(new KeyValuePair<string, string>("Live (Retail)", "wow"));
            programSelect.Items.Add(new KeyValuePair<string, string>("Public Test Realm (PTR)", "wowt"));
            programSelect.Items.Add(new KeyValuePair<string, string>("Beta", "wow_beta"));
            programSelect.Items.Add(new KeyValuePair<string, string>("Classic (Retail)", "wow_classic"));
            //programSelect.Items.Add(new KeyValuePair<string, string>("Classic Beta", "wow_classic_beta"));
            programSelect.Items.Add(new KeyValuePair<string, string>("Submission (unknown use)", "wowz"));
            programSelect.DisplayMemberPath = "Key";
            if (_editMode)
            {
                var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                foreach (KeyValuePair<string, string> item in programSelect.Items)
                {
                    if (item.Value == config.AppSettings.Settings["program"].Value)
                    {
                        programSelect.SelectedItem = item;
                    }
                }
            }
            else
            {
                programSelect.SelectedIndex = 0;
            }
        }

        private void ExportMode_Checked(object sender, RoutedEventArgs e)
        {
            if (OBJLabel == null) { return; }
            if ((bool)OBJCheckbox.IsChecked)
            {
                OBJLabel.Visibility = Visibility.Visible;
            }
            else
            {
                OBJLabel.Visibility = Visibility.Hidden;
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

            var error = false;

            if ((bool)onlineMode.IsChecked)
            {
                // Online mode
                if (programSelect.SelectedValue == null) {
                    error = true;
                }
                else
                {
                    if (config.AppSettings.Settings["basedir"].Value != "" || config.AppSettings.Settings["program"].Value != ((KeyValuePair<string, string>)programSelect.SelectedValue).Value)
                    {
                        needsRestart = true;
                    }
                    config.AppSettings.Settings["basedir"].Value = "";
                    config.AppSettings.Settings["program"].Value = ((KeyValuePair<string, string>)programSelect.SelectedValue).Value;
                    config.AppSettings.Settings["firstrun"].Value = "false";
                }
            }
            else
            {
                // Local mode

                if ((string)basedirLabel.Content == "No WoW directory set" || (string)basedirLabel.Content == "Could not find a WoW client there!")
                {
                    error = true;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(config.AppSettings.Settings["basedir"].Value) || config.AppSettings.Settings["basedir"].Value != (string)basedirLabel.Content)
                    {
                        needsRestart = true;
                    }
                    config.AppSettings.Settings["basedir"].Value = (string)basedirLabel.Content;
                    config.AppSettings.Settings["program"].Value = ((KeyValuePair<string, string>)programSelect.SelectedValue).Value;
                    config.AppSettings.Settings["firstrun"].Value = "false";
                }
            }

            if((string) outdirLabel.Content != "No export directory set, using application folder")
            {
                if (Directory.Exists((string) outdirLabel.Content))
                {
                    config.AppSettings.Settings["outdir"].Value = (string) outdirLabel.Content;
                }
                else
                {
                    error = true;
                }
            }

            if ((bool)textureMetadata.IsChecked)
            {
                config.AppSettings.Settings["textureMetadata"].Value = "True";
            }
            else
            {
                config.AppSettings.Settings["textureMetadata"].Value = "False";
            }

            if (!error)
            {
                config.Save(ConfigurationSaveMode.Full);
                if (needsRestart)
                {
                    System.Diagnostics.Process.Start(Application.ResourceAssembly.Location);
                    MainWindow.shuttingDown = true;
                    Application.Current.Shutdown();
                }
                else
                {
                    Close();
                }
            }
            else
            {
                MessageBox.Show("Not all settings are set! Did you forget to set an export location?", "Confirmation", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OutdirBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            var result = dialog.ShowDialog();
            if (result.ToString() == "OK")
            {
                if((string)outdirLabel.Content != dialog.SelectedPath)
                {
                    outdirLabel.Content = dialog.SelectedPath;
                    needsRestart = true;
                }
            }
        }
    }
}
