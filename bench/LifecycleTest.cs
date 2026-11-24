using Godot;
using MoreRichTextLabelLib;

public partial class LifecycleTest : Control
{
    private const string Doc =
        "[panel=res://stylebox/black_red.tres]Short[/panel] This text should wrap word by word when the width is reduced. " +
        "Another sentence with enough words to make the layout wider than the narrow test width.";

    public override async void _Ready()
    {
        var host = new SubViewport
        {
            Size = new Vector2I(500, 400),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        };
        AddChild(host);

        var cs = new MoreRichTextLabel
        {
            Position = Vector2.Zero,
            Size = new Vector2(480, 400),
        };
        host.AddChild(cs);
        cs.SetTextForced(Doc);
        cs.ForceRelayoutAtWidth(480);

        var gdScript = GD.Load<GDScript>("res://addons/more_rich_text_label/MoreRichTextLabelGD.gd");
        var gd = (Control)gdScript.New();
        gd.Position = Vector2.Zero;
        gd.Size = new Vector2(480, 400);
        host.AddChild(gd);
        gd.Call("set_markup", Doc);
        gd.Call("force_relayout_at_width", 480.0f);

        await WaitFrames(3);
        int csLeavesBefore = CountLeaves(cs);
        int gdLeavesBefore = CountLeaves(gd);
        float csHeightBefore = cs.ContentHeight;
        float gdHeightBefore = gd.GetCombinedMinimumSize().Y;

        host.RemoveChild(cs);
        host.RemoveChild(gd);
        await WaitFrames(2);

        cs.Size = new Vector2(280, 400);
        gd.Size = new Vector2(280, 400);
        host.AddChild(cs);
        host.AddChild(gd);
        await WaitFrames(5);

        cs.ForceRelayoutAtWidth(280);
        gd.Call("force_relayout_at_width", 280.0f);
        await WaitFrames(3);

        int csLeavesAfter = CountLeaves(cs);
        int gdLeavesAfter = CountLeaves(gd);
        float csHeightAfter = cs.ContentHeight;
        float gdHeightAfter = gd.GetCombinedMinimumSize().Y;

        GD.Print($"C# leaves {csLeavesBefore} -> {csLeavesAfter}, height {csHeightBefore} -> {csHeightAfter}, blocks={cs.TextBlockCount}");
        GD.Print($"GD leaves {gdLeavesBefore} -> {gdLeavesAfter}, height {gdHeightBefore} -> {gdHeightAfter}");
        GD.Print(csLeavesAfter == csLeavesBefore && gdLeavesAfter == gdLeavesBefore && csHeightAfter > csHeightBefore && gdHeightAfter > gdHeightBefore
            ? "LifecycleTest: PASS"
            : "LifecycleTest: FAIL");

        host.RemoveChild(cs);
        host.RemoveChild(gd);
        cs.Free();
        gd.Free();
        host.QueueFree();
        GetTree().Quit();
    }

    private async System.Threading.Tasks.Task WaitFrames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private static int CountLeaves(Node node)
    {
        int count = 0;
        foreach (Node child in node.GetChildren(true))
        {
            if (child is RichTextLabel)
            {
                count++;
            }
        }
        return count;
    }
}
