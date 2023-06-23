using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using ModAssistant.Libs;
using static ModAssistant.Http;
using TextBox = System.Windows.Controls.TextBox;

namespace ModAssistant.Pages
{
    /// <summary>
    /// Interaction logic for Mods.xaml
    /// </summary>
    public sealed partial class Mods : Page
    {
        public static Mods Instance = new Mods();

        //public List<string> DefaultMods = new List<string> { "SongCore", "WhyIsThereNoLeaderboard", "BeatSaverDownloader", "BeatSaverVoting", "PlaylistManager" };
        public List<string> DefaultMods = new List<string>();
        public Mod[] ModsList;
        public Mod[] AllModsList;
        public static List<InstalledMod> InstalledMods = new List<InstalledMod>();
        //public static List<Mod> ManifestsToMatch = new List<Mod>();
        public List<string> CategoryNames = new List<string>();
        public CollectionView view;
        public bool PendingChanges;

        private readonly SemaphoreSlim _modsLoadSem = new SemaphoreSlim(1, 1);

        public List<ModListItem> ModList { get; set; }

        public Mods()
        {
            InitializeComponent();
        }

        private void RefreshModsList()
        {
            if (view != null)
            {
                view.Refresh();
            }
        }

        public void RefreshColumns()
        {
            if (MainWindow.Instance.Main.Content != Instance) return;
            double viewWidth = ModsListView.ActualWidth;
            double totalSize = 0;
            GridViewColumn description = null;

            if (ModsListView.View is GridView grid)
            {
                foreach (var column in grid.Columns)
                {
                    if (column.Header?.ToString() == FindResource("Mods:Header:Description").ToString())
                    {
                        description = column;
                    }
                    else
                    {
                        totalSize += column.ActualWidth;
                    }
                    if (double.IsNaN(column.Width))
                    {
                        column.Width = column.ActualWidth;
                        column.Width = double.NaN;
                    }
                }
                double descriptionNewWidth = viewWidth - totalSize - 35;
                description.Width = descriptionNewWidth > 200 ? descriptionNewWidth : 200;
            }
        }

        public async Task LoadMods()
        {
            var versionLoadSuccess = await MainWindow.Instance.VersionLoadStatus.Task;
            if (versionLoadSuccess == false) return;

            await _modsLoadSem.WaitAsync();

            try
            {
                MainWindow.Instance.InstallButton.IsEnabled = false;
                MainWindow.Instance.GameVersionsBox.IsEnabled = false;
                MainWindow.Instance.InfoButton.IsEnabled = false;

                if (ModsList != null)
                {
                    Array.Clear(ModsList, 0, ModsList.Length);
                }

                if (AllModsList != null)
                {
                    Array.Clear(AllModsList, 0, AllModsList.Length);
                }

                InstalledMods = new List<InstalledMod>();
                CategoryNames = new List<string>();
                ModList = new List<ModListItem>();

                ModsListView.Visibility = Visibility.Hidden;

                if (App.CheckInstalledMods)
                {
                    MainWindow.Instance.MainText = $"{FindResource("Mods:CheckingInstalledMods")}...";
                    await Task.Run(async () => await CheckInstalledMods());
                    InstalledColumn.Width = double.NaN;
                    UninstallColumn.Width = 70;
                    DescriptionColumn.Width = 750;
                }
                else
                {
                    InstalledColumn.Width = 0;
                    UninstallColumn.Width = 0;
                    DescriptionColumn.Width = 800;
                }

                MainWindow.Instance.MainText = $"{FindResource("Mods:LoadingMods")}...";
                await Task.Run(async () => await PopulateModsList());

                ModsListView.ItemsSource = ModList;

                try
                {
                    var manualCategories = new string[] { "Core", "Leaderboards" };

                    ModList.Sort((a, b) =>
                    {
                        foreach (var category in manualCategories)
                        {
                            if (a.Category == category && b.Category == category) return 0;
                            if (a.Category == category) return -1;
                            if (b.Category == category) return 1;
                        }

                        var categoryCompare = a.Category.CompareTo(b.Category);
                        if (categoryCompare != 0) return categoryCompare;

                        var aRequired = !a.IsEnabled;
                        var bRequired = !b.IsEnabled;

                        //if (a.ModRequired && !b.ModRequired) return -1;
                        //if (b.ModRequired && !a.ModRequired) return 1;

                        return a.ModName.CompareTo(b.ModName);
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }

                view = (CollectionView)CollectionViewSource.GetDefaultView(ModsListView.ItemsSource);
                PropertyGroupDescription groupDescription = new PropertyGroupDescription("Category");
                view.GroupDescriptions.Add(groupDescription);

                this.DataContext = this;

                RefreshModsList();
                ModsListView.Visibility = ModList.Count == 0 ? Visibility.Hidden : Visibility.Visible;
                NoModsGrid.Visibility = ModList.Count == 0 ? Visibility.Visible : Visibility.Hidden;

                MainWindow.Instance.MainText = $"{FindResource("Mods:FinishedLoadingMods")}.";
                MainWindow.Instance.InstallButton.IsEnabled = ModList.Count != 0;
                MainWindow.Instance.GameVersionsBox.IsEnabled = true;
            }
            finally
            {
                _modsLoadSem.Release();
            }
        }

        public async Task CheckInstalledMods()
        {
            await GetAllMods();

            CheckInstallDir("BepInEx/installed.txt");
        }

        public async Task GetAllMods()
        {
            var resp = await HttpClient.GetAsync(Utils.Constants.LunarModsAPIUrl + "mod");
            var body = await resp.Content.ReadAsStringAsync();

            try
            {
                AllModsList = JsonSerializer.Deserialize<Mod[]>(body);
            }
            catch (Exception e)
            {
                System.Windows.MessageBox.Show($"{FindResource("Mods:LoadFailed")}.\n\n" + e);
                AllModsList = new Mod[] { };
            }
        }

        private void CheckInstallDir(string directory)
        {
            string fileName = Path.Combine(App.BeatSaberInstallDirectory, directory);
            if (!File.Exists(fileName))
            {
                return;
            }

            using (FileStream fileStream = File.OpenRead(fileName))
            using (StreamReader streamReader = new StreamReader(fileStream)) {
                string line;
                while ((line = streamReader.ReadLine()) != null)
                {
                    int split = line.IndexOf('/');
                    if (split == -1)
                    {
                        continue;
                    }

                    Mod mod = AllModsList.FirstOrDefault(n => n.name == line.Substring(0, split));
                    string version = line.Substring(split + 1);
                    if (mod != null)
                    {
                        AddDetectedMod(mod, version);
                    }
                }
            }
        }

        private void AddDetectedMod(Mod mod, string version)
        {
            if (InstalledMods.All(n => n.Mod != mod))
            {
                Mod.FileVersion fileVersion = mod.versions.FirstOrDefault(n => n.version == version);
                if (fileVersion == null)
                {
                    return;
                }
                InstalledMods.Add(new InstalledMod
                {
                    Mod = mod,
                    Version = fileVersion
                });
                if (App.SelectInstalledMods && !DefaultMods.Contains(mod.name))
                {
                    DefaultMods.Add(mod.name);
                }
            }
        }

        public async Task PopulateModsList()
        {
            try
            {
                var resp = await HttpClient.GetAsync(Utils.Constants.LunarModsAPIUrl + Utils.Constants.LunarModsModsOptions + "&gameVersion=" + MainWindow.GameVersion);
                var body = await resp.Content.ReadAsStringAsync();
                ModsList = JsonSerializer.Deserialize<Mod[]>(body);
            }
            catch (Exception e)
            {
                System.Windows.MessageBox.Show($"{FindResource("Mods:LoadFailed")}.\n\n" + e);
                return;
            }

            foreach (Mod mod in ModsList)
            {
                //bool preSelected = mod.required;
                bool preSelected = false;
                if (DefaultMods.Contains(mod.name) || (App.SaveModSelection && App.SavedMods.Contains(mod.name)))
                {
                    preSelected = true;
                    if (!App.SavedMods.Contains(mod.name))
                    {
                        App.SavedMods.Add(mod.name);
                    }
                }

                RegisterDependencies(mod);

                ModListItem ListItem = new ModListItem()
                {
                    IsSelected = preSelected,
                    IsEnabled = true,
                    ModName = mod.name,
                    ModVersion = mod.latestVersion,
                    ModDescription = mod.overview.Replace("\r\n", " ").Replace("\n", " "),
                    ModInfo = mod,
                    Category = mod.category
                };

                foreach (Promotion promo in Promotions.List)
                {
                    if (promo.Active && mod.name == promo.ModName)
                    {
                        ListItem.PromotionTexts = new string[promo.Links.Count];
                        ListItem.PromotionLinks = new string[promo.Links.Count];
                        ListItem.PromotionTextAfterLinks = new string[promo.Links.Count];

                        for (int i = 0; i < promo.Links.Count; ++i)
                        {
                            PromotionLink link = promo.Links[i];
                            ListItem.PromotionTexts[i] = link.Text;
                            ListItem.PromotionLinks[i] = link.Link;
                            ListItem.PromotionTextAfterLinks[i] = link.TextAfterLink;
                        }
                    }
                }

                foreach (InstalledMod installedMod in InstalledMods)
                {
                    if (mod.name == installedMod.Mod.name)
                    {
                        ListItem.InstalledModInfo = installedMod;
                        ListItem.IsInstalled = true;
                        ListItem.InstalledVersion = installedMod.Version.version;
                        break;
                    }
                }

                mod.ListItem = ListItem;

                ModList.Add(ListItem);
            }

            foreach (Mod mod in ModsList)
            {
                ResolveDependencies(mod);
            }
        }

        public async void InstallMods()
        {
            MainWindow.Instance.InstallButton.IsEnabled = false;
            string installDirectory = App.BeatSaberInstallDirectory;

            if (!File.Exists(installDirectory + "winhttp.dll"))
            {
                await InstallBepInEx(installDirectory);
            }

            foreach (Mod mod in ModsList)
            {
                // Ignore mods that are newer than installed version
                if (mod.ListItem.GetVersionComparison > 0) continue;

                // Ignore mods that are on current version if we aren't reinstalling mods
                if (mod.ListItem.GetVersionComparison == 0 && !App.ReinstallInstalledMods) continue;

                if (mod.ListItem.IsSelected)
                {
                    MainWindow.Instance.MainText = $"{string.Format((string)FindResource("Mods:InstallingMod"), mod.name)}...";
                    await Task.Run(async () => await InstallMod(mod, installDirectory));
                    MainWindow.Instance.MainText = $"{string.Format((string)FindResource("Mods:InstalledMod"), mod.name)}.";
                }
            }

            MainWindow.Instance.MainText = $"{FindResource("Mods:FinishedInstallingMods")}.";
            MainWindow.Instance.InstallButton.IsEnabled = true;
            RefreshModsList();
        }

        private async Task InstallBepInEx(string directory)
        {
            var resp = await HttpClient.GetAsync("https://api.github.com/repos/BepInEx/BepInEx/releases/latest");
            var body = await resp.Content.ReadAsStringAsync();
            var root = JsonSerializer.Deserialize<GithubRelease>(body);
            string downloadLink = root.assets.First(n => n.name.StartsWith("BepInEx_x64")).browser_download_url;

            using (Stream stream = await DownloadMod(downloadLink))
            {
                using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    foreach (ZipArchiveEntry file in archive.Entries)
                    {
                        string fileDirectory = Path.GetDirectoryName(Path.Combine(directory, file.FullName));
                        if (!Directory.Exists(fileDirectory))
                        {
                            Directory.CreateDirectory(fileDirectory);
                        }

                        if (!string.IsNullOrEmpty(file.Name))
                        {
                            await ExtractFile(file, Path.Combine(directory, file.FullName), 3.0, "BepInEx", 10);
                        }
                    }
                }
            }

            string fileName = Path.Combine(App.BeatSaberInstallDirectory, "BepInEx/config/BepInEx.cfg");
            string text = File.Exists(fileName) ? File.ReadAllText(fileName) : string.Empty;
            string[] lines = text.Split('\n');
            bool found = false;
            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].StartsWith("HideManagerGameObject"))
                {
                    continue;
                }

                lines[i] = "HideManagerGameObject = true";
                found = true;
                break;
            }

            string final = string.Join("\n", lines);
            if (!found)
            {
                final += "[Chainloader]\nHideManagerGameObject = true";
            }

            File.WriteAllText(fileName, final);
        }

        private async Task InstallMod(Mod mod, string directory)
        {
            string downloadLink = null;

            Mod.FileVersion version = mod.versions.FirstOrDefault(n => n.version == mod.latestVersion);
            downloadLink = version?.url;

            if (string.IsNullOrEmpty(downloadLink))
            {
                System.Windows.MessageBox.Show(string.Format((string)FindResource("Mods:ModDownloadLinkMissing"), mod.name));
                return;
            }

            using (Stream stream = await DownloadMod(Utils.Constants.LunarModsURL + downloadLink))
            using (Stream zipStream = new MemoryStream())
            {
                await stream.CopyToAsync(zipStream);
                zipStream.Position = 0;
                if (version.md5 != Utils.CalculateMD5FromStream(zipStream))
                {
                    // Checksum Mismatch
                    return;
                }

                zipStream.Position = 0;
                using (ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
                {
                    foreach (ZipArchiveEntry file in archive.Entries)
                    {
                        string fileDirectory = Path.GetDirectoryName(Path.Combine(directory, file.FullName));
                        if (!Directory.Exists(fileDirectory))
                        {
                            Directory.CreateDirectory(fileDirectory);
                        }

                        if (!string.IsNullOrEmpty(file.Name))
                        {
                            await ExtractFile(file, Path.Combine(directory, file.FullName), 3.0, mod.name, 10);
                        }
                    }
                }
            }

            string fileName = Path.Combine(App.BeatSaberInstallDirectory, "BepInEx/installed.txt");
            string text = File.Exists(fileName) ? File.ReadAllText(fileName) : string.Empty;
            string[] lines = text.Split('\n');
            File.WriteAllText(fileName, string.Join("\n", lines.Where(n => !n.StartsWith(mod.name))) + $"\n{mod.name}/{version.version}");

            if (App.CheckInstalledMods)
            {
                mod.ListItem.IsInstalled = true;
                mod.ListItem.InstalledVersion = version.version;
                mod.ListItem.InstalledModInfo = new InstalledMod
                {
                    Mod = mod,
                    Version = version
                };
            }
        }

        private async Task ExtractFile(ZipArchiveEntry file, string path, double seconds, string name, int maxTries, int tryNumber = 0)
        {
            if (tryNumber < maxTries)
            {
                try
                {
                    file.ExtractToFile(path, true);
                }
                catch
                {
                    MainWindow.Instance.MainText = $"{string.Format((string)FindResource("Mods:FailedExtract"), name, seconds, tryNumber + 1, maxTries)}";
                    await Task.Delay((int)(seconds * 1000));
                    await ExtractFile(file, path, seconds, name, maxTries, tryNumber + 1);
                }
            }
            else
            {
                System.Windows.MessageBox.Show($"{string.Format((string)FindResource("Mods:FailedExtractMaxReached"), name, maxTries)}.", "Failed to install " + name);
            }
        }

        private async Task<Stream> DownloadMod(string link)
        {
            var resp = await HttpClient.GetAsync(link);
            return await resp.Content.ReadAsStreamAsync();
        }

        private void RegisterDependencies(Mod dependent)
        {
            Mod.FileVersion version = dependent.versions.FirstOrDefault(n => n.version == dependent.latestVersion);
            if (version.dependencies.Length == 0)
                return;

            foreach (Mod mod in ModsList)
            {
                foreach (string dep in version.dependencies)
                {
                    if (dep == mod.name)
                    {
                        mod.Dependents.Add(dependent);
                        dependent.Dependencies.Add(mod);
                    }
                }
            }
        }

        private void ResolveDependencies(Mod dependent)
        {
            if (dependent.ListItem.IsSelected && dependent.Dependencies.Count > 0)
            {
                foreach (Mod dependency in dependent.Dependencies)
                {
                    if (dependency.ListItem.IsEnabled)
                    {
                        dependency.ListItem.PreviousState = dependency.ListItem.IsSelected;
                        dependency.ListItem.IsSelected = true;
                        dependency.ListItem.IsEnabled = false;
                        ResolveDependencies(dependency);
                    }
                }
            }
        }

        private void UnresolveDependencies(Mod dependent)
        {
            if (!dependent.ListItem.IsSelected && dependent.Dependencies.Count > 0)
            {
                foreach (Mod dependency in dependent.Dependencies)
                {
                    if (!dependency.ListItem.IsEnabled)
                    {
                        bool needed = false;
                        foreach (Mod dep in dependency.Dependents)
                        {
                            if (dep.ListItem.IsSelected)
                            {
                                needed = true;
                                break;
                            }
                        }
                        if (!needed)
                        {
                            dependency.ListItem.IsSelected = dependency.ListItem.PreviousState;
                            dependency.ListItem.IsEnabled = true;
                            UnresolveDependencies(dependency);
                        }
                    }
                }
            }
        }

        private void ModCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            Mod mod = (sender as System.Windows.Controls.CheckBox).Tag as Mod;
            mod.ListItem.IsSelected = true;
            ResolveDependencies(mod);
            App.SavedMods.Add(mod.name);
            Properties.Settings.Default.SavedMods = string.Join(",", App.SavedMods.ToArray());
            Properties.Settings.Default.Save();

            RefreshModsList();
        }

        private void ModCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            Mod mod = (sender as System.Windows.Controls.CheckBox).Tag as Mod;
            mod.ListItem.IsSelected = false;
            UnresolveDependencies(mod);
            App.SavedMods.Remove(mod.name);
            Properties.Settings.Default.SavedMods = string.Join(",", App.SavedMods.ToArray());
            Properties.Settings.Default.Save();

            RefreshModsList();
        }

        public class Category
        {
            public string CategoryName { get; set; }
            public List<ModListItem> Mods = new List<ModListItem>();
        }

        public class ModListItem
        {
            public string ModName { get; set; }
            public string ModVersion { get; set; }
            public string ModDescription { get; set; }
            public bool PreviousState { get; set; }

            public bool IsEnabled { get; set; }
            public bool IsSelected { get; set; }
            public Mod ModInfo { get; set; }
            public string Category { get; set; }

            public InstalledMod InstalledModInfo { get; set; }
            public bool IsInstalled { get; set; }
            private SemVersion _installedVersion { get; set; }
            public string InstalledVersion
            {
                get
                {
                    if (!IsInstalled || _installedVersion == null) return "-";
                    return _installedVersion.ToString();
                }
                set
                {
                    if (SemVersion.TryParse(value, out SemVersion tempInstalledVersion))
                    {
                        _installedVersion = tempInstalledVersion;
                    }
                    else
                    {
                        _installedVersion = null;
                    }
                }
            }

            public string GetVersionColor
            {
                get
                {
                    if (!IsInstalled) return "Black";
                    return _installedVersion >= ModVersion ? "Green" : "Red";
                }
            }

            public string GetVersionDecoration
            {
                get
                {
                    if (!IsInstalled) return "None";
                    return _installedVersion >= ModVersion ? "None" : "Strikethrough";
                }
            }

            public int GetVersionComparison
            {
                get
                {
                    if (!IsInstalled || _installedVersion < ModVersion) return -1;
                    if (_installedVersion > ModVersion) return 1;
                    return 0;
                }
            }

            public bool CanDelete
            {
                get
                {
                    return (IsInstalled);
                }
            }

            public string CanSeeDelete
            {
                get
                {
                    if (IsInstalled)
                        return "Visible";
                    else
                        return "Hidden";
                }
            }

            public string[] PromotionTexts { get; set; }
            public string[] PromotionLinks { get; set; }
            public string[] PromotionTextAfterLinks { get; set; }
            public string PromotionMargin
            {
                get
                {
                    if (PromotionTexts == null || string.IsNullOrEmpty(PromotionTexts[0])) return "-15,0,0,0";
                    return "0,0,5,0";
                }
            }
        }

        private void ModsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if ((Mods.ModListItem)Instance.ModsListView.SelectedItem == null)
            {
                MainWindow.Instance.InfoButton.IsEnabled = false;
            }
            else
            {
                MainWindow.Instance.InfoButton.IsEnabled = true;
            }
        }

        private readonly string[] _bepInExFiles =
        {
            "BepInEx/core/MonoMod.Utils.xml",
            "BepInEx/core/MonoMod.Utils.dll",
            "BepInEx/core/MonoMod.RuntimeDetour.xml",
            "BepInEx/core/MonoMod.RuntimeDetour.dll",
            "BepInEx/core/Mono.Cecil.Rocks.dll",
            "BepInEx/core/Mono.Cecil.Pdb.dll",
            "BepInEx/core/Mono.Cecil.Mdb.dll",
            "BepInEx/core/Mono.Cecil.dll",
            "BepInEx/core/HarmonyXInterop.dll",
            "BepInEx/core/BepInEx.xml",
            "BepInEx/core/BepInEx.Preloader.xml",
            "BepInEx/core/BepInEx.Preloader.dll",
            "BepInEx/core/BepInEx.Harmony.xml",
            "BepInEx/core/BepInEx.Harmony.dll",
            "BepInEx/core/BepInEx.dll",
            "BepInEx/core/0Harmony20.dll",
            "BepInEx/core/0Harmony.xml",
            "BepInEx/core/0Harmony.dll",
            "winhttp.dll",
            "doorstop_config.ini",
            "changelog.txt"
        };

        public void UninstallBepInEx()
        {
            var hasBepExe = File.Exists(Path.Combine(App.BeatSaberInstallDirectory, "winhttp.dll"));
            var hasBepDir = Directory.Exists(Path.Combine(App.BeatSaberInstallDirectory, "BepInEx"));

            if (hasBepDir && hasBepExe)
            {
                foreach (string file in _bepInExFiles)
                {
                    if (File.Exists(Path.Combine(App.BeatSaberInstallDirectory, file)))
                        File.Delete(Path.Combine(App.BeatSaberInstallDirectory, file));
                }
                Options.Instance.YeetBepInEx.IsEnabled = false;
            }
            else
            {
                var title = (string)FindResource("Mods:UninstallBepInExNotFound:Title");
                var body = (string)FindResource("Mods:UninstallBepInExNotFound:Body");

                System.Windows.Forms.MessageBox.Show(body, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            Mod mod = ((sender as System.Windows.Controls.Button).Tag as Mod);

            string title = string.Format((string)FindResource("Mods:UninstallBox:Title"), mod.name);
            string body1 = string.Format((string)FindResource("Mods:UninstallBox:Body1"), mod.name);
            string body2 = string.Format((string)FindResource("Mods:UninstallBox:Body2"), mod.name);
            var result = System.Windows.Forms.MessageBox.Show($"{body1}\n{body2}", title, MessageBoxButtons.YesNo);

            if (result == DialogResult.Yes)
            {
                UninstallModFromList(mod);
            }
        }

        private void UninstallModFromList(Mod mod)
        {
            UninstallMod(mod.ListItem.InstalledModInfo.Mod, mod.ListItem.InstalledModInfo.Version);
            mod.ListItem.IsInstalled = false;
            mod.ListItem.InstalledVersion = null;
            if (App.SelectInstalledMods)
            {
                mod.ListItem.IsSelected = false;
                UnresolveDependencies(mod);
                App.SavedMods.Remove(mod.name);
                Properties.Settings.Default.SavedMods = string.Join(",", App.SavedMods.ToArray());
                Properties.Settings.Default.Save();
                RefreshModsList();
            }
            view.Refresh();
        }

        public void UninstallMod(Mod mod, Mod.FileVersion version)
        {
            string fileName = Path.Combine(App.BeatSaberInstallDirectory, "BepInEx/installed.txt");
            if (File.Exists(fileName))
            {
                string[] lines = File.ReadAllText(fileName).Split('\n');
                File.WriteAllText(fileName, string.Join("\n", lines.Where(n => !(n.StartsWith(mod.name) && n.EndsWith(version.version)))));
            }
            foreach (string file in version.files)
            {
                if (File.Exists(Path.Combine(App.BeatSaberInstallDirectory, file)))
                    File.Delete(Path.Combine(App.BeatSaberInstallDirectory, file));
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
            e.Handled = true;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshColumns();
        }

        private void CopyText(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!(sender is TextBlock textBlock)) return;
            var text = textBlock.Text;

            // Ensure there's text to be copied
            if (string.IsNullOrWhiteSpace(text)) return;

            Utils.SetClipboard(text);
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (SearchBar.Height == 0)
            {
                SearchBar.Focus();
                Animate(SearchBar, 0, 16, new TimeSpan(0, 0, 0, 0, 300));
                Animate(SearchText, 0, 16, new TimeSpan(0, 0, 0, 0, 300));
                ModsListView.Items.Filter = new Predicate<object>(SearchFilter);
            }
            else
            {
                Animate(SearchBar, 16, 0, new TimeSpan(0, 0, 0, 0, 300));
                Animate(SearchText, 16, 0, new TimeSpan(0, 0, 0, 0, 300));
                ModsListView.Items.Filter = null;
            }
        }

        private void SearchBar_TextChanged(object sender, TextChangedEventArgs e)
        {
            ModsListView.Items.Filter = new Predicate<object>(SearchFilter);
            if (SearchBar.Text.Length > 0)
            {
                SearchText.Text = null;
            }
            else
            {
                SearchText.Text = (string)FindResource("Mods:SearchLabel");
            }
        }

        private bool SearchFilter(object mod)
        {
            ModListItem item = mod as ModListItem;
            if (item.ModName.ToLowerInvariant().Contains(SearchBar.Text.ToLowerInvariant())) return true;
            if (item.ModDescription.ToLowerInvariant().Contains(SearchBar.Text.ToLowerInvariant())) return true;
            if (item.ModName.ToLowerInvariant().Replace(" ", string.Empty).Contains(SearchBar.Text.ToLowerInvariant().Replace(" ", string.Empty))) return true;
            if (item.ModDescription.ToLowerInvariant().Replace(" ", string.Empty).Contains(SearchBar.Text.ToLowerInvariant().Replace(" ", string.Empty))) return true;
            return false;
        }

        private void Animate(TextBlock target, double oldHeight, double newHeight, TimeSpan duration)
        {
            target.Height = oldHeight;
            DoubleAnimation animation = new DoubleAnimation(newHeight, duration);
            target.BeginAnimation(HeightProperty, animation);
        }

        private void Animate(TextBox target, double oldHeight, double newHeight, TimeSpan duration)
        {
            target.Height = oldHeight;
            DoubleAnimation animation = new DoubleAnimation(newHeight, duration);
            target.BeginAnimation(HeightProperty, animation);
        }
    }
}
