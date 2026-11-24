using Godot;
using MoreRichTextLabelLib;
using System.Text;

public partial class SmokeTest : Control
{
    private const string Doc =
        "[b]Smoke test[/b] before the panel. " +
        "[panel=res://stylebox/black_red.tres][margin=10][img=64x64]res://nine_slice_small_3x3.svg[/img] " +
        "Keyword with [b]Icon[/b] that is fit within the margins and panel. Long text to force wrapping. " +
        "[/margin][/panel] And the description afterwards. " +
        "[panel=res://stylebox/pink_white_cornered.tres]This panel afterwards is self contained and its height is not affected by previous panels.[/panel]";

    public override async void _Ready()
    {
        var cs = new MoreRichTextLabel
        {
            Position = Vector2.Zero,
            Size = new Vector2(500, 1000),
            Text = Doc,
        };
        AddChild(cs);

        var gdScript = GD.Load<GDScript>("res://gdscript/more_rich_text_label_gd.gd");
        var gd = (Control)gdScript.New();
        gd.Position = new Vector2(0, 0);
        gd.Size = new Vector2(500, 1000);
        AddChild(gd);
        gd.Call("set_markup", Doc);
        gd.Call("force_relayout");

        var ts = new TextServerRichTextLabel
        {
            Position = Vector2.Zero,
            Size = new Vector2(500, 1000),
            Text = Doc,
        };
        AddChild(ts);

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        cs.ForceRelayout();
        gd.Call("force_relayout");
        ts.ForceRelayout();

        GD.Print("C# nodes=", CountNodes(cs), " min=", cs.GetCombinedMinimumSize(), " content=", cs.ContentHeight);
        GD.Print("GD nodes=", CountNodes(gd), " min=", gd.GetCombinedMinimumSize());
        GD.Print("TS nodes=", CountNodes(ts), " min=", ts.GetCombinedMinimumSize(), " content=", ts.ContentHeight);

        var builder = new StringBuilder();
        builder.AppendLine("C# leaf children:");
        PrintChildren(cs, builder);
        GD.Print(builder.ToString());

        GetTree().Quit();
    }

    private static int CountNodes(Node node)
    {
        int count = 1;
        foreach (Node child in node.GetChildren(true))
        {
            count += CountNodes(child);
        }
        return count;
    }

    private static void PrintChildren(Node node, StringBuilder builder, int depth = 0)
    {
        foreach (Node child in node.GetChildren(true))
        {
            builder.Append(' ', depth * 2).Append(child.GetClass()).Append(" size=").Append((child as Control)?.Size).AppendLine();
            PrintChildren(child, builder, depth + 1);
        }
    }
}
