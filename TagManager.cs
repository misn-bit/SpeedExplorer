using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace SpeedExplorer;

public class TagManager
{
    private static TagManager? _instance;
    public static TagManager Instance => _instance ??= new TagManager();

    private Dictionary<string, HashSet<string>> _tags = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _tagsFilePath;
    private bool _isDirty = false;
    private readonly object _lock = new object();
    private System.Threading.Timer? _saveTimer;
    private const int SaveDelayMs = 500;

    private TagManager()
    {
        // Store in the same folder as the executable
         _tagsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tags.json");
        LoadTags();
    }

    public void AddTag(string path, string tag)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(tag)) return;

        path = NormalizePath(path);
        tag = tag.Trim();

        lock (_lock)
        {
            if (!_tags.ContainsKey(path))
            {
                _tags[path] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            if (_tags[path].Add(tag))
            {
                _isDirty = true;
                ScheduleSave();
            }
        }
    }

    public void RemoveTag(string path, string tag)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        path = NormalizePath(path);

        lock (_lock)
        {
            if (_tags.ContainsKey(path))
            {
                if (_tags[path].Remove(tag.Trim()))
                {
                    if (_tags[path].Count == 0)
                    {
                        _tags.Remove(path);
                    }
                    _isDirty = true;
                    ScheduleSave();
                }
            }
        }
    }

    public void SetTags(string path, IEnumerable<string> tags)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        path = NormalizePath(path);

        var validTags = tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        lock (_lock)
        {
            if (validTags.Count == 0)
            {
                if (_tags.Remove(path))
                {
                    _isDirty = true;
                    ScheduleSave();
                }
            }
            else
            {
                // Check if different
                if (!_tags.ContainsKey(path) || !_tags[path].SetEquals(validTags))
                {
                    _tags[path] = validTags;
                    _isDirty = true;
                    ScheduleSave();
                }
            }
        }
    }

    public void SetTagsBatch(IEnumerable<string> paths, IEnumerable<string> tags)
    {
        var validTags = tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool changed = false;

        lock (_lock)
        {
            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                var normalizedPath = NormalizePath(path);

                if (validTags.Count == 0)
                {
                    if (_tags.Remove(normalizedPath))
                    {
                        changed = true;
                    }
                }
                else
                {
                    if (!_tags.ContainsKey(normalizedPath) || !_tags[normalizedPath].SetEquals(validTags))
                    {
                        _tags[normalizedPath] = new HashSet<string>(validTags, StringComparer.OrdinalIgnoreCase);
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                _isDirty = true;
                ScheduleSave();
            }
        }
    }

    public void UpdateTagsBatch(IEnumerable<string> paths, IEnumerable<string> toAdd, IEnumerable<string> toRemove)
    {
        var addSet = toAdd.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList();
        var removeSet = toRemove.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList();
        
        if (addSet.Count == 0 && removeSet.Count == 0) return;

        bool changed = false;

        lock (_lock)
        {
            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                var normalizedPath = NormalizePath(path);

                if (!_tags.ContainsKey(normalizedPath))
                {
                    if (addSet.Count > 0)
                    {
                        _tags[normalizedPath] = new HashSet<string>(addSet, StringComparer.OrdinalIgnoreCase);
                        changed = true;
                    }
                    continue;
                }

                var currentTags = _tags[normalizedPath];

                // Add
                foreach (var tag in addSet)
                {
                    if (currentTags.Add(tag))
                    {
                        changed = true;
                    }
                }

                // Remove
                foreach (var tag in removeSet)
                {
                    if (currentTags.Remove(tag))
                    {
                        changed = true;
                        if (currentTags.Count == 0)
                        {
                            _tags.Remove(normalizedPath);
                            break; // Item removed from dictionary, stop processing this path
                        }
                    }
                }
            }

            if (changed)
            {
                _isDirty = true;
                ScheduleSave();
            }
        }
    }

    public HashSet<string> GetTags(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return new HashSet<string>();
        path = NormalizePath(path);

        lock (_lock)
        {
            if (_tags.TryGetValue(path, out var tags))
            {
                return new HashSet<string>(tags); // Return COPY to be thread safe
            }
            return new HashSet<string>();
        }
    }

    public bool HasTags(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        path = NormalizePath(path);
        lock (_lock)
        {
            return _tags.TryGetValue(path, out var tags) && tags.Count > 0;
        }
    }

    public string GetPrimaryTagForSort(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        path = NormalizePath(path);
        lock (_lock)
        {
            if (!_tags.TryGetValue(path, out var tags) || tags.Count == 0)
                return string.Empty;

            string? best = null;
            foreach (var tag in tags)
            {
                if (best == null || string.Compare(tag, best, StringComparison.OrdinalIgnoreCase) < 0)
                    best = tag;
            }

            return best ?? string.Empty;
        }
    }
    
    // For auto-complete or suggestions
    public IEnumerable<string> GetAllKnownTags()
    {
        lock (_lock)
        {
            return _tags.Values.SelectMany(t => t).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t).ToList();
        }
    }

    public void CopyTags(string sourcePath, string destPath)
    {
        sourcePath = NormalizePath(sourcePath);
        destPath = NormalizePath(destPath);
        
        lock (_lock)
        {
            if (_tags.ContainsKey(sourcePath))
            {
                _tags[destPath] = new HashSet<string>(_tags[sourcePath], StringComparer.OrdinalIgnoreCase);
                _isDirty = true;
                ScheduleSave();
            }
            
            // Handle folder copy (children)
            var keys = _tags.Keys.ToList();
            var srcPrefix = sourcePath + Path.DirectorySeparatorChar;
            
            foreach (var key in keys)
            {
                if (key.StartsWith(srcPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var suffix = key.Substring(srcPrefix.Length);
                    var newKey = Path.Combine(destPath, suffix);
                    
                    if (_tags.TryGetValue(key, out var tags))
                    {
                         _tags[newKey] = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);
                         _isDirty = true;
                    }
                }
            }
            if (_isDirty) ScheduleSave();
        }
    }

    public List<string> GetPathsWithTag(string rootPath, string tagQuery)
    {
        lock (_lock)
        {
            var results = new List<string>();
            var lowerQuery = tagQuery.ToLowerInvariant();
            
            bool isGlobal = rootPath == "::ThisPC";
            var root = isGlobal ? "" : NormalizePath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var rootWithSlash = root + Path.DirectorySeparatorChar;

            foreach (var kvp in _tags)
            {
                // Check if path is under root (or is root itself)
                bool isUnderRoot = isGlobal || 
                                   kvp.Key.StartsWith(rootWithSlash, StringComparison.OrdinalIgnoreCase) || 
                                   kvp.Key.Equals(root, StringComparison.OrdinalIgnoreCase);

                if (isUnderRoot)
                {
                    if (kvp.Value.Any(t => t.ToLowerInvariant().Contains(lowerQuery)))
                    {
                        results.Add(kvp.Key);
                    }
                }
            }
            return results;
        }
    }

    public void HandleRename(string oldPath, string newPath)
    {
        oldPath = NormalizePath(oldPath);
        newPath = NormalizePath(newPath);

        lock (_lock)
        {
            // Simple exact match rename
            if (_tags.ContainsKey(oldPath))
            {
                var tags = _tags[oldPath];
                _tags.Remove(oldPath);
                _tags[newPath] = tags;
                _isDirty = true;
            }

            // Handle folder rename (children)
            var keys = _tags.Keys.ToList();
            var oldPrefix = oldPath + Path.DirectorySeparatorChar;
            
            foreach (var key in keys)
            {
                if (key.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var suffix = key.Substring(oldPrefix.Length);
                    var newKey = Path.Combine(newPath, suffix);
                    
                    var tags = _tags[key];
                    _tags.Remove(key);
                    _tags[newKey] = tags;
                    _isDirty = true;
                }
            }

            if (_isDirty) ScheduleSave();
        }
    }

    private void LoadTags()
    {
        try
        {
            if (File.Exists(_tagsFilePath))
            {
                var json = File.ReadAllText(_tagsFilePath);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, HashSet<string>>>(json);
                if (loaded != null)
                {
                    _tags = new Dictionary<string, HashSet<string>>(loaded, StringComparer.OrdinalIgnoreCase);
                }
            }
        }
        catch 
        { 
            // Ignore load errors, start fresh
        }
    }

    private void ScheduleSave()
    {
        // Debounce: reset the timer each time so we only write once after changes settle.
        _saveTimer?.Dispose();
        _saveTimer = new System.Threading.Timer(_ => SaveTagsNow(), null, SaveDelayMs, Timeout.Infinite);
    }

    /// <summary>
    /// Flush any pending save immediately. Call on app shutdown.
    /// </summary>
    public void Flush()
    {
        _saveTimer?.Dispose();
        _saveTimer = null;
        lock (_lock)
        {
            if (_isDirty) SaveTagsNow();
        }
    }

    private void SaveTagsNow()
    {
        try
        {
            string json;
            lock (_lock)
            {
                if (!_isDirty) return;
                // Pretty print for user readability since they might edit it manually
                var options = new JsonSerializerOptions { WriteIndented = true };
                json = JsonSerializer.Serialize(_tags, options);
                _isDirty = false;
            }
            File.WriteAllText(_tagsFilePath, json);
        }
        catch
        {
            // Ignore save errors
        }
    }

    private string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.StartsWith("::")) return path;
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
