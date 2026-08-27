namespace Ducz.Tools.SceneEditor;

/// <summary>
/// Automatic saving. Any change marks the document dirty (via <c>PushUndo</c>); after a short
/// idle the map is written to disk on its own, so a casual user never loses work to a forgotten
/// Ctrl+S. It also keeps a <c>.autosave.json</c> backup next to the scene.
/// </summary>
partial class EditorScene
{
    /// <summary>Seconds of no further change before an autosave fires.</summary>
    private const float AutosaveDelay = 8f;

    /// <summary>Hard ceiling: save at least this often while changes keep coming.</summary>
    private const float AutosaveMax = 90f;

    private bool _dirty;
    private float _idleSinceChange;
    private float _sinceLastSave;

    /// <summary>Called from the mutation path so autosave knows there is work to save.</summary>
    private void MarkDirty()
    {
        _dirty = true;
        _idleSinceChange = 0f;
    }

    private void UpdateAutosave(float dt)
    {
        if (!_dirty || _playing || _exporting)
            return;

        _idleSinceChange += dt;
        _sinceLastSave += dt;

        // Save once the user pauses, or after a long stretch of continuous edits.
        if (_idleSinceChange >= AutosaveDelay || _sinceLastSave >= AutosaveMax)
            Autosave();
    }

    private void Autosave()
    {
        try
        {
            string json = _doc.ToJson();
            File.WriteAllText(_savePath, json);
            // A parallel backup, in case the main file is ever mid-write when something crashes.
            File.WriteAllText(Path.ChangeExtension(Path.GetFullPath(_savePath), null) + ".autosave.json", json);
            _dirty = false;
            _idleSinceChange = 0f;
            _sinceLastSave = 0f;
            SetStatus($"Autosaved  ({System.DateTime.Now:HH:mm})");
        }
        catch (System.Exception ex)
        {
            Log.Error($"Autosave failed: {ex}");
            // Do not clear the dirty flag - try again on the next tick.
        }
    }
}
