using Godot;

namespace AlphaDefectAdvisor;

internal sealed class AdviceOverlay
{
    const string SettingsPath = "user://alphadefect-advisor-position.cfg";
    readonly CanvasLayer layer = new() { Layer = 110 };
    readonly PanelContainer panel = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
    readonly Label label = new() {
        MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(380, 0),
        AutowrapMode = TextServer.AutowrapMode.WordSmart
    };
    readonly Label handle = new() { MouseFilter = Control.MouseFilterEnum.Stop,
        MouseDefaultCursorShape = Control.CursorShape.Move };
    internal void RefreshHeader() => handle.Text = "⠿  " + AdvisorText.T("move");

    readonly ConfigFile settings = new();
    Vector2? preferred;
    Vector2 dragOffset;
    bool dragging;

    public AdviceOverlay(SceneTree tree)
    {
        var style = new StyleBoxFlat { BgColor = new Color(0.07f, 0.11f, 0.13f, 0.94f),
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 8, ContentMarginBottom = 10 };
        panel.AddThemeStyleboxOverride("panel", style);
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        RefreshHeader();
        var font = new SystemFont { FontNames = ["Yu Gothic UI", "Hiragino Sans", "Meiryo", "Arial"] };
        handle.AddThemeFontOverride("font", font);
        handle.AddThemeFontSizeOverride("font_size", 15);
        handle.GuiInput += input => {
            if (input is InputEventMouseButton button && button.ButtonIndex == MouseButton.Left) {
                dragging = button.Pressed;
                if (dragging) dragOffset = panel.GetGlobalMousePosition() - panel.Position;
                else {
                    preferred = panel.Position;
                    settings.SetValue("panel", "position", panel.Position);
                    settings.Save(SettingsPath);
                }
                handle.AcceptEvent();
            } else if (dragging && input is InputEventMouseMotion) {
                preferred = panel.GetGlobalMousePosition() - dragOffset;
                Place();
                handle.AcceptEvent();
            }
        };
        label.AddThemeFontSizeOverride("font_size", 18);
        label.AddThemeFontOverride("font", font);
        column.AddChild(handle);
        column.AddChild(label);
        panel.AddChild(column);
        layer.AddChild(panel);
        tree.Root.AddChild(layer);
        if (settings.Load(SettingsPath) == Error.Ok && settings.HasSectionKey("panel", "position"))
            preferred = settings.GetValue("panel", "position").AsVector2();
        tree.Root.SizeChanged += Place;
        panel.Resized += Place;
        panel.TreeExiting += () => tree.Root.SizeChanged -= Place;
        Place();
    }

    void Place()
    {
        var viewport = panel.GetViewportRect().Size;
        label.CustomMinimumSize = new Vector2(Math.Clamp(viewport.X - 32, 1, 460), 0);
        var available = (viewport - panel.Size).Max(Vector2.Zero);
        var desired = preferred ?? new Vector2(available.X - 16, available.Y / 2);
        panel.Position = desired.Clamp(Vector2.Zero, available);
    }

    public void Show(string message)
    {
        layer.Visible = true;
        if (label.Text == message) return;
        label.Text = message;
        panel.ResetSize();
        Place();
    }

    public void Hide() { dragging = false; layer.Visible = false; }
}
