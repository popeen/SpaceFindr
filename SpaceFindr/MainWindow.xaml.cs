using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Win32;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Navigation;
using System.Text.Json;
using System.Net.Http;
// Only use the specific type from System.Windows.Forms when needed

namespace SpaceFindr
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private StorageItem _treeRoot; // The full tree
        private StorageItem _currentViewRoot; // The currently viewed folder
        private Stopwatch _progressStopwatch = new Stopwatch();
        private CancellationTokenSource _loadingCts;
        private Stack<StorageItem> _backStack = new Stack<StorageItem>();
        private Stack<StorageItem> _forwardStack = new Stack<StorageItem>();

        private DateTime _lastFooterUpdate = DateTime.MinValue;

        private bool _showFreeSpace = true;
        private bool _showUsedSpace = false;
        private bool _checkUpdatesOnStart = true;
        private bool _ignoreReparsePoints = true;
        private bool _showRemovableDrives = true;
        private bool _showNetworkDrives = true;
        private bool _showTips = true;
        private const string RegistryPath = @"Software\\Popeen\\SpaceFindr";
        private const string ShowFreeSpaceKey = "ShowFreeSpace";
        private const string ShowUsedSpaceKey = "ShowUsedSpace";
        private const string CheckUpdatesKey = "CheckUpdatesOnStart";
        private const string IgnoreReparsePointsKey = "IgnoreReparsePoints";
        private const string ShowRemovableDrivesKey = "ShowRemovableDrives";
        private const string ShowNetworkDrivesKey = "ShowNetworkDrives";
        private const string ShowTipsKey = "ShowTips";
        private bool _isInitializing = true;

        private bool _breadcrumbEditMode = false;

        private readonly string[] _tips = new[]
        {
            "TIP: You can use the select folder option to scan drives on other computers in the network if they are shared with you",
            "TIP: You can right-click files and folders for more options.",
            "TIP: Press Ctrl+Alt+drive letter to quickly scan a drive (e.g. Ctrl+Alt+C for C:)."
        };

        public MainWindow()
        {
            InitializeComponent();
            LoadSettingsFromRegistry();
            ShowFreeSpaceCheckBox.IsChecked = _showFreeSpace;
            ShowUsedSpaceCheckBox.IsChecked = _showUsedSpace;
            CheckUpdatesOnStartCheckBox.IsChecked = _checkUpdatesOnStart;
            IgnoreReparsePointsCheckBox.IsChecked = _ignoreReparsePoints;
            _isInitializing = false;
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
            this.Title = $"SpaceFindr BETA v.{version}";
            this.PreviewMouseDown += MainWindow_PreviewMouseDown;
            this.Deactivated += MainWindow_Deactivated;
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;
            // Set a random tip on startup
            var rand = new Random();
            var tipText = _tips[rand.Next(_tips.Length)];
            var tipTextBlock = TipBar.Child as DockPanel;
            if (tipTextBlock != null && tipTextBlock.Children.Count > 0 && tipTextBlock.Children[0] is TextBlock tb)
                tb.Text = tipText;
            UpdateDriveUsage(null); // Load all drives on startup
            if (_checkUpdatesOnStart)
            {
                _ = CheckForUpdatesAsync();
            }
        }
        private void ShowUsedSpaceCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _showUsedSpace = true;
            if (!_isInitializing) SaveSettingsToRegistry();
            UpdateDriveUsage(null);
        }

        private void ShowUsedSpaceCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            _showUsedSpace = false;
            if (!_isInitializing) SaveSettingsToRegistry();
            UpdateDriveUsage(null);
        }

        private void LoadSettingsFromRegistry()
        {
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryPath))
            {
                if (key != null)
                {
                    _showFreeSpace = Convert.ToBoolean(key.GetValue(ShowFreeSpaceKey, true));
                    _showUsedSpace = Convert.ToBoolean(key.GetValue(ShowUsedSpaceKey, false));
                    _checkUpdatesOnStart = Convert.ToBoolean(key.GetValue(CheckUpdatesKey, true));
                    _ignoreReparsePoints = Convert.ToBoolean(key.GetValue(IgnoreReparsePointsKey, true));
                    _showRemovableDrives = Convert.ToBoolean(key.GetValue(ShowRemovableDrivesKey, true));
                    _showNetworkDrives = Convert.ToBoolean(key.GetValue(ShowNetworkDrivesKey, true));
                    _showTips = Convert.ToBoolean(key.GetValue(ShowTipsKey, true));
                }
            }
        }

        private void SaveSettingsToRegistry()
        {
            using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RegistryPath))
            {
                key.SetValue(ShowFreeSpaceKey, _showFreeSpace);
                key.SetValue(ShowUsedSpaceKey, _showUsedSpace);
                key.SetValue(CheckUpdatesKey, _checkUpdatesOnStart);
                key.SetValue(IgnoreReparsePointsKey, _ignoreReparsePoints);
                key.SetValue(ShowRemovableDrivesKey, _showRemovableDrives);
                key.SetValue(ShowNetworkDrivesKey, _showNetworkDrives);
                key.SetValue(ShowTipsKey, _showTips);
            }
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string url = "https://api.github.com/repos/popeen/SpaceFindr/releases";
                    client.DefaultRequestHeaders.Add("User-Agent", "spacefindr-updater");
                    HttpResponseMessage response = await client.GetAsync(url);
                    response.EnsureSuccessStatusCode();
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    using var doc = System.Text.Json.JsonDocument.Parse(jsonResponse);
                    var root = doc.RootElement;
                    if (root.GetArrayLength() > 0)
                    {
                        var latest = root[0];
                        var latestTag = latest.GetProperty("tag_name").GetString();
                        var releaseUrl = latest.GetProperty("html_url").GetString();
                        Version latestVersion = null;
                        Version currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                        if (!Version.TryParse(latestTag.TrimStart('v', 'V'), out latestVersion))
                            latestVersion = new Version(latestTag.TrimStart('v', 'V'));
                        if (latestVersion > currentVersion)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                var dlg = new UpdateDialog(latestTag, releaseUrl) { Owner = this };
                                dlg.ShowDialog();
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Optionally log or ignore update errors
            }
        }

        private void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.XButton1)
            {
                NavigateBack();
                e.Handled = true;
            }
            else if (e.ChangedButton == MouseButton.XButton2)
            {
                NavigateForward();
                e.Handled = true;
            }
            if (_breadcrumbEditMode && BreadcrumbPathBox.Visibility == Visibility.Visible)
            {
                var mousePos = e.GetPosition(BreadcrumbPathBox);
                var rect = new Rect(0, 0, BreadcrumbPathBox.ActualWidth, BreadcrumbPathBox.ActualHeight);
                if (!rect.Contains(mousePos))
                {
                    ExitBreadcrumbEditMode(false);
                }
            }
        }

        private void MainWindow_Deactivated(object sender, EventArgs e)
        {
            if (_breadcrumbEditMode)
            {
                ExitBreadcrumbEditMode(false);
            }
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.O)
            {
                e.Handled = true;
                BrowseButton_Click(BrowseButton, new RoutedEventArgs());
                return;
            }
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
                (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt &&
                e.Key >= Key.A && e.Key <= Key.Z)
            {
                char driveLetter = (char)('A' + (e.Key - Key.A));
                string driveRoot = driveLetter + ":\\";
                var drive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && string.Equals(d.Name, driveRoot, StringComparison.OrdinalIgnoreCase));
                if (drive != null)
                {
                    e.Handled = true;
                    ScanningSpinner.Visibility = Visibility.Visible;
                    TreemapCanvas.Children.Clear();
                    _treeRoot = new StorageItem { Name = drive.Name, FullPath = drive.Name, IsFolder = true };
                    _currentViewRoot = _treeRoot;
                    _progressStopwatch.Restart();
                    var progress = new Progress<StorageItem>(item =>
                    {
                        var now = DateTime.Now;
                        if ((now - _lastFooterUpdate).TotalMilliseconds > 500)
                        {
                            _lastFooterUpdate = now;
                            Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                DrawTreemap(_currentViewRoot);
                                UpdateFooter(item);
                            });
                        }
                    });
                    Task.Run(async () =>
                    {
                        var rootItem = await Task.Run(() => StorageItem.BuildFromDirectory(drive.Name, progress, _treeRoot, null, _ignoreReparsePoints));
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            ScanningSpinner.Visibility = Visibility.Collapsed;
                            _treeRoot = rootItem;
                            _currentViewRoot = rootItem;
                            UpdateBreadcrumbBar(rootItem);
                            DrawTreemap(_currentViewRoot);
                            UpdateFooter(rootItem);
                        });
                    });
                }
            }
        }

        private void NavigateBack()
        {
            if (_currentViewRoot?.Parent != null)
            {
                _forwardStack.Push(_currentViewRoot);
                _backStack.Push(_currentViewRoot);
                _currentViewRoot = _currentViewRoot.Parent;
                UpdateBreadcrumbBar(_currentViewRoot);
                DrawTreemap(_currentViewRoot);
                UpdateFooter(_currentViewRoot);
            }
        }

        private void NavigateForward()
        {
            if (_forwardStack.Count > 0)
            {
                var next = _forwardStack.Pop();
                if (next != null)
                {
                    _backStack.Push(_currentViewRoot);
                    _currentViewRoot = next;
                    UpdateBreadcrumbBar(_currentViewRoot);
                    DrawTreemap(_currentViewRoot);
                    UpdateFooter(_currentViewRoot);
                }
            }
        }

        private void UpdateFooter(StorageItem folder)
        {
            if (folder == null)
            {
                FolderFileCountText.Text = "";
                return;
            }
            if (ScanningSpinner.Visibility == Visibility.Visible)
            {
                FolderFileCountText.Text = $"Scanning: {folder.FullPath}";
            }
            else
            {
                var children = folder.Children ?? (_currentViewRoot?.Children);
                if (children == null)
                {
                    FolderFileCountText.Text = "Items: 0 folders, 0 files";
                    return;
                }
                int folderCount = children.Count(x => x != null && x.IsFolder);
                int fileCount = children.Count(x => x != null && !x.IsFolder);
                FolderFileCountText.Text = $"Items: {folderCount} folders, {fileCount} files";
            }
        }

        private void UpdateBreadcrumbBar(StorageItem current)
        {
            BreadcrumbBarBorder.Visibility = _currentViewRoot == null ? Visibility.Collapsed : Visibility.Visible;
            BreadcrumbPanel.Children.Clear();
            if (current == null) return;
            var path = current.FullPath;
            var pathParts = path.TrimEnd(System.IO.Path.DirectorySeparatorChar).Split(System.IO.Path.DirectorySeparatorChar);
            string root = System.IO.Path.GetPathRoot(path);
            string cumulativePath = root;
            int partIndex = 0;
            for (int i = 0; i < pathParts.Length; i++)
            {
                string part = pathParts[i];
                if (string.IsNullOrEmpty(part)) continue;
                string display = (i == 0) ? root.TrimEnd(System.IO.Path.DirectorySeparatorChar) : part;
                if (i > 0) cumulativePath = System.IO.Path.Combine(cumulativePath, part);
                var btn = new Button
                {
                    Content = display,
                    Margin = new Thickness(0, 0, 2, 0),
                    Padding = new Thickness(6, 0, 6, 0),
                    Tag = cumulativePath,
                    Style = (Style)FindResource("BreadcrumbButtonStyle"),
                    FontWeight = (i == pathParts.Length - 1) ? FontWeights.Bold : FontWeights.Normal
                };
                btn.Click += async (s, e) =>
                {
                    string targetPath = (string)btn.Tag;
                    if (Directory.Exists(targetPath))
                    {
                        // Try to find the StorageItem for this path in the current tree
                        StorageItem targetItem = _treeRoot;
                        if (targetItem != null && !string.Equals(targetItem.FullPath, targetPath, StringComparison.OrdinalIgnoreCase))
                        {
                            var parts = targetPath.TrimEnd(System.IO.Path.DirectorySeparatorChar).Split(System.IO.Path.DirectorySeparatorChar);
                            var current = targetItem;
                            foreach (var part in parts.Skip(1))
                            {
                                if (current.Children == null) { current = null; break; }
                                current = current.Children.FirstOrDefault(x => x != null && string.Equals(x.Name, part, StringComparison.OrdinalIgnoreCase));
                                if (current == null) break;
                            }
                            targetItem = current;
                        }
                        if (targetItem != null && targetItem.Children != null && targetItem.Children.Count > 0)
                        {
                            _currentViewRoot = targetItem;
                            UpdateBreadcrumbBar(targetItem);
                            DrawTreemap(targetItem);
                            UpdateFooter(targetItem);
                        }
                        else
                        {
                            await NavigateToPathAsync(targetPath);
                        }
                    }
                };
                btn.PreviewKeyDown += BreadcrumbButton_PreviewKeyDown;
                btn.GotFocus += (s, e) => btn.BorderBrush = Brushes.DodgerBlue;
                btn.LostFocus += (s, e) => btn.ClearValue(Button.BorderBrushProperty);
                BreadcrumbPanel.Children.Add(btn);
                partIndex++;
                if (i < pathParts.Length - 1)
                {
                    BreadcrumbPanel.Children.Add(new TextBlock { Text = ">", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 2, 0), Foreground = Brushes.Gray });
                }
            }
        }

        private void BreadcrumbButton_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var btn = sender as Button;
            var panel = BreadcrumbPanel;
            int idx = panel.Children.IndexOf(btn);
            if (e.Key == Key.Right)
            {
                // Move to next breadcrumb (skip separators)
                for (int i = idx + 1; i < panel.Children.Count; i++)
                {
                    if (panel.Children[i] is Button nextBtn)
                    {
                        nextBtn.Focus();
                        e.Handled = true;
                        break;
                    }
                }
            }
            else if (e.Key == Key.Left)
            {
                // Move to previous breadcrumb (skip separators)
                for (int i = idx - 1; i >= 0; i--)
                {
                    if (panel.Children[i] is Button prevBtn)
                    {
                        prevBtn.Focus();
                        e.Handled = true;
                        break;
                    }
                }
            }
            else if (e.Key == Key.Enter)
            {
                btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                e.Handled = true;
            }
        }

        private async void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            _loadingCts?.Cancel();
            _loadingCts = new CancellationTokenSource();
            try
            {
                var dialog = new System.Windows.Forms.FolderBrowserDialog();
                dialog.Description = "Select a folder to scan";
                dialog.ShowNewFolderButton = false;
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    string selectedPath = dialog.SelectedPath;
                    TreemapCanvas.Children.Clear();
                    _treeRoot = new StorageItem { Name = selectedPath, FullPath = selectedPath, IsFolder = true };
                    _currentViewRoot = _treeRoot;
                    _progressStopwatch.Restart();

                    var progress = new Progress<StorageItem>(item =>
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (_progressStopwatch.ElapsedMilliseconds > 2000)
                            {
                                DrawTreemap(_currentViewRoot);
                                UpdateFilesListView(); // Throttle file list updates to match treemap
                                _progressStopwatch.Restart();
                            }
                        });
                    });

                    // Run scan on background thread
                    var root = await Task.Run(() => StorageItem.BuildFromDirectory(selectedPath, progress, _treeRoot, null, _ignoreReparsePoints), _loadingCts.Token);
                    // Final UI update after scan
                    ScanningSpinner.Visibility = Visibility.Collapsed;
                    _treeRoot = root;
                    _currentViewRoot = root;
                    UpdateBreadcrumbBar(root);
                    DrawTreemap(root);
                    UpdateFooter(root);
                }
            }
            catch (Exception ex)
            {
                System.Windows.Clipboard.SetText(ex.ToString());
                System.Windows.MessageBox.Show(ex.ToString() + "\n\n(The error has been copied to your clipboard)", "Error");
            }
        }

        private void ShowFreeSpaceCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _showFreeSpace = true;
            if (!_isInitializing) SaveSettingsToRegistry();
            if (_currentViewRoot != null)
                DrawTreemap(_currentViewRoot);
        }

        private void ShowFreeSpaceCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            _showFreeSpace = false;
            if (!_isInitializing) SaveSettingsToRegistry();
            if (_currentViewRoot != null)
                DrawTreemap(_currentViewRoot);
        }

        private void CheckUpdatesOnStartCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _checkUpdatesOnStart = true;
            if (!_isInitializing) SaveSettingsToRegistry();
        }

        private void CheckUpdatesOnStartCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            _checkUpdatesOnStart = false;
            if (!_isInitializing) SaveSettingsToRegistry();
        }

        private void IgnoreReparsePointsCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _ignoreReparsePoints = true;
            if (!_isInitializing) SaveSettingsToRegistry();
        }

        private void IgnoreReparsePointsCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            _ignoreReparsePoints = false;
            if (!_isInitializing) SaveSettingsToRegistry();
        }

        private void ShowRemovableDrivesCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _showRemovableDrives = true;
            if (!_isInitializing) SaveSettingsToRegistry();
            UpdateDriveUsage(null);
        }
        private void ShowRemovableDrivesCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            _showRemovableDrives = false;
            if (!_isInitializing) SaveSettingsToRegistry();
            UpdateDriveUsage(null);
        }
        private void ShowNetworkDrivesCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _showNetworkDrives = true;
            if (!_isInitializing) SaveSettingsToRegistry();
            UpdateDriveUsage(null);
        }
        private void ShowNetworkDrivesCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            _showNetworkDrives = false;
            if (!_isInitializing) SaveSettingsToRegistry();
            UpdateDriveUsage(null);
        }
        private void ShowTipsCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _showTips = true;
            if (!_isInitializing) SaveSettingsToRegistry();
            TipBar.Visibility = Visibility.Visible;
        }
        private void ShowTipsCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            _showTips = false;
            if (!_isInitializing) SaveSettingsToRegistry();
            TipBar.Visibility = Visibility.Collapsed;
        }
        private void TipBarCloseButton_Click(object sender, RoutedEventArgs e)
        {
            TipBar.Visibility = Visibility.Collapsed;
        }

        private void DrawTreemap(StorageItem root)
        {
            TreemapCanvas.Children.Clear();
            if (root == null || root.Children == null || root.Children.Count == 0) return;

            string driveRoot = System.IO.Path.GetPathRoot(root.FullPath);
            var children = root.Children.ToList();
            bool isDriveRoot = string.Equals(root.FullPath, driveRoot, StringComparison.OrdinalIgnoreCase);
            if (isDriveRoot && _showFreeSpace)
            {
                try
                {
                    var drive = new DriveInfo(driveRoot);
                    if (drive.IsReady)
                    {
                        var freeSpaceItem = new StorageItem
                        {
                            Name = "Free space",
                            FullPath = driveRoot,
                            IsFolder = false,
                            Size = drive.AvailableFreeSpace
                        };
                        children.Add(freeSpaceItem);
                    }
                }
                catch { }
            }

            TreemapCanvas.UpdateLayout(); // Ensure ActualWidth/Height are up to date
            double width = TreemapCanvas.ActualWidth;
            double height = TreemapCanvas.ActualHeight;
            if (width == 0 || height == 0)
            {
                width = TreemapCanvas.Width = 900;
                height = TreemapCanvas.Height = 450;
            }

            UpdateBreadcrumbBar(root);
            UpdateFooter(root);

            var rects = TreemapHelper.Squarify(children, 0, 0, width, height);
            foreach (var treemapRect in rects)
            {
                var child = treemapRect.Item;
                var r = treemapRect.Rect;
                Brush fillBrush;
                bool isFreeSpaceRect = isDriveRoot && child.Name == "Free space" && string.Equals(child.FullPath, driveRoot, StringComparison.OrdinalIgnoreCase);
                if (isFreeSpaceRect)
                    fillBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCC"));
                else if (child.IsFolder)
                    fillBrush = Brushes.LightGreen;
                else if (child.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    fillBrush = Brushes.SteelBlue;
                else
                    fillBrush = Brushes.Yellow;
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = r.Width,
                    Height = r.Height,
                    Fill = fillBrush,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1,
                    ToolTip = isFreeSpaceRect ? $"Free space: {FormatSize(child.Size)}" : $"{child.Name}\n{FormatSize(child.Size)}"
                };
                // Only attach context menu and zoom for real files/folders
                if (!isFreeSpaceRect)
                {
                    // Context menu (same as before)
                    var contextMenu = new System.Windows.Controls.ContextMenu();
                    var askAiMenu = new MenuItem { Header = "Ask AI" };
                    askAiMenu.Click += (s, e) => AskAI(child);
                    if (child.IsFolder)
                    {
                        var deleteFolder = new MenuItem { Header = "Delete Folder" };
                        deleteFolder.Click += (s, e) => DeleteFolder(child);
                        var openInExplorer = new MenuItem { Header = "Open in File Explorer" };
                        openInExplorer.Click += (s, e) => OpenInExplorer(child.FullPath);
                        contextMenu.Items.Add(openInExplorer);
                        contextMenu.Items.Add(askAiMenu);
                        contextMenu.Items.Add(deleteFolder);
                    }
                    else
                    {
                        var deleteFile = new MenuItem { Header = "Delete File" };
                        deleteFile.Click += (s, e) => DeleteFile(child);
                        var openFolder = new MenuItem { Header = "Open in File Explorer" };
                        openFolder.Click += (s, e) => OpenInExplorer(System.IO.Path.GetDirectoryName(child.FullPath));
                        var openFile = new MenuItem { Header = "Open File" };
                        openFile.Click += (s, e) => OpenFile(child.FullPath);
                        contextMenu.Items.Add(openFile);
                        contextMenu.Items.Add(openFolder);
                        contextMenu.Items.Add(askAiMenu);
                        contextMenu.Items.Add(deleteFile);
                    }
                    rect.ContextMenu = contextMenu;
                    // Only attach zoom for folders
                    if (child.IsFolder)
                    {
                        rect.MouseLeftButtonUp += async (s, e) =>
                        {
                            _loadingCts?.Cancel();
                            _loadingCts = new CancellationTokenSource();
                            _progressStopwatch.Restart();
                            if (child.Children == null || child.Children.Count == 0)
                            {
                                var progress = new Progress<StorageItem>(item =>
                                {
                                    if (_progressStopwatch.ElapsedMilliseconds > 2000)
                                    {
                                        DrawTreemap(child);
                                        _progressStopwatch.Restart();
                                    }
                                });
                                await Task.Run(() => StorageItem.BuildFromDirectory(child.FullPath, progress, child, null, _ignoreReparsePoints), _loadingCts.Token);
                            }
                            ScanningSpinner.Visibility = Visibility.Collapsed;
                            _backStack.Push(_currentViewRoot);
                            _currentViewRoot = child;
                            _forwardStack.Clear();
                            UpdateBreadcrumbBar(child);
                            DrawTreemap(child);
                        };
                    }
                }
                TreemapCanvas.Children.Add(rect);
                Canvas.SetLeft(rect, r.X);
                Canvas.SetTop(rect, r.Y);

                double minLabelSize = 40;
                double medLabelSize = 80;
                string labelText = null;
                if (r.Width > medLabelSize && r.Height > minLabelSize)
                    labelText = isFreeSpaceRect ? $"Free space\n{FormatSize(child.Size)}" : $"{child.Name}\n{FormatSize(child.Size)}";
                else if (r.Width > minLabelSize && r.Height > minLabelSize)
                    labelText = isFreeSpaceRect ? "Free space" : child.Name;
                if (!string.IsNullOrEmpty(labelText))
                {
                    var label = new TextBlock
                    {
                        Text = labelText,
                        Foreground = Brushes.Black,
                        FontWeight = FontWeights.Bold,
                        FontSize = r.Width > medLabelSize ? 12 : 10,
                        TextAlignment = TextAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        IsHitTestVisible = false
                    };
                    label.Width = r.Width;
                    label.Height = r.Height;
                    TreemapCanvas.Children.Add(label);
                    Canvas.SetLeft(label, r.X);
                    Canvas.SetTop(label, r.Y + r.Height / 2 - (r.Width > medLabelSize ? 16 : 10));
                }
            }
        }

        private string FormatSize(long size)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = size;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private void CalculateFolderSizes(StorageItem folder)
        {
            if (!folder.IsFolder || folder.Children == null) return;
            long totalSize = 0;
            foreach (var child in folder.Children)
            {
                if (child.IsFolder)
                {
                    CalculateFolderSizes(child);
                }
                else
                {
                    try
                    {
                        var fileInfo = new FileInfo(child.FullPath);
                        var attrs = fileInfo.Attributes;
                        if ((_ignoreReparsePoints && (attrs & System.IO.FileAttributes.ReparsePoint) != 0) ||
                            (attrs & System.IO.FileAttributes.Offline) != 0 ||
                            (attrs & System.IO.FileAttributes.Temporary) != 0)
                            continue;
                        string ext = fileInfo.Extension.ToLowerInvariant();
                        if (ext == ".nextcloud" || ext == ".cloud" || ext == ".cloudf")
                            continue;
                        if (fileInfo.Length == 0)
                            continue;
                    }
                    catch { continue; }
                }
                totalSize += child.Size;
            }
            folder.Size = totalSize;
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_currentViewRoot != null)
            {
                DrawTreemap(_currentViewRoot);
            }
        }

        private void DeleteFolder(StorageItem folder)
        {
            if (System.Windows.MessageBox.Show($"Are you sure you want to delete the folder '{folder.FullPath}'?", "Confirm Delete", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                try
                {
                    Directory.Delete(folder.FullPath, true);
                    if (folder.Parent != null)
                    {
                        folder.Parent.Children.Remove(folder);
                        folder.Parent.Size -= folder.Size;
                        DrawTreemap(folder.Parent);
                        UpdateDriveUsage(folder.Parent.FullPath);
                    }
                    else
                    {
                        DrawTreemap(_currentViewRoot);
                        UpdateDriveUsage(_currentViewRoot.FullPath);
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(ex.Message, "Error");
                }
            }
        }

        private void DeleteFile(StorageItem file)
        {
            if (System.Windows.MessageBox.Show($"Are you sure you want to delete the file '{file.FullPath}'?", "Confirm Delete", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                try
                {
                    File.Delete(file.FullPath);
                    if (file.Parent != null)
                    {
                        file.Parent.Children.Remove(file);
                        file.Parent.Size -= file.Size;
                        DrawTreemap(file.Parent);
                        UpdateDriveUsage(file.Parent.FullPath);
                    }
                    else
                    {
                        DrawTreemap(_currentViewRoot);
                        UpdateDriveUsage(_currentViewRoot.FullPath);
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(ex.Message, "Error");
                }
            }
        }

        private void OpenInExplorer(string path)
        {
            try
            {
                Process.Start("explorer.exe", $"\"{path}\"");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "Error");
            }
        }

        private void OpenFile(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "Error");
            }
        }

        private void AskAI(StorageItem item)
        {
            string type = item.IsFolder ? "folder" : "file";
            string query = $"What is this {type}? Is it safe to remove?\n{item.FullPath}";
            string urlSafeQuery = System.Net.WebUtility.UrlEncode(query);
            string url = $"https://www.google.com/search?q={urlSafeQuery}&udm=50";
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open browser: {ex.Message}", "Error");
            }
        }

        private void UpdateDriveUsage(string path)
        {
            DrivesPanel.Children.Clear();
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                string root = drive.Name;
                string label = drive.VolumeLabel;
                string driveLetter = root.Length >= 2 ? root.Substring(0, 2) : root;
                string driveDisplay = $"{drive.Name} ({driveLetter})"; // Default assignment
                if (drive.DriveType == DriveType.Network)
                {
                    string unc = label;
                    if (string.IsNullOrWhiteSpace(unc))
                        unc = drive.RootDirectory.FullName.TrimEnd('/');
                    if (unc.Substring(0, 2) == driveLetter)
                        unc = "Network Drive";
                    driveDisplay = $"{unc} ({driveLetter})";
                }
                else if (drive.DriveType == DriveType.Fixed)
                {
                    driveDisplay = string.IsNullOrWhiteSpace(label)
                        ? $"Local Disk ({driveLetter})"
                        : $"{label} ({driveLetter})";
                }
                else
                {
                    driveDisplay = $"{drive.DriveType} ({driveLetter})";
                }
                double total = drive.TotalSize;
                double free = drive.AvailableFreeSpace;
                double used = total - free;
                double percent = total > 0 ? (used / total) * 100 : 0;
                double percentFree = total > 0 ? (free / total) * 100 : 0;

                var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 16, 10), Width = 180, Cursor = System.Windows.Input.Cursors.Hand };
                panel.ToolTip = $"Select {driveLetter} ( Ctrl+Alt+{driveLetter[0]} )";
                var nameText = new TextBlock { Text = driveDisplay, FontWeight = FontWeights.Normal, FontSize = 13, Margin = new Thickness(0, 0, 0, 2) };
                var bar = new ProgressBar { Width = 180, Height = 18, Minimum = 0, Maximum = 100, Value = percent };
                bar.Foreground = percentFree < 10 ? Brushes.Red : Brushes.DodgerBlue;
                var freeText = new TextBlock { Text = $"{FormatSize((long)free)} free of {FormatSize((long)total)}", Margin = new Thickness(0, 4, 0, 0), TextAlignment = TextAlignment.Left };
                panel.Children.Add(nameText);
                panel.Children.Add(bar);
                panel.Children.Add(freeText);
                if (_showUsedSpace)
                {
                    var usedPercent = total > 0 ? (used * 100.0 / total) : 0;
                    var usedText = new TextBlock {
                        Text = $"{FormatSize((long)used)} used ({usedPercent:0.#}%)",
                        Margin = new Thickness(0, 2, 0, 0),
                        Foreground = Brushes.DimGray,
                        FontSize = 12
                    };
                    panel.Children.Add(usedText);
                }
                panel.MouseLeftButtonUp += async (s, e) =>
                {
                    ScanningSpinner.Visibility = Visibility.Visible;
                    await System.Threading.Tasks.Task.Yield(); // Force UI update
                    TreemapCanvas.Children.Clear();
                    _treeRoot = new StorageItem { Name = driveDisplay, FullPath = root, IsFolder = true };
                    _currentViewRoot = _treeRoot;
                    _progressStopwatch.Restart();
                    var progress = new Progress<StorageItem>(item =>
                    {
                        var now = DateTime.Now;
                        if ((now - _lastFooterUpdate).TotalMilliseconds > 500)
                        {
                            _lastFooterUpdate = now;
                            Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                DrawTreemap(_currentViewRoot);
                                UpdateFooter(item);
                            });
                        }
                    });
                    var rootItem = await System.Threading.Tasks.Task.Run(() => StorageItem.BuildFromDirectory(root, progress, _treeRoot, null, _ignoreReparsePoints));
                    ScanningSpinner.Visibility = Visibility.Collapsed;
                    _treeRoot = rootItem;
                    _currentViewRoot = rootItem;
                    UpdateBreadcrumbBar(rootItem);
                    DrawTreemap(rootItem);
                    UpdateFooter(rootItem);
                };
                DrivesPanel.Children.Add(panel);
            }
        }

        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MainTabControl.SelectedIndex == 1) // Largest Files tab
            {
                UpdateFilesListView();
            }
        }

        private void UpdateFilesListView()
        {
            if (_treeRoot == null) return;
            var allFiles = new List<StorageItem>();
            CollectFiles(_treeRoot, allFiles);
            var largestFiles = allFiles.OrderByDescending(f => f.Size)
                .Take(100)
                .Select(f => new { f.FullPath, SizeDisplay = FormatSize(f.Size) })
                .ToList();
            FilesListView.ItemsSource = largestFiles;
        }

        private void CollectFiles(StorageItem item, List<StorageItem> files)
        {
            if (item == null) return;
            if (!item.IsFolder)
            {
                files.Add(item);
            }
            else if (item.Children != null)
            {
                foreach (var child in item.Children)
                {
                    CollectFiles(child, files);
                }
            }
        }

        private void File_Open_Click(object sender, RoutedEventArgs e)
        {
            var file = FilesListView.SelectedItem;
            if (file != null)
            {
                var pathProp = file.GetType().GetProperty("FullPath");
                if (pathProp != null)
                {
                    string path = pathProp.GetValue(file) as string;
                    if (!string.IsNullOrEmpty(path))
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(ex.Message, "Error");
                        }
                    }
                }
            }
        }

        private void File_AskAI_Click(object sender, RoutedEventArgs e)
        {
            var file = FilesListView.SelectedItem;
            if (file != null)
            {
                var pathProp = file.GetType().GetProperty("FullPath");
                if (pathProp != null)
                {
                    string path = pathProp.GetValue(file) as string;
                    if (!string.IsNullOrEmpty(path))
                    {
                        string query = $"What is this file? Is it safe to remove?\n{path}";
                        string urlSafeQuery = System.Net.WebUtility.UrlEncode(query);
                        string url = $"https://www.google.com/search?q={urlSafeQuery}&udm=50";
                        try
                        {
                            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Failed to open browser: {ex.Message}", "Error");
                        }
                    }
                }
            }
        }

        private void File_Delete_Click(object sender, RoutedEventArgs e)
        {
            var file = FilesListView.SelectedItem;
            if (file != null)
            {
                var pathProp = file.GetType().GetProperty("FullPath");
                if (pathProp != null)
                {
                    string path = pathProp.GetValue(file) as string;
                    if (!string.IsNullOrEmpty(path))
                    {
                        if (MessageBox.Show($"Are you sure you want to delete the file '{path}'?", "Confirm Delete", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                        {
                            try
                            {
                                File.Delete(path);
                                UpdateFilesListView();
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show(ex.Message, "Error");
                            }
                        }
                    }
                }
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private void BreadcrumbBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Only switch to edit mode if user clicks empty space (not a child element)
            if (e.OriginalSource == sender)
            {
                EnterBreadcrumbEditMode();
            }
        }

        private void EnterBreadcrumbEditMode()
        {
            _breadcrumbEditMode = true;
            BreadcrumbPanel.Visibility = Visibility.Collapsed;
            BreadcrumbPathBox.Visibility = Visibility.Visible;
            BreadcrumbPathBox.Text = _currentViewRoot?.FullPath ?? string.Empty;
            BreadcrumbPathBox.SelectAll();
            BreadcrumbPathBox.Focus();
        }

        private void ExitBreadcrumbEditMode(bool applyPath)
        {
            if (applyPath)
            {
                string path = BreadcrumbPathBox.Text.Trim();
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    // Navigate to the entered path
                    _ = NavigateToPathAsync(path);
                }
            }
            _breadcrumbEditMode = false;
            BreadcrumbPanel.Visibility = Visibility.Visible;
            BreadcrumbPathBox.Visibility = Visibility.Collapsed;
        }

        private async Task NavigateToPathAsync(string path)
        {
            try
            {
                ScanningSpinner.Visibility = Visibility.Visible;
                TreemapCanvas.Children.Clear();
                _treeRoot = new StorageItem { Name = System.IO.Path.GetFileName(path), FullPath = path, IsFolder = true };
                _currentViewRoot = _treeRoot;
                _progressStopwatch.Restart();
                var progress = new Progress<StorageItem>(item =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (_progressStopwatch.ElapsedMilliseconds > 2000)
                        {
                            DrawTreemap(_treeRoot);
                            UpdateFilesListView();
                            _progressStopwatch.Restart();
                        }
                        UpdateFooter(item);
                    });
                });
                var root = await Task.Run(() => StorageItem.BuildFromDirectory(path, progress, _treeRoot, null, _ignoreReparsePoints));
                ScanningSpinner.Visibility = Visibility.Collapsed;
                _treeRoot = root;
                _currentViewRoot = root;
                UpdateBreadcrumbBar(root);
                DrawTreemap(root);
                UpdateFooter(root);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not navigate to path:\n{path}\n\n{ex.Message}", "Navigation Error");
            }
        }

        private void BreadcrumbPathBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ExitBreadcrumbEditMode(true);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                ExitBreadcrumbEditMode(false);
                e.Handled = true;
            }
        }

        private void BreadcrumbPathBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ExitBreadcrumbEditMode(false);
        }
    }
}