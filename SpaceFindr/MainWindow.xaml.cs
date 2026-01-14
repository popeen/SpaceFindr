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

        public MainWindow()
        {
            InitializeComponent();
            this.PreviewMouseDown += MainWindow_PreviewMouseDown;
            UpdateDriveUsage(null); // Load all drives on startup
        }

        private void MainWindow_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.XButton1)
            {
                NavigateBack();
                e.Handled = true;
            }
            else if (e.ChangedButton == System.Windows.Input.MouseButton.XButton2)
            {
                NavigateForward();
                e.Handled = true;
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
                if (_currentViewRoot.Parent == null)
                    BackButton.Visibility = Visibility.Collapsed;
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
                    BackButton.Visibility = _currentViewRoot.Parent == null ? Visibility.Collapsed : Visibility.Visible;
                    UpdateFooter(_currentViewRoot);
                }
            }
        }

        private void UpdateFooter(StorageItem folder)
        {
            if (folder == null)
            {
                CurrentFolderText.Text = "Current folder: ";
                FolderFileCountText.Text = "Items: 0 folders, 0 files";
                return;
            }
            // Show the path currently being scanned if a scan is in progress
            if (ScanningPanel.Visibility == Visibility.Visible)
            {
                CurrentFolderText.Text = $"Scanning: {folder.FullPath}";
            }
            else
            {
                var current = _currentViewRoot ?? folder;
                CurrentFolderText.Text = $"Current folder: {current.FullPath}";
            }
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

        private void UpdateBreadcrumbBar(StorageItem current)
        {
            BreadcrumbPanel.Children.Clear();
            if (current == null) return;
            var path = current.FullPath;
            // Handle root (e.g. C:\)
            if (System.IO.Path.GetPathRoot(path) == path)
            {
                var btn = new System.Windows.Controls.Button
                {
                    Content = path.TrimEnd(System.IO.Path.DirectorySeparatorChar),
                    Margin = new Thickness(0, 0, 4, 0),
                    Padding = new Thickness(6, 0, 6, 0),
                    Tag = path
                };
                btn.Click += async (s, e) =>
                {
                    string targetPath = (string)btn.Tag;
                    if (_treeRoot != null && string.Equals(_treeRoot.FullPath, targetPath, System.StringComparison.OrdinalIgnoreCase))
                    {
                        _currentViewRoot = _treeRoot;
                        DrawTreemap(_treeRoot);
                        BackButton.Visibility = _treeRoot.Parent == null ? Visibility.Collapsed : Visibility.Visible;
                    }
                    else
                    {
                        ScanningPanel.Visibility = Visibility.Visible;
                        TreemapCanvas.Children.Clear();
                        _treeRoot = new StorageItem { Name = System.IO.Path.GetFileName(targetPath), FullPath = targetPath, IsFolder = true };
                        _currentViewRoot = _treeRoot;
                        _progressStopwatch.Restart();
                        var progress = new Progress<StorageItem>(item =>
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                if (_progressStopwatch.ElapsedMilliseconds > 2000)
                                {
                                    DrawTreemap(_treeRoot);
                                    UpdateFilesListView(); // Throttle file list updates to match treemap
                                    _progressStopwatch.Restart();
                                }
                                UpdateFooter(item);
                            });
                        });
                        var root = await System.Threading.Tasks.Task.Run(() => StorageItem.BuildFromDirectory(targetPath, progress, _treeRoot));
                        ScanningPanel.Visibility = Visibility.Collapsed;
                        _treeRoot = root;
                        _currentViewRoot = root;
                        UpdateBreadcrumbBar(root);
                        DrawTreemap(root);
                        BackButton.Visibility = Visibility.Collapsed;
                        UpdateFooter(root);
                    }
                };
                BreadcrumbPanel.Children.Add(btn);
                UpdateFooter(current);
                return;
            }
            // Normal path
            var pathParts = path.TrimEnd(System.IO.Path.DirectorySeparatorChar).Split(System.IO.Path.DirectorySeparatorChar);
            string root = System.IO.Path.GetPathRoot(path);
            string cumulativePath = root;
            for (int i = 0; i < pathParts.Length; i++)
            {
                string part = pathParts[i];
                if (string.IsNullOrEmpty(part)) continue;
                string display = (i == 0) ? root.TrimEnd(System.IO.Path.DirectorySeparatorChar) : part;
                if (i > 0) cumulativePath = System.IO.Path.Combine(cumulativePath, part);
                var btn = new System.Windows.Controls.Button
                {
                    Content = display,
                    Margin = new Thickness(0, 0, 4, 0),
                    Padding = new Thickness(6, 0, 6, 0),
                    Tag = cumulativePath
                };
                btn.Click += async (s, e) =>
                {
                    string targetPath = (string)btn.Tag;
                    if (_treeRoot != null && string.Equals(_treeRoot.FullPath, targetPath, System.StringComparison.OrdinalIgnoreCase))
                    {
                        _currentViewRoot = _treeRoot;
                        DrawTreemap(_treeRoot);
                        BackButton.Visibility = _treeRoot.Parent == null ? Visibility.Collapsed : Visibility.Visible;
                    }
                    else
                    {
                        ScanningPanel.Visibility = Visibility.Visible;
                        TreemapCanvas.Children.Clear();
                        _treeRoot = new StorageItem { Name = System.IO.Path.GetFileName(targetPath), FullPath = targetPath, IsFolder = true };
                        _currentViewRoot = _treeRoot;
                        _progressStopwatch.Restart();
                        var progress = new Progress<StorageItem>(item =>
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                if (_progressStopwatch.ElapsedMilliseconds > 2000)
                                {
                                    DrawTreemap(_treeRoot);
                                    UpdateFilesListView(); // Throttle file list updates to match treemap
                                    _progressStopwatch.Restart();
                                }
                                UpdateFooter(item);
                            });
                        });
                        var rootItem = await System.Threading.Tasks.Task.Run(() => StorageItem.BuildFromDirectory(targetPath, progress, _treeRoot));
                        ScanningPanel.Visibility = Visibility.Collapsed;
                        _treeRoot = rootItem;
                        _currentViewRoot = rootItem;
                        UpdateBreadcrumbBar(rootItem);
                        DrawTreemap(rootItem);
                        BackButton.Visibility = Visibility.Collapsed;
                        UpdateFooter(rootItem);
                    }
                };
                BreadcrumbPanel.Children.Add(btn);
                if (i < pathParts.Length - 1)
                {
                    BreadcrumbPanel.Children.Add(new TextBlock { Text = ">", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
                }
            }
            UpdateFooter(current);
            if (current != null)
                UpdateDriveUsage(current.FullPath);
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
                    ScanningPanel.Visibility = Visibility.Visible;
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
                                DrawTreemap(_treeRoot);
                                UpdateFilesListView(); // Throttle file list updates to match treemap
                                _progressStopwatch.Restart();
                            }
                        });
                    });

                    // Run scan on background thread
                    var root = await Task.Run(() => StorageItem.BuildFromDirectory(selectedPath, progress, _treeRoot), _loadingCts.Token);

                    // Final UI update after scan
                    ScanningPanel.Visibility = Visibility.Collapsed;
                    _treeRoot = root;
                    _currentViewRoot = root;
                    UpdateBreadcrumbBar(root);
                    DrawTreemap(root);
                    BackButton.Visibility = Visibility.Collapsed;
                    UpdateFooter(root);
                }
            }
            catch (Exception ex)
            {
                System.Windows.Clipboard.SetText(ex.ToString());
                System.Windows.MessageBox.Show(ex.ToString() + "\n\n(The error has been copied to your clipboard)", "Error");
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateBack();
        }

        private void DrawTreemap(StorageItem root)
        {
            TreemapCanvas.Children.Clear();
            if (root == null || root.Children == null || root.Children.Count == 0) return;

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

            var rects = TreemapHelper.Squarify(root.Children, 0, 0, width, height);
            foreach (var treemapRect in rects)
            {
                var child = treemapRect.Item;
                var r = treemapRect.Rect;
                Brush fillBrush;
                if (child.IsFolder)
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
                    ToolTip = $"{child.Name}\n{FormatSize(child.Size)}"
                };

                // Context menu
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
                rect.MouseLeftButtonUp += async (s, e) =>
                {
                    if (child.IsFolder)
                    {
                        _loadingCts?.Cancel();
                        _loadingCts = new CancellationTokenSource();
                        ScanningPanel.Visibility = Visibility.Visible;
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
                            await Task.Run(() => StorageItem.BuildFromDirectory(child.FullPath, progress, child), _loadingCts.Token);
                        }
                        ScanningPanel.Visibility = Visibility.Collapsed;
                        _backStack.Push(_currentViewRoot);
                        _currentViewRoot = child;
                        _forwardStack.Clear();
                        BackButton.Visibility = Visibility.Visible;
                        UpdateBreadcrumbBar(child);
                        DrawTreemap(child);
                    }
                };
                TreemapCanvas.Children.Add(rect);
                Canvas.SetLeft(rect, r.X);
                Canvas.SetTop(rect, r.Y);

                double minLabelSize = 40;
                double medLabelSize = 80;
                string labelText = null;
                if (r.Width > medLabelSize && r.Height > minLabelSize)
                    labelText = $"{child.Name}\n{FormatSize(child.Size)}";
                else if (r.Width > minLabelSize && r.Height > minLabelSize)
                    labelText = child.Name;
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
                        if ((attrs & System.IO.FileAttributes.ReparsePoint) != 0 ||
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
                double percent = total > 0 ? ((total - free) / total) * 100 : 0;
                double percentFree = total > 0 ? (free / total) * 100 : 0;

                var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 16, 0), Width = 180, Cursor = System.Windows.Input.Cursors.Hand };
                var nameText = new TextBlock { Text = driveDisplay, FontWeight = FontWeights.Normal, FontSize = 13, Margin = new Thickness(0, 0, 0, 2) };
                var bar = new ProgressBar { Width = 180, Height = 18, Minimum = 0, Maximum = 100, Value = percent };
                bar.Foreground = percentFree < 10 ? Brushes.Red : Brushes.DodgerBlue;
                var freeText = new TextBlock { Text = $"{FormatSize((long)free)} free of {FormatSize((long)total)}", Margin = new Thickness(0, 4, 0, 0), TextAlignment = TextAlignment.Left };
                panel.Children.Add(nameText);
                panel.Children.Add(bar);
                panel.Children.Add(freeText);
                panel.MouseLeftButtonUp += async (s, e) =>
                {
                    ScanningPanel.Visibility = Visibility.Visible;
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
                                DrawTreemap(_treeRoot);
                                UpdateFooter(item);
                            });
                        }
                    });
                    var rootItem = await System.Threading.Tasks.Task.Run(() => StorageItem.BuildFromDirectory(root, progress, _treeRoot));
                    ScanningPanel.Visibility = Visibility.Collapsed;
                    _treeRoot = rootItem;
                    _currentViewRoot = rootItem;
                    UpdateBreadcrumbBar(rootItem);
                    DrawTreemap(rootItem);
                    BackButton.Visibility = Visibility.Collapsed;
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

    }
}