using System;
using System.Collections.Generic;

namespace SpeedExplorer;

/// <summary>
/// Manages undo/redo stacks for file operations
/// Singleton pattern ensures single instance across application
/// </summary>
public class UndoRedoManager
{
    private static UndoRedoManager? _instance;
    private static readonly object _lock = new object();

    private Stack<FileOperation> _undoStack;
    private Stack<FileOperation> _redoStack;

    public static UndoRedoManager Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = new UndoRedoManager();
                    }
                }
            }
            return _instance;
        }
    }

    private UndoRedoManager()
    {
        _undoStack = new Stack<FileOperation>();
        _redoStack = new Stack<FileOperation>();
    }

    public bool CanUndo { get { lock (_lock) { return _undoStack.Count > 0; } } }
    public bool CanRedo { get { lock (_lock) { return _redoStack.Count > 0; } } }

    /// <summary>
    /// Records a new operation. Clears the redo stack.
    /// </summary>
    public void RecordOperation(FileOperation operation)
    {
        lock (_lock)
        {
            _undoStack.Push(operation);
            _redoStack.Clear(); // New action invalidates redo history
        }
    }

    /// <summary>
    /// Undoes the most recent operation
    /// </summary>
    public void Undo()
    {
        FileOperation? operation;
        lock (_lock)
        {
            if (_undoStack.Count == 0)
                return;
            operation = _undoStack.Pop();
        }

        try
        {
            operation.Undo();
            lock (_lock) { _redoStack.Push(operation); }
        }
        catch (Exception ex)
        {
            // If undo fails, don't push to redo stack
            System.Windows.Forms.MessageBox.Show(
                $"Undo failed: {ex.Message}\n\nOperation: {operation.GetDescription()}",
                "Undo Error",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// Redoes the most recently undone operation
    /// </summary>
    public void Redo()
    {
        FileOperation? operation;
        lock (_lock)
        {
            if (_redoStack.Count == 0)
                return;
            operation = _redoStack.Pop();
        }

        try
        {
            operation.Redo();
            lock (_lock) { _undoStack.Push(operation); }
        }
        catch (Exception ex)
        {
            // If redo fails, don't push back to undo stack
            System.Windows.Forms.MessageBox.Show(
                $"Redo failed: {ex.Message}\n\nOperation: {operation.GetDescription()}",
                "Redo Error",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// Gets description of the next undo operation
    /// </summary>
    public string GetUndoDescription()
    {
        lock (_lock)
        {
            if (_undoStack.Count == 0)
                return Localization.T("undo");

            return string.Format(Localization.T("undo_with"), _undoStack.Peek().GetDescription());
        }
    }

    /// <summary>
    /// Gets description of the next redo operation
    /// </summary>
    public string GetRedoDescription()
    {
        lock (_lock)
        {
            if (_redoStack.Count == 0)
                return Localization.T("redo");

            return string.Format(Localization.T("redo_with"), _redoStack.Peek().GetDescription());
        }
    }

    /// <summary>
    /// Clears both undo and redo stacks
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _undoStack.Clear();
            _redoStack.Clear();
        }
    }
}
