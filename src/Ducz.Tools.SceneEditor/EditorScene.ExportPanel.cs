using System.Globalization;
using System.Numerics;
using Ducz.Export;
using Ducz.UI;

namespace Ducz.Tools.SceneEditor;

/// <summary>
/// The "Export GLB" options panel: scale (with unit presets), merge static geometry,
/// Godot suffixes, embed models. Choices are remembered per user; "Export..." then opens
/// the native save dialog.
/// </summary>
partial class EditorScene
{
    private Panel _exportPanel = null!;
    private TextBox _exportScaleBox = null!;
    private CheckBox _exportMerge = null!;
    private CheckBox _exportSuffixes = null!;
    private CheckBox _exportModels = null!;
    private Label _exportHint = null!;

    private void BuildExportPanel()
    {
        _exportPanel = _canvas.AddChild(new Panel
        {
            Anchor = Anchor.Center,
            Size = new Vector2(400, 330),
            BackgroundColor = Color.Black.WithAlpha(0.85f),
            BorderColor = Color.White.WithAlpha(0.2f),
            Visible = false
        });

        _exportPanel.AddChild(new Label("Export map as GLB")
        {
            Anchor = Anchor.TopCenter, Position = new Vector2(0, 12), FontSize = 18, Color = Color.FromHex("#7fd4ff")
        });

        // Scale row
        _exportPanel.AddChild(new Label("Scale") { Anchor = Anchor.TopLeft, Position = new Vector2(20, 54), FontSize = 15 });
        _exportScaleBox = _exportPanel.AddChild(new TextBox
        {
            Anchor = Anchor.TopLeft, Position = new Vector2(80, 48), Size = new Vector2(90, 30), FontSize = 15, Text = "1"
        });
        _exportScaleBox.TextChanged += _ => RefreshExportHint();

        float x = 180;
        foreach (var (label, value) in new[] { ("x1 (m)", 1f), ("x100 (cm)", 100f), ("x0.01", 0.01f) })
        {
            float captured = value;
            var preset = _exportPanel.AddChild(new Button(label)
            {
                Anchor = Anchor.TopLeft, Position = new Vector2(x, 48), Size = new Vector2(label.Length > 6 ? 76 : 50, 30), FontSize = 12
            });
            preset.Clicked += () => { _exportScaleBox.Text = FormatScale(captured); RefreshExportHint(); };
            x += preset.Size.X + 4;
        }

        _exportHint = _exportPanel.AddChild(new Label("")
        {
            Anchor = Anchor.TopLeft, Position = new Vector2(20, 88), FontSize = 12, Color = Color.White.WithAlpha(0.6f)
        });

        _exportMerge = _exportPanel.AddChild(new CheckBox("Merge static geometry (one mesh per material)")
        {
            Anchor = Anchor.TopLeft, Position = new Vector2(20, 130), Size = new Vector2(320, 26), FontSize = 14
        });
        _exportSuffixes = _exportPanel.AddChild(new CheckBox("Godot collision suffixes (-col / -rigid)")
        {
            Anchor = Anchor.TopLeft, Position = new Vector2(20, 162), Size = new Vector2(320, 26), FontSize = 14
        });
        _exportModels = _exportPanel.AddChild(new CheckBox("Embed imported models (props / maps)")
        {
            Anchor = Anchor.TopLeft, Position = new Vector2(20, 194), Size = new Vector2(320, 26), FontSize = 14
        });

        var exportButton = _exportPanel.AddChild(new Button("Export...")
        {
            Anchor = Anchor.BottomRight, Position = new Vector2(-16, -16), Size = new Vector2(150, 36), FontSize = 15,
            NormalColor = UITheme.AccentColor.Darkened(0.35f)
        });
        exportButton.Clicked += StartGlbExport;

        var cancelButton = _exportPanel.AddChild(new Button("Cancel")
        {
            Anchor = Anchor.BottomLeft, Position = new Vector2(16, -16), Size = new Vector2(120, 36), FontSize = 15
        });
        cancelButton.Clicked += () => _exportPanel.Visible = false;
    }

    private static string FormatScale(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private bool TryParseScale(out float scale)
    {
        return float.TryParse(_exportScaleBox.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out scale)
               && scale > 0f && !float.IsInfinity(scale);
    }

    private void RefreshExportHint()
    {
        if (!TryParseScale(out float scale))
        {
            _exportHint.Text = "Scale must be a positive number";
            _exportHint.Color = Color.FromHex("#ff8a80");
            return;
        }
        _exportHint.Color = Color.White.WithAlpha(0.6f);
        _exportHint.Text = MathF.Abs(scale - 1f) < 1e-6f
            ? "1 unit = 1 meter (Godot, Blender, Unity, glTF default)"
            : $"A 1 m block will measure {FormatScale(scale)} units in the target tool";
    }

    /// <summary>Opens the export options panel (Export GLB button / Ctrl+E).</summary>
    private void ExportGlb()
    {
        if (_playing || _exporting)
            return;

        var settings = EditorSettings.Load();
        _exportScaleBox.Text = FormatScale(settings.GlbScale > 0f ? settings.GlbScale : 1f);
        _exportMerge.Checked = settings.GlbMergeByMaterial;
        _exportSuffixes.Checked = settings.GlbGodotSuffixes;
        _exportModels.Checked = settings.GlbIncludeModels;
        RefreshExportHint();
        _exportPanel.Visible = true;
        _exportPanel.MoveToFront();
    }

    /// <summary>Reads the panel, remembers the choices, asks where to save and exports.</summary>
    private void StartGlbExport()
    {
        if (!TryParseScale(out float scale))
        {
            SetStatus("Export scale must be a positive number");
            return;
        }

        var options = new GlbExportOptions
        {
            Scale = scale,
            MergeByMaterial = _exportMerge.Checked,
            GodotSuffixes = _exportSuffixes.Checked,
            IncludeModels = _exportModels.Checked
        };

        var settings = EditorSettings.Load();
        settings.GlbScale = scale;
        settings.GlbMergeByMaterial = options.MergeByMaterial;
        settings.GlbGodotSuffixes = options.GodotSuffixes;
        settings.GlbIncludeModels = options.IncludeModels;
        settings.Save();

        _exportPanel.Visible = false;
        SaveDocument();
        string fileName = MakeFileSafe(_doc.Name) + ".glb";

        if (!FileDialogs.IsSupported)
        {
            // No native dialogs on this platform: fall back to the project's Export folder.
            ExportGlbTo(Path.Combine(ExportRoot, fileName), options);
            return;
        }

        string initialDirectory = Directory.Exists(settings.LastGlbExportDirectory)
            ? settings.LastGlbExportDirectory!
            : ExportRoot;

        _exporting = true;
        SetStatus("Choose where to save the GLB...");
        FileDialogs.SaveFileAsync("Export map as GLB", "glTF binary (*.glb)|*.glb|All files|*.*",
            initialDirectory, fileName,
            onPicked: path => Post(() =>
            {
                _exporting = false;
                settings.LastGlbExportDirectory = Path.GetDirectoryName(path);
                settings.Save();
                ExportGlbTo(path, options);
            }),
            onCancelled: () => Post(() =>
            {
                _exporting = false;
                SetStatus("GLB export cancelled");
            }));
    }

    private void ExportGlbTo(string outputPath, GlbExportOptions options)
    {
        try
        {
            var result = GlbExporter.Export(_doc, outputPath, options);
            string scaleNote = MathF.Abs(options.Scale - 1f) < 1e-6f ? "" : $", scale x{FormatScale(options.Scale)}";
            SetStatus(result.Warnings.Count == 0
                ? $"Exported GLB: {outputPath}  ({result.MeshCount} meshes, {result.TriangleCount} tris{scaleNote})"
                : $"Exported GLB with {result.Warnings.Count} warning(s) (see log): {outputPath}");
        }
        catch (Exception ex)
        {
            Log.Error($"GLB export failed: {ex}");
            SetStatus($"GLB export failed: {ex.Message}");
            CrashReporter.ShowMessage("Export GLB failed",
                $"The map could not be exported.\n\n{ex.GetType().Name}: {ex.Message}\n\nDetails: {Log.FilePath ?? "see log"}");
        }
    }
}
