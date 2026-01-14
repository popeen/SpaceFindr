using System.IO;
using System.Windows;

namespace SpaceFindr
{
    public class TreemapRect
    {
        public StorageItem Item { get; set; }
        public Rect Rect { get; set; }
    }

    public static class TreemapHelper
    {
        // Squarified treemap layout
        public static List<TreemapRect> Squarify(List<StorageItem> items, double x, double y, double width, double height)
        {
            var result = new List<TreemapRect>();
            if (items == null || items.Count == 0) return result;
            double total = items.Sum(i => (double)i.Size);
            SquarifyRecursive(items.OrderByDescending(i => i.Size).ToList(), x, y, width, height, total, result);
            return result;
        }

        private static void SquarifyRecursive(List<StorageItem> items, double x, double y, double width, double height, double total, List<TreemapRect> result)
        {
            if (items.Count == 0) return;
            if (items.Count == 1)
            {
                result.Add(new TreemapRect { Item = items[0], Rect = new Rect(x, y, width, height) });
                return;
            }
            bool horizontal = width > height;
            double acc = 0;
            int split = 0;
            for (int i = 0; i < items.Count; i++)
            {
                acc += items[i].Size;
                if (acc >= total / 2) { split = i + 1; break; }
            }
            if (split == 0) split = 1;
            var first = items.Take(split).ToList();
            var rest = items.Skip(split).ToList();
            double firstTotal = first.Sum(i => (double)i.Size);
            if (horizontal)
            {
                double w = width * (firstTotal / total);
                double x2 = x + w;
                SquarifyRecursive(first, x, y, w, height, firstTotal, result);
                SquarifyRecursive(rest, x2, y, width - w, height, total - firstTotal, result);
            }
            else
            {
                double h = height * (firstTotal / total);
                double y2 = y + h;
                SquarifyRecursive(first, x, y, width, h, firstTotal, result);
                SquarifyRecursive(rest, x, y2, width, height - h, total - firstTotal, result);
            }
        }
    }

    public class StorageItem
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public long Size { get; set; }
        public bool IsFolder { get; set; }
        public List<StorageItem> Children { get; set; } = new List<StorageItem>();
        public StorageItem Parent { get; set; }
        public bool IsLoaded { get; set; } // True if children have been loaded

        private static bool ShouldSkipFile(FileInfo file)
        {
            try
            {
                var attrs = file.Attributes;
                // Skip cloud/placeholder files (reparse points, offline, temporary, sparse, etc.)
                if ((attrs & FileAttributes.ReparsePoint) != 0 ||
                    (attrs & FileAttributes.Offline) != 0 ||
                    (attrs & FileAttributes.Temporary) != 0 ||
                    (attrs & FileAttributes.SparseFile) != 0)
                    return true;
                // Skip Nextcloud and other cloud placeholder files by extension
                string ext = file.Extension.ToLowerInvariant();
                if (ext == ".nextcloud" || ext == ".cloud" || ext == ".cloudf")
                    return true;
                // Skip zero-length files
                if (file.Length == 0)
                    return true;
            }
            catch { return true; }
            return false;
        }

        public static StorageItem BuildFromDirectory(string path, IProgress<StorageItem> progress = null, StorageItem rootRef = null, StorageItem parent = null)
        {
            var dirInfo = new DirectoryInfo(path);
            // Skip any folder named Nextcloud TODO, find a better way to handle this
            if (dirInfo.Name.Equals("Nextcloud", StringComparison.OrdinalIgnoreCase))
                return null;
            var root = rootRef ?? new StorageItem
            {
                Name = dirInfo.Name,
                FullPath = dirInfo.FullName,
                IsFolder = true,
                Parent = parent
            };
            long totalSize = 0;
            DateTime lastReport = DateTime.Now;
            try
            {
                foreach (var dir in dirInfo.GetDirectories())
                {
                    var child = BuildFromDirectory(dir.FullName, progress, null, root);
                    if (child == null) continue;
                    root.Children.Add(child);
                    totalSize += child.Size;
                    if ((DateTime.Now - lastReport).TotalMilliseconds > 500)
                    {
                        progress?.Report(rootRef ?? root);
                        lastReport = DateTime.Now;
                    }
                }
                foreach (var file in dirInfo.GetFiles())
                {
                    if (ShouldSkipFile(file))
                        continue;
                    var fileItem = new StorageItem
                    {
                        Name = file.Name,
                        FullPath = file.FullName,
                        Size = file.Length,
                        IsFolder = false,
                        Parent = root
                    };
                    root.Children.Add(fileItem);
                    totalSize += file.Length;
                    if ((DateTime.Now - lastReport).TotalMilliseconds > 500)
                    {
                        progress?.Report(rootRef ?? root);
                        lastReport = DateTime.Now;
                    }
                }
            }
            catch { /* Ignore access errors */ }
            root.Size = totalSize;
            progress?.Report(rootRef ?? root);
            return root;
        }

        public static StorageItem LoadChildren(StorageItem item)
        {
            if (!item.IsFolder || item.IsLoaded) return item;
            var dirInfo = new DirectoryInfo(item.FullPath);
            // Skip any folder named Nextcloud TODO, find a better way to handle this
            if (dirInfo.Name.Equals("Nextcloud", StringComparison.OrdinalIgnoreCase))
                return item;
            try
            {
                item.Children.Clear();
                long totalSize = 0;
                foreach (var dir in dirInfo.GetDirectories())
                {
                    if (dir.Name.Equals("Nextcloud", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var child = new StorageItem
                    {
                        Name = dir.Name,
                        FullPath = dir.FullName,
                        IsFolder = true,
                        Parent = item,
                        Size = 0
                    };
                    LoadChildren(child);
                    item.Children.Add(child);
                    totalSize += child.Size;
                }
                foreach (var file in dirInfo.GetFiles())
                {
                    if (ShouldSkipFile(file))
                        continue;
                    var fileItem = new StorageItem
                    {
                        Name = file.Name,
                        FullPath = file.FullName,
                        Size = file.Length,
                        IsFolder = false,
                        Parent = item
                    };
                    item.Children.Add(fileItem);
                    totalSize += file.Length;
                }
                item.Size = totalSize;
                item.IsLoaded = true;
            }
            catch { }
            return item;
        }
    }
}
