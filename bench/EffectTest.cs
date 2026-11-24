using Godot;
using MoreRichTextLabelLib;

public partial class EffectTest : Control
{
    private const string Doc =
        "[wave]Built-in wave[/wave] and [demo_pulse]custom DemoPulseEffect[/demo_pulse]. " +
        "[panel=res://stylebox/black_red.tres][margin=10]Effects inside a panel: " +
        "[demo_pulse]colored custom effect text[/demo_pulse][/margin][/panel]";

    public override async void _Ready()
    {
        var effect = GD.Load<RichTextEffect>("res://demo/effects/DemoPulseEffect.tres");
        if (effect == null)
        {
            GD.PushError("EffectTest: failed to load DemoPulseEffect.tres");
            GetTree().Quit();
            return;
        }

        var effects = new Godot.Collections.Array<RichTextEffect> { effect };

        var control = new MoreRichTextLabel
        {
            Position = new Vector2(10, 10),
            Size = new Vector2(360, 200),
            CustomEffects = effects,
        };
        var viewport = new SubViewport
        {
            Size = new Vector2I(400, 240),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(viewport);

        var background = new ColorRect
        {
            Color = new Color(0.1f, 0.1f, 0.12f),
            Size = new Vector2(400, 240),
        };
        viewport.AddChild(background);
        viewport.AddChild(control);
        control.SetTextForced(Doc);
        control.ForceRelayoutAtWidth(360);

        for (int i = 0; i < 4; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        Save(viewport, "effect_a");
        await ToSignal(GetTree().CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);
        Save(viewport, "effect_b");

        GD.Print($"EffectTest: effects={control.CustomEffects.Count}, blocks={control.TextBlockCount}");

        var gdScript = GD.Load<GDScript>("res://addons/more_rich_text_label/MoreRichTextLabelGD.gd");
        var gd = (Control)gdScript.New();
        var gdViewport = new SubViewport
        {
            Size = new Vector2I(400, 240),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(gdViewport);
        gdViewport.AddChild(new ColorRect
        {
            Color = new Color(0.1f, 0.1f, 0.12f),
            Size = new Vector2(400, 240),
        });
        gd.Position = new Vector2(10, 10);
        gd.Size = new Vector2(360, 200);
        gdViewport.AddChild(gd);
        gd.Set("custom_effects", effects);
        gd.Call("set_markup", Doc);
        gd.Call("force_relayout_at_width", 360.0f);

        for (int i = 0; i < 4; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        Save(gdViewport, "effect_gd_a");
        await ToSignal(GetTree().CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);
        Save(gdViewport, "effect_gd_b");

        int gdLeaves = 0;
        foreach (Node child in gd.GetChildren(true))
        {
            if (child is RichTextLabel)
            {
                gdLeaves++;
            }
        }
        GD.Print($"GD EffectTest: effects={gd.Get("custom_effects").AsGodotArray().Count}, leaves={gdLeaves}");

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
