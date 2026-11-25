using Godot;

namespace AlphaDefectAdvisor;

internal sealed class AdviceOverlay
{
    readonly CanvasLayer layer = new() { Layer = 110 };
    readonly Label label = new() {
        MouseFilter = Control.MouseFilterEnum.Ignore,
        CustomMinimumSize = new Vector2(380, 0),
        AutowrapMode = TextServer.AutowrapMode.WordSmart
    };

    public AdviceOverlay(SceneTree tree)
    {
        var panel = new PanelContainer { Position = new Vector2(16, 84), MouseFilter = Control.MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat { BgColor = new Color(0.07f, 0.11f, 0.13f, 0.94f),
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 10, ContentMarginBottom = 10 };
        panel.AddThemeStyleboxOverride("panel", style);
        label.AddThemeFontSizeOverride("font_size", 18);
        label.AddThemeFontOverride("font", new SystemFont { FontNames = ["Yu Gothic UI", "Meiryo"] });
        panel.AddChild(label);
        layer.AddChild(panel);
        tree.Root.AddChild(layer);
    }

    public void Show(string message)
    {
        layer.Visible = true;
        label.Text = message;
        ((Control)label.GetParent()).ResetSize();
    }

    public void Hide() => layer.Visible = false;
}
