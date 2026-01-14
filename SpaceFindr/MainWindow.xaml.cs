using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.IO;
using System.Diagnostics;
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

        public MainWindow()
        {
            InitializeComponent();
            this.PreviewMouseDown += MainWindow_PreviewMouseDown;
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
            CurrentFolderText.Text = $"Current folder: {folder.FullPath}";
            if (folder.Children == null)
            {
                FolderFileCountText.Text = "Items: 0 folders, 0 files";
                return;
            }
            int folderCount = folder.Children.Count(x => x != null && x.IsFolder);
            int fileCount = folder.Children.Count(x => x != null && !x.IsFolder);
            FolderFileCountText.Text = $"Items: {folderCount} folders, {fileCount} files";
        }

        private void UpdateBreadcrumbBar(StorageItem current)
        {
            BreadcrumbPanel.Children.Clear();
            if (current == null) return;
            var pathParts = current.FullPath.Split(System.IO.Path.DirectorySeparatorChar);
            string cumulativePath = pathParts[0];
            StorageItem item = _treeRoot;
            for (int i = 0; i < pathParts.Length; i++)
            {
                string part = pathParts[i];
                if (string.IsNullOrEmpty(part)) continue;
                var btn = new System.Windows.Controls.Button
                {
                    Content = part,
                    Margin = new Thickness(0, 0, 4, 0),
                    Padding = new Thickness(6, 0, 6, 0),
                    Tag = i
                };
                btn.Click += (s, e) =>
                {
                    StorageItem target = _treeRoot;
                    for (int j = 1; j <= (int)btn.Tag; j++)
                    {
                        string segment = pathParts[j];
                        target = target.Children?.FirstOrDefault(x => x.Name == segment && x.IsFolder) ?? target;
                    }
                    if (target != null)
                    {
                        _currentViewRoot = target;
                        DrawTreemap(target);
                        BackButton.Visibility = target.Parent == null ? Visibility.Collapsed : Visibility.Visible;
                    }
                };
                BreadcrumbPanel.Children.Add(btn);
                if (i < pathParts.Length - 1)
                {
                    BreadcrumbPanel.Children.Add(new TextBlock { Text = ">", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
                }
            }
            // At the end, update the footer
            UpdateFooter(current);
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
                        if (_progressStopwatch.ElapsedMilliseconds > 2000)
                        {
                            DrawTreemap(_treeRoot);
                            _progressStopwatch.Restart();
                        }
                        UpdateFooter(item);
                    });
                    var root = await Task.Run(() => StorageItem.BuildFromDirectory(selectedPath, progress, _treeRoot), _loadingCts.Token);
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
                if (child.IsFolder)
                {
                    var deleteFolder = new MenuItem { Header = "Delete Folder" };
                    deleteFolder.Click += (s, e) => DeleteFolder(child);
                    var openInExplorer = new MenuItem { Header = "Open in File Explorer" };
                    openInExplorer.Click += (s, e) => OpenInExplorer(child.FullPath);
                    contextMenu.Items.Add(openInExplorer);
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
                    DrawTreemap(_currentViewRoot);
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
                    }
                    else
                    {
                        DrawTreemap(_currentViewRoot);
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
    }
}