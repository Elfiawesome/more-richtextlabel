using Godot;
using MoreRichTextLabelLib;
using MoreRichTextLabelLib.Experimental;
using System.Collections.Generic;

public partial class LayoutMatrixTest : Control
{
    private const string Font = "[font=res://bench/assets/Rajdhani-Regular.ttf]";
    private const string Icon = "[img=32x32]res://icon.svg[/img]";
    private const string Long = "This is a long sentence used to test wrapping across multiple lines with inline panels and formatting. ";

    private readonly List<(string Name, string Doc)> _cases = new();

    public override async void _Ready()
    {
        DirAccess.MakeDirRecursiveAbsolute("res://bench/results");

        _cases.Add(("text-only", Long + Long));
        _cases.Add(("panel-text", "[panel]Panel[/panel] Deal a various of 20 ATK! " + Long));
        _cases.Add(("text-panel", "Intro text " + Long + "[panel]Panel[/panel]"));
        _cases.Add(("two-panels",
            "[panel modulate=red][margin=8]" + Icon + Font + "Sweep ATK[/font][/margin][/panel]" +
            "Deal a various of 20 ATK!" +
            "[panel modulate=red][margin=8]" + Icon + Font + "Sweep ATK[/font][/margin][/panel]" +
            "Deal a various of 20 ATK!"));
        _cases.Add(("adjacent-panels", "[panel]One[/panel][panel]Two[/panel] tail text"));
        _cases.Add(("margin-flow", "[margin=10]Inside margin text[/margin] after margin " + Long));
        _cases.Add(("nested", "[panel][margin=8]Nested [panel]Inner[/panel] margin[/margin][/panel] tail text"));
        _cases.Add(("styled-wrap", "[panel]Card[/panel] Deal [b]20[/b] [i]ATK[/i] damage to [color=#ffcc66]all enemies[/color] in range. " + Long));
        _cases.Add(("font-wrap", "[panel]Card[/panel] " + Font + "Deal a various of 20 ATK![/font] " + Long));
        _cases.Add(("font-size-wrap", "[panel]Card[/panel] [font_size=30]Deal a various of 20 ATK![/font_size] " + Long));
        _cases.Add(("newline-flow", "[panel]Card[/panel] Deal a various of 20 ATK!\nnext line " + Long));
        _cases.Add(("tagged-newline", Font + "[panel]Card[/panel] Deal a various of 20 ATK!\nnext line[/font] " + Long));
        _cases.Add(("effects", "[panel]Card[/panel] [wave]Wave[/wave] and [shake]Shake[/shake] plus trailing words that should wrap normally. " + Long));
        _cases.Add(("modulation", "[panel modulate=red]Red[/panel] [panel modulate=#66ccff]Blue[/panel] [panel modulate=#66ff99]Green[/panel] tail text " + Long));
        _cases.Add(("long-word", "[panel]Card[/panel] Supercalifragilisticexpialidocious_without_spaces_should_still_fit_and_wrap " + Long));
        _cases.Add(("images", "[panel]Card[/panel] Text " + Icon + " more text after the image that should wrap around " + Long));
        _cases.Add(("blank-lines", "First line\n\nSecond paragraph with words\nThird line " + Long));
        _cases.Add(("panel-content-wrap", "[panel]The content inside this panel is long enough to wrap within the panel and grow its height with the text. " + Long + "[/panel] tail text"));
        _cases.Add(("default-mod", "[panel modulate=#cc99ff]Default style panel[/panel] tail text " + Long));
        _cases.Add(("margin-panel", "[margin=8][panel]Card[/panel] text inside margin[/margin] tail text " + Long));

        foreach ((string name, string doc) in _cases)
        {
            await Capture("cs", name, 620, doc);
            await Capture("cs", name, 300, doc);
        }

        foreach (string name in new[] { "two-panels", "styled-wrap", "font-wrap", "tagged-newline", "effects", "images" })
        {
            string doc = _cases.Find(c => c.Name == name).Doc;
            await CaptureGd(name, 300, doc);
            await CaptureTs(name, 300, doc);
        }

        GetTree().Quit();
    }

    private async System.Threading.Tasks.Task Capture(string prefix, string name, int width, string doc)
    {
        var viewport = CreateViewport(width);
        var control = new MoreRichTextLabel
        {
            Position = new Vector2(10, 10),
            Size = new Vector2(width, 500),
            DefaultPanelStyle = GD.Load<StyleBox>("res://stylebox/pink_white_cornered.tres"),
        };
        viewport.AddChild(control);
        control.SetTextForced(doc);
        control.ForceRelayoutAtWidth(width);
        await WaitFrames(2);
        CheckNoStrayTags(control, $"{prefix}-{name}-{width}");
        Save(viewport, $"{prefix}-{name}-{width}");
        viewport.QueueFree();
        await WaitFrames(1);
    }

    private async System.Threading.Tasks.Task CaptureGd(string name, int width, string doc)
    {
        var viewport = CreateViewport(width);
        var script = GD.Load<GDScript>("res://addons/more_rich_text_label/MoreRichTextLabelGD.gd");
        var control = (Control)script.New();
        control.Position = new Vector2(10, 10);
        control.Size = new Vector2(width, 500);
        viewport.AddChild(control);
        control.Set("default_panel_style", GD.Load<StyleBox>("res://stylebox/pink_white_cornered.tres"));
        control.Call("set_markup", doc);
        await WaitFrames(2);
        control.Call("force_relayout_at_width", (float)width);
        await WaitFrames(2);
        CheckNoStrayTags(control, $"gd-{name}-{width}");
        Save(viewport, $"gd-{name}-{width}");
        viewport.QueueFree();
        await WaitFrames(1);
    }

    private async System.Threading.Tasks.Task CaptureTs(string name, int width, string doc)
    {
        var viewport = CreateViewport(width);
        var control = new TextServerRichTextLabel
        {
            Position = new Vector2(10, 10),
            Size = new Vector2(width, 500),
            DefaultPanelStyle = GD.Load<StyleBox>("res://stylebox/pink_white_cornered.tres"),
        };
        viewport.AddChild(control);
        control.SetTextForced(doc);
        control.ForceRelayoutAtWidth(width);
        await WaitFrames(2);
        Save(viewport, $"ts-{name}-{width}");
        viewport.QueueFree();
        await WaitFrames(1);
    }

    private void CheckNoStrayTags(Control control, string name)
    {
        foreach (Node child in control.GetChildren(true))
        {
            if (child is RichTextLabel label)
            {
                string parsed = label.GetParsedText();
                if (parsed.Contains('[') || parsed.Contains(']'))
                {
                    GD.PushError($"{name}: stray tag markup in parsed text: {parsed}");
                }
            }
        }
    }

    private SubViewport CreateViewport(int width)
    {
        var viewport = new SubViewport
        {
            Size = new Vector2I(width + 20, 520),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(viewport);
        viewport.AddChild(new ColorRect
        {
            Color = new Color(0.1f, 0.1f, 0.12f),
            Size = new Vector2(width + 20, 520),
        });
        return viewport;
    }

    private async System.Threading.Tasks.Task WaitFrames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private static void Save(SubViewport viewport, string name)
    {
        Image image = viewport.GetTexture().GetImage();
        string path = ProjectSettings.GlobalizePath($"res://bench/results/matrix-{name}.png");
        image.SavePng(path);
    }
}
