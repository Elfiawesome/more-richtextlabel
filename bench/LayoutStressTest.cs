using Godot;
using MoreRichTextLabelLib;
using MoreRichTextLabelLib.Experimental;

public partial class LayoutStressTest : Control
{
    private const string Icon = "[img=32x32]res://icon.svg[/img]";
    private const string Font = "[font=res://bench/assets/Rajdhani-Regular.ttf]";

    public override async void _Ready()
    {
        DirAccess.MakeDirRecursiveAbsolute("res://bench/results");

        string case1 =
            "[panel modulate=red][margin=8]" + Icon + Font + "Sweep ATK[/font][/margin][/panel]" +
            "Deal a various of 20 ATK!" +
            "[panel modulate=red][margin=8]" + Icon + Font + "Sweep ATK[/font][/margin][/panel]" +
            "Deal a various of 20 ATK!";

        string case2 =
            Font + "[panel modulate=red][margin=8]" + Icon + Font + "Sweep ATK[/font][/margin][/panel]" +
            "Deal a various of 20 ATK![/font]\ntext";

        foreach (int width in new[] { 620, 500, 400, 360, 340, 300, 320 })
        {
            await Capture("case1", width, case1);
            await Capture("case2", width, case2);
        }

        await CaptureGd("case1-gd", 300, case1);
        await CaptureGd("case2-gd", 620, case2);
        await CaptureGd("case2-gd", 320, case2);
        await CaptureTs("case1-ts", 300, case1);
        await CaptureTs("case2-ts", 620, case2);
        await CaptureTs("case2-ts", 320, case2);

        GetTree().Quit();
    }

    private async System.Threading.Tasks.Task Capture(string name, int width, string doc)
    {
        var viewport = new SubViewport
        {
            Size = new Vector2I(width + 20, 360),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(viewport);

        viewport.AddChild(new ColorRect
        {
            Color = new Color(0.1f, 0.1f, 0.12f),
            Size = new Vector2(width + 20, 360),
        });

        var control = new MoreRichTextLabel
        {
            Position = new Vector2(10, 10),
            Size = new Vector2(width, 340),
            DefaultPanelStyle = GD.Load<StyleBox>("res://stylebox/pink_white_cornered.tres"),
        };
        viewport.AddChild(control);
        control.SetTextForced(doc);
        control.ForceRelayoutAtWidth(width);

        await WaitFrames(3);
        Save(viewport, $"{name}-{width}");
        viewport.QueueFree();
        await WaitFrames(1);
    }

    private async System.Threading.Tasks.Task CaptureGd(string name, int width, string doc)
    {
        var viewport = CreateViewport(width);
        var script = GD.Load<GDScript>("res://addons/more_rich_text_label/MoreRichTextLabelGD.gd");
        var control = (Control)script.New();
        control.Position = new Vector2(10, 10);
        control.Size = new Vector2(width, 340);
        viewport.AddChild(control);
        control.Set("default_panel_style", GD.Load<StyleBox>("res://stylebox/pink_white_cornered.tres"));
        control.Call("set_markup", doc);
        await WaitFrames(2);
        control.Call("force_relayout_at_width", (float)width);
        await WaitFrames(2);
        Save(viewport, $"{name}-{width}");
        viewport.QueueFree();
        await WaitFrames(1);
    }

    private async System.Threading.Tasks.Task CaptureTs(string name, int width, string doc)
    {
        var viewport = CreateViewport(width);
        var control = new TextServerRichTextLabel
        {
            Position = new Vector2(10, 10),
            Size = new Vector2(width, 340),
            DefaultPanelStyle = GD.Load<StyleBox>("res://stylebox/pink_white_cornered.tres"),
        };
        viewport.AddChild(control);
        control.SetTextForced(doc);
        control.ForceRelayoutAtWidth(width);
        await WaitFrames(2);
        Save(viewport, $"{name}-{width}");
        viewport.QueueFree();
        await WaitFrames(1);
    }

    private SubViewport CreateViewport(int width)
    {
        var viewport = new SubViewport
        {
            Size = new Vector2I(width + 20, 360),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(viewport);
        viewport.AddChild(new ColorRect
        {
            Color = new Color(0.1f, 0.1f, 0.12f),
            Size = new Vector2(width + 20, 360),
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
        string path = ProjectSettings.GlobalizePath($"res://bench/results/{name}.png");
        image.SavePng(path);
        GD.Print($"{name}: {image.GetWidth()}x{image.GetHeight()}");
    }
}
