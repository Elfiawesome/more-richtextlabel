using Godot;
using MoreRichTextLabelLib;

public partial class CaptureScreenshots : Control
{
    private Godot.Collections.Array<RichTextEffect> _effects = new();

    public override async void _Ready()
    {
        DirAccess.MakeDirRecursiveAbsolute("res://docs/images");

        var effect = GD.Load<RichTextEffect>("res://demo/effects/DemoPulseEffect.tres");
        if (effect != null)
        {
            _effects.Add(effect);
        }

        await CaptureDemo();
        await CaptureDoc("inline-panels", new Vector2I(900, 130), Demo.InlineDoc,
            GD.Load<StyleBox>("res://stylebox/pink_white_cornered.tres"), null);
        await CaptureDoc("nine-slice-panels", new Vector2I(900, 300), Demo.NineSliceDoc, null, null);
        await CaptureDoc("panel-modulation", new Vector2I(900, 110), Demo.ModulateDoc, GD.Load<StyleBox>("res://stylebox/pink_white_cornered.tres"), null);
        await CaptureDoc("effects", new Vector2I(900, 100), Demo.EffectsDoc, null, _effects);
        await CaptureDoc("word-wrapping", new Vector2I(720, 100), "[panel=res://stylebox/black_red.tres]Tag[/panel] This plain text continues on the same line and then wraps word by word when it no longer fits the available width.", null, null);
        await CaptureAlignment();

        GD.Print("CaptureScreenshots: done");
        GetTree().Quit();
    }

    private async System.Threading.Tasks.Task CaptureDemo()
    {
        var viewport = CreateViewport(new Vector2I(1100, 1310));
        var demo = GD.Load<PackedScene>("res://demo/Demo.tscn").Instantiate<Control>();
        viewport.AddChild(demo);
        await WaitFrames(10);
        Save(viewport, "demo");
        viewport.QueueFree();
        await WaitFrames(1);
    }

    private async System.Threading.Tasks.Task CaptureDoc(
        string name,
        Vector2I size,
        string doc,
        StyleBox? defaultPanelStyle,
        Godot.Collections.Array<RichTextEffect>? effects)
    {
        var viewport = CreateViewport(size);

        var control = new MoreRichTextLabel
        {
            Position = new Vector2(20, 20),
            Size = new Vector2(size.X - 40, size.Y - 40),
            Text = doc,
            DefaultPanelStyle = defaultPanelStyle,
        };

        if (effects != null)
        {
            control.CustomEffects = effects;
        }

        viewport.AddChild(control);
        await WaitFrames(8);
        Save(viewport, name);
        viewport.QueueFree();
        await WaitFrames(1);
    }

    private async System.Threading.Tasks.Task CaptureAlignment()
    {
        var viewport = CreateViewport(new Vector2I(900, 180));
        var row = new HBoxContainer
        {
            Position = new Vector2(20, 20),
            Size = new Vector2(860, 170),
        };
        row.AddThemeConstantOverride("separation", 16);
        viewport.AddChild(row);

        row.AddChild(CreateAlignmentCell("Top", InlineRowAlignment.Top));
        row.AddChild(CreateAlignmentCell("Middle", InlineRowAlignment.Middle));
        row.AddChild(CreateAlignmentCell("Bottom", InlineRowAlignment.Bottom));

        await WaitFrames(8);
        Save(viewport, "row-alignment");
        viewport.QueueFree();
        await WaitFrames(1);
    }

    private VBoxContainer CreateAlignmentCell(string name, InlineRowAlignment alignment)
    {
        var cell = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        cell.AddThemeConstantOverride("separation", 6);

        var label = new Label { Text = name };
        label.AddThemeFontSizeOverride("font_size", 13);
        label.AddThemeColorOverride("font_color", new Color("9aa4b8"));
        cell.AddChild(label);

        var body = new MoreRichTextLabel
        {
            Text = Demo.AlignmentDoc,
            RowAlignment = alignment,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 110),
        };
        cell.AddChild(body);
        return cell;
    }

    private SubViewport CreateViewport(Vector2I size)
    {
        var viewport = new SubViewport
        {
            Size = size,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(viewport);

        var background = new ColorRect
        {
            Color = new Color("12141b"),
            Size = new Vector2(size.X, size.Y),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        viewport.AddChild(background);
        return viewport;
    }

    private async System.Threading.Tasks.Task WaitFrames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private void Save(SubViewport viewport, string name)
    {
        Image image = viewport.GetTexture().GetImage();
        string path = ProjectSettings.GlobalizePath($"res://docs/images/{name}.png");
        image.SavePng(path);
        GD.Print($"{name}: {image.GetWidth()}x{image.GetHeight()}");
    }
}
