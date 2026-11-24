using Godot;
using MoreRichTextLabelLib;

public partial class ThemeTest : Control
{
    private const string Doc =
        "Theme font size test. This sentence is long enough to wrap at the narrow width when the theme font becomes larger.";

    public override async void _Ready()
    {
        var host = new SubViewport
        {
            Size = new Vector2I(400, 500),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        };
        AddChild(host);

        var cs = new MoreRichTextLabel
        {
            Position = Vector2.Zero,
            Size = new Vector2(300, 500),
        };
        host.AddChild(cs);
        cs.SetTextForced(Doc);
        cs.ForceRelayoutAtWidth(300);

        var gdScript = GD.Load<GDScript>("res://gdscript/more_rich_text_label_gd.gd");
        var gd = (Control)gdScript.New();
        gd.Position = Vector2.Zero;
        gd.Size = new Vector2(300, 500);
        host.AddChild(gd);
        gd.Call("set_markup", Doc);
        gd.Call("force_relayout_at_width", 300.0f);

        var ts = new TextServerRichTextLabel
        {
            Position = Vector2.Zero,
            Size = new Vector2(300, 500),
        };
        host.AddChild(ts);
        ts.SetTextForced(Doc);
        ts.ForceRelayoutAtWidth(300);

        await WaitFrames(3);
        float csSmall = HeightOf(cs);
        float gdSmall = HeightOf(gd);
        float tsSmall = HeightOf(ts);

        var theme = MakeTheme(32);
        cs.Theme = theme;
        gd.Theme = theme;
        ts.Theme = theme;
        await WaitFrames(5);

        float csLarge = HeightOf(cs);
        float gdLarge = HeightOf(gd);
        float tsLarge = HeightOf(ts);

        theme.SetFontSize("normal_font_size", "RichTextLabel", 14);
        theme.SetFontSize("bold_font_size", "RichTextLabel", 14);
        theme.SetFontSize("italics_font_size", "RichTextLabel", 14);
        theme.DefaultFontSize = 14;
        await WaitFrames(5);

        float csEdited = HeightOf(cs);
        float gdEdited = HeightOf(gd);
        float tsEdited = HeightOf(ts);

        GD.Print($"C# theme heights {csSmall} -> {csLarge} -> {csEdited}");
        GD.Print($"GD theme heights {gdSmall} -> {gdLarge} -> {gdEdited}");
        GD.Print($"TS theme heights {tsSmall} -> {tsLarge} -> {tsEdited}");

        bool pass = csLarge > csSmall && gdLarge > gdSmall && tsLarge > tsSmall &&
                    csEdited != csLarge && gdEdited != gdLarge && tsEdited != tsLarge;
        GD.Print(pass ? "ThemeTest: PASS" : "ThemeTest: FAIL");

        GetTree().Quit();
    }

    private static Theme MakeTheme(int size)
    {
        var theme = new Theme
        {
            DefaultFontSize = size,
        };
        theme.SetFontSize("normal_font_size", "RichTextLabel", size);
        theme.SetFontSize("bold_font_size", "RichTextLabel", size);
        theme.SetFontSize("italics_font_size", "RichTextLabel", size);
        theme.SetFontSize("mono_font_size", "RichTextLabel", size);
        return theme;
    }

    private static float HeightOf(Control control)
    {
        return control is MoreRichTextLabel csharp
            ? csharp.ContentHeight
            : control is TextServerRichTextLabel textServer
                ? textServer.ContentHeight
                : control.GetCombinedMinimumSize().Y;
    }

    private async System.Threading.Tasks.Task WaitFrames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
