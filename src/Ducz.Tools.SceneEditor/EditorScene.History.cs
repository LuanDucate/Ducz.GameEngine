using Ducz.Serialization;

namespace Ducz.Tools.SceneEditor;

/// <summary>
/// Undo / redo by document snapshots. The scene JSON is small, so every mutating
/// action simply stores the whole document before changing it.
/// </summary>
partial class EditorScene
{
    private const int MaxHistory = 100;
    private readonly List<string> _undoStack = new();
    private readonly List<string> _redoStack = new();

    /// <summary>Call right before mutating the document.</summary>
    private void PushUndo()
    {
        _undoStack.Add(_doc.ToJson());
        if (_undoStack.Count > MaxHistory)
            _undoStack.RemoveAt(0);
        _redoStack.Clear();
        MarkDirty();
    }

    private void Undo()
    {
        if (_undoStack.Count == 0)
        {
            SetStatus("Nothing to undo");
            return;
        }
        _redoStack.Add(_doc.ToJson());
        RestoreSnapshot(_undoStack[^1]);
        _undoStack.RemoveAt(_undoStack.Count - 1);
        SetStatus($"Undo ({_undoStack.Count} left)");
    }

    private void Redo()
    {
        if (_redoStack.Count == 0)
        {
            SetStatus("Nothing to redo");
            return;
        }
        _undoStack.Add(_doc.ToJson());
        RestoreSnapshot(_redoStack[^1]);
        _redoStack.RemoveAt(_redoStack.Count - 1);
        SetStatus("Redo");
    }

    private void RestoreSnapshot(string json)
    {
        _doc = SceneDocument.FromJson(json);
        _painting = false;
        _rectStart = null;
        AfterDocumentReplaced();
        RefreshModelPaletteFromDoc();
    }
}
