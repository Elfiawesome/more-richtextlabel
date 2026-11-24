using Godot;
using MoreRichTextLabelLib;

public partial class AlignmentTest : Control
{
    private Node _cellParent = null!;
    private const string Doc =
        "[panel=res://stylebox/black_red.tres][img=64x64]res://nine_slice_small_3x3.svg[/img][/panel] [b]Align[/b]";

    public override async void _Ready()
    {
        var viewport = new SubViewport
        {
            Size = new Vector2I(660, 360),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(viewport);
        viewport.AddChild(new ColorRect
        {
            Color = new Color(0.1f, 0.1f, 0.12f),
            Size = new Vector2(660, 360),
        });

        _cellParent = viewport;
        var gdScript = GD.Load<GDScript>("res://gdscript/more_rich_text_label_gd.gd");

        for (int alignment = 0; alignment < 3; alignment++)
        {
            float y = 10 + alignment * 115;

            AddCell(new MoreRichTextLabel(), 10, y, alignment, RowAlignmentToString(alignment));
            AddCell((Control)gdScript.New(), 230, y, alignment, RowAlignmentToString(alignment));
            AddCell(new TextServerRichTextLabel(), 450, y, alignment, RowAlignmentToString(alignment));
        }

        for (int i = 0; i < 5; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        Image image = viewport.GetTexture().GetImage();
        string path = ProjectSettings.GlobalizePath("res://bench/results/alignment_test.png");
        image.SavePng(path);
        GD.Print($"alignment_test: {image.GetWidth()}x{image.GetHeight()}");
        GetTree().Quit();
    }

    private void AddCell(Control control, float x, float y, int alignment, string labelText)
    {
        var panel = new ColorRect
        {
            Color = new Color(0.16f, 0.16f, 0.19f),
            Position = new Vector2(x, y),
            Size = new Vector2(200, 95),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _cellParent.AddChild(panel);

        var label = new Label
        {
            Text = labelText,
            Position = new Vector2(x + 6, y + 4),
            Size = new Vector2(100, 20),
            Modulate = new Color(1, 1, 1, 0.6f),
        };
        _cellParent.AddChild(label);

        control.Position = new Vector2(x + 6, y + 28);
        control.Size = new Vector2(188, 60);
        _cellParent.AddChild(control);

        if (control is MoreRichTextLabel csharp)
        {
            csharp.RowAlignment = (InlineRowAlignment)alignment;
            csharp.SetTextForced(Doc);
            csharp.ForceRelayoutAtWidth(188);
        }
        else if (control is TextServerRichTextLabel textServer)
        {
            textServer.RowAlignment = (InlineRowAlignment)alignment;
            textServer.SetTextForced(Doc);
            textServer.ForceRelayoutAtWidth(188);
        }
        else
        {
            control.Set("row_alignment", alignment);
            control.Call("set_markup", Doc);
            control.Call("force_relayout_at_width", 188.0f);
        }
    }

    private static string RowAlignmentToString(int alignment)
    {
        return alignment switch
        {
            1 => "Middle",
            2 => "Bottom",
            _ => "Top",
        };
    }
}
