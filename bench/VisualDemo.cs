using Godot;
using MoreRichTextLabelLib;
using System.IO;

public partial class VisualDemo : Control
{
    private const string Doc =
        "[panel=res://stylebox/black_red.tres]Short[/panel] same-line text after a hugging panel. " +
        "[b]Before the long panel[/b]: text is inline too. " +
        "[panel=res://stylebox/black_red.tres][margin=15][img=64x64]res://nine_slice_small_3x3.svg[/img] " +
        "Keyword with [b]Icon[/b] that is fit within the margins and panel. This sentence is long enough to wrap inside the panel and prove that the panel height grows with its content." +
        "[/margin][/panel] And the description afterwards is a separate text block. " +
        "[panel=res://stylebox/pink_white_cornered.tres]This panel afterwards is self contained and its height is not affected by previous panels or margins.[/panel] " +
        "[panel=res://stylebox/nine_slice_small.tres][panel=res://stylebox/black_red.tres][margin=8]Nested panels and margins can stack. [i]Italic text[/i] and [b]bold text[/b] still work inside them.[/margin][/panel][/panel] " +
        "[panel=res://stylebox/nine_slice_detailed.tres]The detailed 9-slice panel has 100px content margins. This text is fitted inside those margins and wraps when the width is reduced.[/panel] " +
        "Final text after all panels.";

    public override async void _Ready()
    {
        DirAccess.MakeDirRecursiveAbsolute("res://bench/results");

        var cs = await CreateViewport("csharp_leaf", new MoreRichTextLabel());
        var gd = await CreateViewport("gdscript_leaf", CreateGdScript());
        var ts = await CreateViewport("csharp_textserver", new TextServerRichTextLabel());

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        Save(cs, "csharp_leaf");
        Save(gd, "gdscript_leaf");
        Save(ts, "csharp_textserver");

        GD.Print("VisualDemo wrote screenshots.");
        GetTree().Quit();
    }

    private static Control CreateGdScript()
    {
        var script = GD.Load<GDScript>("res://gdscript/more_rich_text_label_gd.gd");
        return (Control)script.New();
    }

    private async System.Threading.Tasks.Task<SubViewport> CreateViewport(string name, Control impl)
    {
        var viewport = new SubViewport
        {
            Name = name,
            Size = new Vector2I(560, 1400),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = false,
        };

        AddChild(viewport);

        var background = new ColorRect
        {
            Color = new Color(0.12f, 0.12f, 0.14f),
            Position = Vector2.Zero,
            Size = new Vector2(560, 1400),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        viewport.AddChild(background);

        impl.Position = Vector2.Zero;
        impl.Size = new Vector2(560, 1400);
        viewport.AddChild(impl);

        if (impl is MoreRichTextLabel csharp)
        {
            csharp.Text = Doc;
            csharp.ForceRelayout();
        }
        else if (impl is TextServerRichTextLabel textServer)
        {
            textServer.Text = Doc;
            textServer.ForceRelayout();
        }
        else
        {
            impl.Call("set_markup", Doc);
            impl.Call("force_relayout");
        }

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        return viewport;
    }

    private static void Save(SubViewport viewport, string name)
    {
        Image image = viewport.GetTexture().GetImage();
        string path = ProjectSettings.GlobalizePath($"res://bench/results/{name}.png");
        Error error = image.SavePng(path);
        GD.Print($"{name}: {image.GetWidth()}x{image.GetHeight()} save={error} {path}");
    }
}
