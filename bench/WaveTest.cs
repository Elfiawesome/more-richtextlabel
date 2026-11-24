using Godot;
using MoreRichTextLabelLib;
using System.IO;

public partial class WaveTest : Control
{
    private const string Doc = "[wave]Hey[/wave] [b]Wave[/b]";

    public override async void _Ready()
    {
        var csView = await MakeViewport("wave_csharp", new MoreRichTextLabel(), true);
        var tsView = await MakeViewport("wave_textserver", new TextServerRichTextLabel(), false);
        Control csControl = csView.GetChild<Control>(1);

        foreach (Node child in csControl.GetChildren(true))
        {
            if (child is RichTextLabel rtl)
            {
                GD.Print($"leaf text={rtl.Text} parsed={rtl.GetParsedText()} chars={rtl.GetTotalCharacterCount()} processing={rtl.IsProcessing()} internal={rtl.IsProcessingInternal()} visible={rtl.IsVisibleInTree()}");
            }
        }

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Save(csView, "wave_csharp_a");
        Save(tsView, "wave_textserver_a");

        await ToSignal(GetTree().CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);

        foreach (Node child in csControl.GetChildren(true))
        {
            if (child is RichTextLabel rtl2)
            {
                GD.Print($"after: internal={rtl2.IsProcessingInternal()} visible={rtl2.IsVisibleInTree()}");
            }
        }

        Save(csView, "wave_csharp_b");
        Save(tsView, "wave_textserver_b");

        GetTree().Quit();
    }

    private async System.Threading.Tasks.Task<SubViewport> MakeViewport(string name, Control impl, bool setEffects)
    {
        var viewport = new SubViewport
        {
            Name = name,
            Size = new Vector2I(300, 100),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(viewport);

        var background = new ColorRect
        {
            Color = new Color(0.1f, 0.1f, 0.12f),
            Size = new Vector2(300, 100),
        };
        viewport.AddChild(background);

        impl.Position = new Vector2(10, 10);
        impl.Size = new Vector2(280, 80);
        viewport.AddChild(impl);

        if (setEffects && impl is MoreRichTextLabel csharp)
        {
            csharp.SetTextForced(Doc);
            csharp.ForceRelayoutAtWidth(280);
        }
        else if (impl is TextServerRichTextLabel ts)
        {
            ts.SetTextForced(Doc);
            ts.ForceRelayoutAtWidth(280);
        }

        for (int i = 0; i < 4; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        return viewport;
    }

    private static void Save(SubViewport viewport, string name)
    {
        Image image = viewport.GetTexture().GetImage();
        string path = ProjectSettings.GlobalizePath($"res://bench/results/{name}.png");
        image.SavePng(path);
        GD.Print($"{name}: {image.GetWidth()}x{image.GetHeight()}");
    }
}
