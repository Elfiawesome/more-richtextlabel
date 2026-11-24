using Godot;
using MoreRichTextLabelLib;
using MoreRichTextLabelLib.Experimental;

public partial class InlineTest : Control
{
    private const string Doc =
        "Before [panel=res://stylebox/black_red.tres]Panel content[/panel] after the panel. " +
        "[panel=res://stylebox/pink_white_cornered.tres][img=48x48]res://nine_slice_small_3x3.svg[/img] Icon text[/panel] " +
        "This trailing text should continue on the same line when it fits, otherwise it moves below. " +
        "[panel=res://stylebox/nine_slice_small.tres][margin=8]Nested [panel=res://stylebox/black_red.tres]small[/panel] panel[/margin][/panel] " +
        "End. [panel]Hey[/panel] Hey";

    public override async void _Ready()
    {
        var viewport = new SubViewport
        {
            Size = new Vector2I(600, 500),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(viewport);

        viewport.AddChild(new ColorRect
        {
            Color = new Color(0.1f, 0.1f, 0.12f),
            Size = new Vector2(600, 500),
        });

        var control = new MoreRichTextLabel
        {
            Position = new Vector2(10, 10),
            Size = new Vector2(580, 480),
        };
        viewport.AddChild(control);
        control.SetTextForced(Doc);
        control.ForceRelayoutAtWidth(580);

        for (int i = 0; i < 5; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        Save(viewport, "inline_test");
        GD.Print($"InlineTest: blocks={control.TextBlockCount}, height={control.ContentHeight}");

        const string WrapDoc = "[margin=10][b][wave amp=300]Hello[/wave][/b] world[/margin]Hello World This text is noting";
        var wrapViewport = new SubViewport
        {
            Size = new Vector2I(560, 220),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(wrapViewport);
        wrapViewport.AddChild(new ColorRect
        {
            Color = new Color(0.1f, 0.1f, 0.12f),
            Size = new Vector2(560, 220),
        });
        var wrapControl = new MoreRichTextLabel
        {
            Position = new Vector2(10, 10),
            Size = new Vector2(540, 200),
        };
        wrapViewport.AddChild(wrapControl);
        wrapControl.SetTextForced(WrapDoc);
        wrapControl.ForceRelayoutAtWidth(540);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Save(wrapViewport, "wrap_wide");
        wrapControl.ForceRelayoutAtWidth(300);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Save(wrapViewport, "wrap_narrow");

        var gdViewport = new SubViewport
        {
            Size = new Vector2I(560, 220),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(gdViewport);
        gdViewport.AddChild(new ColorRect
        {
            Color = new Color(0.1f, 0.1f, 0.12f),
            Size = new Vector2(560, 220),
        });
        var gdScript = GD.Load<GDScript>("res://addons/more_rich_text_label/MoreRichTextLabelGD.gd");
        var gd = (Control)gdScript.New();
        gd.Position = new Vector2(10, 10);
        gd.Size = new Vector2(300, 200);
        gdViewport.AddChild(gd);
        gd.Call("set_markup", WrapDoc);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        gd.Call("force_relayout_at_width", 300.0f);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Save(gdViewport, "wrap_gd_narrow");

        var tsViewport = new SubViewport
        {
            Size = new Vector2I(560, 220),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(tsViewport);
        tsViewport.AddChild(new ColorRect
        {
            Color = new Color(0.1f, 0.1f, 0.12f),
            Size = new Vector2(560, 220),
        });
        var ts = new TextServerRichTextLabel
        {
            Position = new Vector2(10, 10),
            Size = new Vector2(300, 200),
        };
        tsViewport.AddChild(ts);
        ts.SetTextForced(WrapDoc);
        ts.ForceRelayoutAtWidth(300);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Save(tsViewport, "wrap_ts_narrow");

        GetTree().Quit();
    }

    private static void Save(SubViewport viewport, string name)
    {
        Image image = viewport.GetTexture().GetImage();
        string path = ProjectSettings.GlobalizePath($"res://bench/results/{name}.png");
        image.SavePng(path);
        GD.Print($"{name}: {image.GetWidth()}x{image.GetHeight()}");
    }
}
