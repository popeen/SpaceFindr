using System.IO;
using System.Windows;
using System;
using System.Collections.Generic;
using System.Linq;

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

        private static bool ShouldSkipFile(FileInfo file, bool ignoreReparsePoints = true)
        {
            try
            {
                var attrs = file.Attributes;
                // Skip cloud/placeholder files (reparse points, offline, temporary, sparse, etc.)
                if (ignoreReparsePoints && (attrs & FileAttributes.ReparsePoint) != 0)
                    return true;
                // Skip zero-length files
                if (file.Length == 0)
                    return true;
            }
            catch { return true; }
            return false;
        }

        private static bool ShouldSkipDirectory(DirectoryInfo directory, bool ignoreReparsePoints = true)
        {
            try
            {
                var attrs = directory.Attributes;
                // Skip reparse point directories (symbolic links, junctions, etc.) if configured to do so
                if (ignoreReparsePoints && (attrs & FileAttributes.ReparsePoint) != 0)
                    return true;
            }
            catch { return true; }
            return false;
        }

        public static StorageItem BuildFromDirectory(string path, IProgress<StorageItem> progress = null, StorageItem rootRef = null, StorageItem parent = null, bool ignoreReparsePoints = true)
        {
            // Use FileSystemEnumerator for efficient scanning
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
                ReturnSpecialDirectories = false
            };
            string rootName = System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar));
            var root = rootRef ?? new StorageItem
            {
                Name = string.IsNullOrEmpty(rootName) ? path : rootName,
                FullPath = path,
                IsFolder = true,
                Parent = parent
            };
            long totalSize = 0;
            DateTime lastReport = DateTime.Now;
            try
            {
                var enumerable = new System.IO.Enumeration.FileSystemEnumerable<StorageItem>(
                    path,
                    (ref System.IO.Enumeration.FileSystemEntry entry) =>
                    {
                        string fileName = entry.FileName.ToString();
                        string fullPath = System.IO.Path.Combine(path, fileName);
                        bool isDir = entry.Attributes.HasFlag(FileAttributes.Directory);
                        if (isDir)
                        {
                            if (ignoreReparsePoints && entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                                return null;
                            if (fileName == "." || fileName == "..")
                                return null;
                            var child = BuildFromDirectory(fullPath, progress, null, root, ignoreReparsePoints);
                            return child;
                        }
                        else
                        {
                            if (ignoreReparsePoints && entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                                return null;
                            if (entry.Length == 0)
                                return null;
                            string ext = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
                            if (ext == ".nextcloud" || ext == ".cloud" || ext == ".cloudf")
                                return null;
                            return new StorageItem
                            {
                                Name = fileName,
                                FullPath = fullPath,
                                Size = entry.Length,
                                IsFolder = false,
                                Parent = root
                            };
                        }
                    },
                    options
                );
                using (var enumerator = enumerable.GetEnumerator())
                {
                    while (enumerator.MoveNext())
                    {
                        var item = enumerator.Current;
                        if (item == null) continue;
                        root.Children.Add(item);
                        totalSize += item.Size;
                        if ((DateTime.Now - lastReport).TotalMilliseconds > 500)
                        {
                            progress?.Report(rootRef ?? root);
                            lastReport = DateTime.Now;
                        }
                    }
                }
            }
            catch { /* Ignore access errors */ }
            root.Size = totalSize;
            progress?.Report(rootRef ?? root);
            return root;
        }

        public static StorageItem LoadChildren(StorageItem item, bool ignoreReparsePoints = true)
        {
            if (!item.IsFolder || item.IsLoaded) return item;
            var dirInfo = new DirectoryInfo(item.FullPath);
            if (ShouldSkipDirectory(dirInfo, ignoreReparsePoints))
                return item;
            try
            {
                item.Children.Clear();
                long totalSize = 0;
                foreach (var dir in dirInfo.GetDirectories())
                {
                    if (ShouldSkipDirectory(dir, ignoreReparsePoints))
                        continue;
                    var child = new StorageItem
                    {
                        Name = dir.Name,
                        FullPath = dir.FullName,
                        IsFolder = true,
                        Parent = item,
                        Size = 0
                    };
                    LoadChildren(child, ignoreReparsePoints);
                    item.Children.Add(child);
                    totalSize += child.Size;
                }
                foreach (var file in dirInfo.GetFiles())
                {
                    if (ShouldSkipFile(file, ignoreReparsePoints))
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
