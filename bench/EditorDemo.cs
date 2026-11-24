using Godot;
using MoreRichTextLabelLib;

[Tool]
public partial class EditorDemo : Control
{
    private const string Doc =
        "[b]Editor preview[/b]\n\n" +
        "[panel=res://stylebox/black_red.tres]Short[/panel] trailing text on the same line.\n" +
        "[panel=res://stylebox/black_red.tres][margin=12]" +
        "[img=64x64]res://nine_slice_small_3x3.svg[/img] " +
        "[wave]Built-in wave[/wave] and [demo_pulse]custom DemoPulseEffect[/demo_pulse]. " +
        "This text is inside a panel with a margin.[/margin][/panel]\n" +
        "[panel=res://stylebox/pink_white_cornered.tres]This panel is self-contained.[/panel]\n" +
        "[panel=res://stylebox/nine_slice_detailed.tres]The detailed 9-slice panel uses its 100px content margins.[/panel]\n" +
        "Final text after all panels.";

    public override void _Ready()
    {
        var label = GetNodeOrNull<MoreRichTextLabel>("MoreRichTextLabel");
        if (label == null)
        {
            return;
        }

        var effect = GD.Load<RichTextEffect>("res://bench/effects/demo_pulse.tres");
        if (effect != null)
        {
            label.CustomEffects = new Godot.Collections.Array<RichTextEffect> { effect };
        }

        label.Text = Doc;
        label.ForceRelayout();

        if (Engine.IsEditorHint())
        {
            int leaves = 0;
            bool processing = false;
            foreach (Node child in label.GetChildren(true))
            {
                if (child is RichTextLabel leaf)
                {
                    leaves++;
                    processing |= leaf.IsProcessingInternal();
                }
            }

            bool hasEffectsProperty = false;
            foreach (Godot.Collections.Dictionary property in label.GetPropertyList())
            {
                if (property["name"].AsString() == "CustomEffects")
                {
                    hasEffectsProperty = true;
                    break;
                }
            }

            GD.Print($"EditorDemo tool preview: blocks={label.TextBlockCount}, effects={label.CustomEffects.Count}, leaves={leaves}, processing={processing}, exported={hasEffectsProperty}");
        }
    }
}
