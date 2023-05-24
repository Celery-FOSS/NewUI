﻿using Celery.Controls;
using Celery.Utils;
using Microsoft.Win32;
using System.Collections.Generic;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Reflection;
using System.IO.Compression;
using Celery.Settings;
using System.Windows.Markup;
using System.Linq;
using System.Windows.Media;
using System.Threading.Tasks;
using Celery.CeleryAPI;
using System.Threading;

namespace Celery
{
    public partial class MainWindow : Window
    {
        public CeleryAPI.CeleryAPI Celery;
        public WebClient WebClient;
        public bool StartupAnimation;

        private bool _saveTabs = true;
        private bool _autoAttach = false;
        public bool DebuggingMode = false;

        public Dictionary<string, ResourceDictionary> Themes;

        public MainWindow()
        {
            InitializeComponent();
            WebClient = new WebClient();
            WebClient.Headers["User-Agent"] = "Celery WebClient";
            Tabs.GetContent = Tabs_GetContent;

            MessageBoxUtils.BaseGrid = BaseGrid;
            MessageBoxUtils.BlurGrid = BlurGrid;
            MessageBoxUtils.Tabs = Tabs;

            if (!Directory.Exists(Config.SettingsPath))
                Directory.CreateDirectory(Config.SettingsPath);
            if (!Directory.Exists(Config.ScriptsPath)) 
                Directory.CreateDirectory(Config.ScriptsPath);
            if (!Directory.Exists(Config.ThemesPath))
                Directory.CreateDirectory(Config.ThemesPath);

            ExtractZipFromResources("Ace", Properties.Resources.Ace, "\\bin");
            ExtractZipFromResources("dll", Properties.Resources.dll, "");

            new SettingsSaveManager(Config.SettingsFilePath);

            Themes = new Dictionary<string, ResourceDictionary>();
            ResourceDictionary defaultTheme = Application.Current.Resources.MergedDictionaries[0];
            Themes.Add("Default", defaultTheme);
            foreach (string file in Directory.GetFiles(Config.ThemesPath, "*.xaml"))
            {
                using (FileStream fs = new FileStream(file, FileMode.Open))
                {
                    try
                    {
                        ResourceDictionary rd = (ResourceDictionary)XamlReader.Load(fs);
                        Themes.Add(Path.GetFileNameWithoutExtension(file), rd);
                    }
                    catch { }
                }
            }

            DispatcherTimer saveTimer = new DispatcherTimer();
            saveTimer.Tick += async (s, e) =>
            {
                if (_saveTabs)
                    await SaveTabs();
            };
            saveTimer.Interval = new TimeSpan(0, 0, 1, 0, 0);

            SettingsMenu.AddSettings(
                new BooleanSetting("Topmost", "topmost", false, onChange: (value) =>
                {
                    Topmost = value;
                }),
                new BooleanSetting("Auto Attach", "autoattach", false, onChange: (value) =>
                {
                    _autoAttach = value;
                }),
                new BooleanSetting("Startup Animation", "startupanimation", true, onChange: (value) =>
                {
                    StartupAnimation = value;
                }),
                new BooleanSetting("Debugging Mode", "debuggingmode", false, onChange: (value) =>
                {
                    DebuggingMode = value;
                }),
                new DropdownSetting("Theme", "theme", Themes.Keys.ToList(), "Default", async (value, isInit) =>
                {
                    ResourceDictionary theme = value == "Default" || !Themes.ContainsKey(value) ? defaultTheme : Themes[value];

                    Application.Current.Resources.Clear();
                    Application.Current.Resources.MergedDictionaries.Add(theme);

                    string acePath = Path.Combine(Config.BinPath, "Ace", "js", "ace");
                    string templatepath = Path.Combine(acePath, "theme-template.js");
                    if (!File.Exists(templatepath))
                    {
                        await MessageBoxUtils.ShowMessage("File not found", "The theme-template.js file is missing.", true, MessageBoxButtons.Ok);
                        return;
                    }

                    try
                    {
                        string template = File.ReadAllText(templatepath);
                        ResourceDictionary resources = Application.Current.Resources;
                        template = template.Replace("{background}", ((Color)resources["BackgroundColor"]).ToString().Replace("#FF", "#"));
                        template = template.Replace("{foreground}", ((Color)resources["ForegroundColor"]).ToString().Replace("#FF", "#"));
                        template = template.Replace("{bordercolor}", ((Color)resources["BorderColor"]).ToString().Replace("#FF", "#"));
                        template = template.Replace("{lightbordercolor}", ((Color)resources["LightBorderColor"]).ToString().Replace("#FF", "#"));
                        template = template.Replace("{darkforeground}", ((Color)resources["DarkForegroundColor"]).ToString().Replace("#FF", "#"));
                        template = template.Replace("{highlightcolor}", ((Color)resources["HighlightColor"]).ToString().Replace("#FF", "#"));
                        template = template.Replace("{lightbackground}", ((Color)resources["LightBackgroundColor"]).ToString().Replace("#FF", "#"));
                        File.WriteAllText(Path.Combine(acePath, "theme-celery.js"), template);
                    } catch (Exception ex)
                    {
                        await MessageBoxUtils.ShowMessage("Error", "Un unexpected error occured while creating the editor, Error: " + ex.Message, true, MessageBoxButtons.Ok);
                        return;
                    }

                    if (!isInit)
                    {
                        Utils.MessageBoxResult result = await MessageBoxUtils.ShowMessage("Restart Required", "Not all colors are applied, a restart is required for all changes to take effect. Would you like to restart now?", false, MessageBoxButtons.YesNo);
                        if (result == Utils.MessageBoxResult.Yes)
                        {
                            await SaveTabs();
                            Process.Start(Assembly.GetExecutingAssembly().Location);
                            Application.Current.Shutdown();
                        }
                    }
                }),
                new BooleanSetting("Save Tabs", "savetabs", _saveTabs, (value) =>
                {
                    _saveTabs = value;
                }),
                new NumberSetting("Save Tabs Delay", "savetabsdelay", 60, 10, 600, 1, (value) =>
                {
                    saveTimer.Stop();
                    saveTimer.Interval = new TimeSpan(0, 0, 0, (int)value, 0);
                    saveTimer.Start();
                })
            );

            if (_saveTabs)
                LoadTabs();
            else
                Tabs.MakeTab();
            saveTimer.Start();

            DispatcherTimer autoAttachTimer = new DispatcherTimer();
            autoAttachTimer.Tick += (s, e) =>
            {
                if (_autoAttach)
                {
                    Celery.Inject(false);
                }
            };
            autoAttachTimer.Interval = new TimeSpan(0, 0, 0, 0, 3000);
            autoAttachTimer.Start();

            CreateFileWatcher();
            Celery = new CeleryAPI.CeleryAPI();

            CheckUpdates();
        }

        public async Task SaveTabs()
        {
            if (!_saveTabs)
                return;

            // Clear tabs folder
            if (Directory.Exists(Config.TabsPath))
                Directory.Delete(Config.TabsPath, true);
            Directory.CreateDirectory(Config.TabsPath);

            for (int i = 0; i < Tabs.Items.Count; i++)
            {
                TabItem tab = (TabItem)Tabs.Items[i];
                Editor editor = (Editor)tab.Content;
                string content = await editor.GetText();
                File.WriteAllText(Path.Combine(Config.TabsPath, $"{i}.txt"), content);
            }
        }

        public void LoadTabs()
        {
            if (!Directory.Exists(Config.TabsPath))
            {
                Tabs.MakeTab();
                return;
            }

            foreach (string file in Directory.GetFiles(Config.TabsPath))
            {
                Tabs.MakeTab(File.ReadAllText(file));
            }
        }

        public void ExtractZipFromResources(string name, byte[] resource, string path)
        {
            if (!Directory.Exists(Config.ApplicationPath + path + "\\" + name))
            {
                Directory.CreateDirectory(Config.ApplicationPath + path + "\\" + name);
                try
                {
                    File.WriteAllBytes($"{Config.ApplicationPath}\\{name}.zip", resource);
                    ZipFile.ExtractToDirectory($"{Config.ApplicationPath}\\{name}.zip", Config.ApplicationPath + path + "\\" + name);
                    File.Delete($"{Config.ApplicationPath}\\{name}.zip");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
            }
        }

        public async void CheckUpdates()
        {
            Dictionary<string, object> latest;
            Version currentVersion;
            Version latestVersion;
            try
            {
                string json = WebClient.DownloadString("https://api.github.com/repos/sten-code/Celery/releases/latest");
                latest = json.FromJson<Dictionary<string, object>>();

                currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
                latestVersion = Version.Parse((string)latest["tag_name"]);
            }
            catch
            {
                await MessageBoxUtils.ShowMessage("Error", "Couldn't check for updates", true, MessageBoxButtons.Ok);
                return;
            }

            if (currentVersion.CompareTo(latestVersion) >= 0)
                return;

            if (await MessageBoxUtils.ShowMessage("Update", "A new update was detected, do you want to update?", false, MessageBoxButtons.YesNo) == Utils.MessageBoxResult.No)
                return;

            try
            {
                List<Dictionary<string, object>> assets = (List<Dictionary<string, object>>)latest["assets"];
                string downloadUrl = (string)assets[0]["browser_download_url"];

                if (!Directory.Exists(Config.SettingsPath))
                    Directory.CreateDirectory(Config.SettingsPath);

                File.WriteAllBytes(Config.UpdaterPath, Properties.Resources.CeleryUpdater);
                Process updater = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = Config.UpdaterPath,
                        Arguments = $"\"{downloadUrl}\" \"{Config.ApplicationPath}\" \"{Process.GetCurrentProcess().Id}\"",
                        Verb = "runas"
                    }
                };
                updater.Start();
            }
            catch (Exception ex)
            {
                await MessageBoxUtils.ShowMessage("Error", $"There was an error while trying to start update. Error: {ex.Message}", true, MessageBoxButtons.Ok);
            }
        }

        private ListBoxItem CreateScriptItem(string header)
        {
            MenuItem open = new MenuItem
            {
                Header = "Open"
            };
            open.Click += (s, e) =>
            {
                string name = ((ListBoxItem)((ContextMenu)open.Parent).PlacementTarget).Content.ToString();
                Tabs.MakeTab(File.ReadAllText(Path.Combine(Config.ScriptsPath, name + ".lua")), name);
            };
            ListBoxItem item = new ListBoxItem
            {
                Content = header,
                ContextMenu = new ContextMenu
                {
                    Items =
                    {
                        open
                    }
                }
            };
            item.MouseDoubleClick += ScriptItem_MouseDoubleClick;
            return item;
        }

        public void CreateFileWatcher()
        {
            ScriptList.Items.SortDescriptions.Clear();
            ScriptList.Items.SortDescriptions.Add(new SortDescription("Content", ListSortDirection.Ascending));
            foreach (string filename in Directory.GetFiles(Config.ScriptsPath))
            {
                ScriptList.Items.Add(CreateScriptItem(Path.GetFileNameWithoutExtension(filename)));
            }

            FileSystemWatcher watcher = new FileSystemWatcher(Config.ScriptsPath)
            {
                NotifyFilter = NotifyFilters.FileName,
                Filter = "*.lua",
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            watcher.Created += (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    ScriptList.Items.Add(CreateScriptItem(Path.GetFileNameWithoutExtension(e.FullPath)));
                    ScriptList.Items.Refresh();
                });
            };
            watcher.Deleted += (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    string itemRemove = Path.GetFileNameWithoutExtension(e.FullPath);
                    for (int i = ScriptList.Items.Count - 1; i >= 0; --i)
                    {
                        if (((ListBoxItem)ScriptList.Items[i]).Content.ToString() == itemRemove)
                        {
                            ScriptList.Items.RemoveAt(i);
                        }
                    }
                    ScriptList.Items.Refresh();
                });
            };
            watcher.Renamed += (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    string itemRemove = Path.GetFileNameWithoutExtension(e.OldFullPath);
                    Debug.WriteLine(itemRemove);
                    for (int i = ScriptList.Items.Count - 1; i >= 0; --i)
                    {
                        if (((ListBoxItem)ScriptList.Items[i]).Content.ToString() == itemRemove)
                        {
                            ScriptList.Items.RemoveAt(i);
                        }
                    }
                    ScriptList.Items.Add(CreateScriptItem(Path.GetFileNameWithoutExtension(e.FullPath)));
                    ScriptList.Items.Refresh();
                });
            };
        }

        public UIElement Tabs_GetContent(string text)
        {
            return new AceEditor(text);
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private async void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            await SaveTabs();
            Application.Current.Shutdown();
        }

        private async void ExecuteButton_Click(object sender, RoutedEventArgs e)
        {
            Editor editor = (Editor)Tabs.SelectedContent;
            string script = await editor.GetText();
            Celery.Execute(script);
        }

        private void ScriptItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            Tabs.MakeTab(File.ReadAllText(Path.Combine(Config.ScriptsPath, ((ListBoxItem)sender).Content.ToString() + ".lua")), ((ListBoxItem)sender).Content.ToString());
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            await SaveTabs();
            if (Tabs.SelectedContent == null)
                return;

            SaveFileDialog sfd = new SaveFileDialog
            {
                FileName = ((TabItem)Tabs.SelectedValue).Header.ToString(),
                DefaultExt = ".lua",
                Filter = "Lua Files|*.lua",
                InitialDirectory = Config.ScriptsPath
            };

            if (sfd.ShowDialog() == true)
            {
                string filename = sfd.FileName;
                File.WriteAllText(filename, await ((Editor)Tabs.SelectedContent).GetText());
            }
        }

        private void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog
            {
                DefaultExt = ".lua",
                Filter = "Lua Files|*.lua|Text Files|*.txt",
                InitialDirectory = Config.ScriptsPath
            };

            if (ofd.ShowDialog() == true)
            {
                string filename = ofd.FileName;
                Tabs.MakeTab(File.ReadAllText(filename), Path.GetFileNameWithoutExtension(filename));
            }
        }

        public TextBox Console
        {
            get
            {
                return OutputBox;
            }
        }

        private void InjectButton_Click(object sender, RoutedEventArgs e)
        {
            Celery.Inject();
        }

        private bool SettingsVisible = false;

        private void ToggleSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsVisible = !SettingsVisible;
            if (SettingsVisible)
            {
                AnimationUtils.AnimateMargin(SettingsMenu, SettingsMenu.Margin, new Thickness(), AnimationUtils.EaseInOut);
            }
            else
            {
                AnimationUtils.AnimateMargin(SettingsMenu, SettingsMenu.Margin, new Thickness(0, 0, MainGrid.ColumnDefinitions[0].Width.Value, 0), AnimationUtils.EaseInOut);
            }
        }

        private void ScriptList_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // For some reason when you apply an animation to something you can't change that particular property anymore, the only way to change that property is with an animation
            if (!SettingsVisible)
                AnimationUtils.AnimateMargin(SettingsMenu, SettingsMenu.Margin, new Thickness(0, 0, MainGrid.ColumnDefinitions[0].Width.Value, 0), AnimationUtils.EaseInOut, 0);
        }

        private bool ScriptHubVisible = false;

        private void ToggleScriptHub_Click(object sender, RoutedEventArgs e)
        {
            ScriptHubVisible = !ScriptHubVisible;
            if (ScriptHubVisible)
            {
                if (Tabs.SelectedContent != null)
                    AnimationUtils.AnimateMargin((Editor)Tabs.SelectedContent, ((Editor)Tabs.SelectedContent).Margin, new Thickness(0, 0, Tabs.ActualWidth, 0), AnimationUtils.EaseInOut);
                AnimationUtils.AnimateMargin(ScriptHub, ScriptHub.Margin, new Thickness(), AnimationUtils.EaseInOut);
            }
            else
            {
                if (Tabs.SelectedContent != null)
                    AnimationUtils.AnimateMargin((Editor)Tabs.SelectedContent, ((Editor)Tabs.SelectedContent).Margin, new Thickness(0), AnimationUtils.EaseInOut);
                AnimationUtils.AnimateMargin(ScriptHub, ScriptHub.Margin, new Thickness(InsideGrid.ActualWidth, 0, 0, 0), AnimationUtils.EaseInOut);
            }
        }

        private void Tabs_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!ScriptHubVisible)
                AnimationUtils.AnimateMargin(ScriptHub, ScriptHub.Margin, new Thickness(0, 0, InsideGrid.ActualWidth, 0), AnimationUtils.EaseInOut, 0);
            else if (Tabs.SelectedContent != null)
                AnimationUtils.AnimateMargin((Editor)Tabs.SelectedContent, ((Editor)Tabs.SelectedContent).Margin, new Thickness(0, 0, InsideGrid.ActualWidth, 0), AnimationUtils.EaseInOut, 0);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            AnimationUtils.AnimateMargin(ScriptHub, ScriptHub.Margin, new Thickness(InsideGrid.ActualWidth, 0, 0, 0), AnimationUtils.EaseInOut, 0); // For some reason animations are just really weird, just don't change this

            ThreadPool.QueueUserWorkItem(new WaitCallback((o) =>
            {
                List<int> openedProcs = new List<int>();
                while (true)
                {
                    while (DebuggingMode)
                    {
                        List<ProcInfo> procs = ProcessUtil.openProcessesByName(Injector.InjectProcessName);
                        foreach (ProcInfo proc in procs)
                        {
                            if (!openedProcs.Contains(proc.processId))
                            {
                                openedProcs.Add(proc.processId);
                                Dispatcher.Invoke(() =>
                                {
                                    Logger.Log("Roblox has been opened.");
                                });
                                ThreadPool.QueueUserWorkItem((s) =>
                                {
                                    Process.GetProcessById(proc.processId).WaitForExit();
                                    Dispatcher.Invoke(() =>
                                    {
                                        Logger.Log("Roblox exited.");
                                    });
                                    openedProcs.Remove(proc.processId);
                                });
                            }
                        }
                        Thread.Sleep(1000);
                    }
                    Thread.Sleep(3000);
                }
            }));
        }
    }
}
