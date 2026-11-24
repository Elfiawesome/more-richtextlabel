using Godot;
using MoreRichTextLabelLib;

public partial class Demo : Control
{
    internal const string InlineDoc =
        "Equipment: " +
        "[panel=res://stylebox/black_red.tres]Iron Sword[/panel] " +
        "[panel=res://stylebox/pink_white_cornered.tres][img=28x28]res://icon.svg[/img] Health Potion[/panel] " +
        "and [panel=res://stylebox/nine_slice_small.tres][margin=8]Nested [panel=res://stylebox/black_red.tres]Inner[/panel] panel[/margin][/panel] " +
        "text continues naturally on the same line. [panel]Default style[/panel] is available too.";

    internal const string NineSliceDoc =
        "[panel=res://stylebox/nine_slice_detailed.tres]" +
        "The detailed 9-slice panel uses its own StyleBox content margins. " +
        "[b]Bold[/b], [i]italic[/i], [color=#ffcf70]colored[/color] text, and inline images " +
        "[img=24x24]res://icon.svg[/img] work inside.[/panel]";

    internal const string EffectsDoc =
        "Built-in effects: [wave amp=10 freq=6]Wave[/wave] [shake]Shake[/shake] " +
        "[rainbow]Rainbow[/rainbow] [pulse]Pulse[/pulse]. " +
        "Custom effects: [demo_pulse]DemoPulseEffect[/demo_pulse]. " +
        "Effects are native RichTextLabel effects, so existing effects and custom RichTextEffects work.";

    internal const string AlignmentDoc =
        "[panel=res://stylebox/black_red.tres][img=40x40]res://icon.svg[/img][/panel] Align";

    internal const string ThemeDoc =
        "[b]Theme-aware[/b] text follows the control's Theme. " +
        "Font family and size changes apply immediately to every internal text block.";

    private Godot.Collections.Array<RichTextEffect> _effects = new();

    public override void _Ready()
    {
        BuildUi();
    }

    private void BuildUi()
    {
        var background = new ColorRect
        {
            Color = new Color("12141b"),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(background);

        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(scroll);

        var outer = new MarginContainer();
        outer.AddThemeConstantOverride("margin_left", 32);
        outer.AddThemeConstantOverride("margin_top", 28);
        outer.AddThemeConstantOverride("margin_right", 32);
        outer.AddThemeConstantOverride("margin_bottom", 32);
        outer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(outer);

        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        column.AddThemeConstantOverride("separation", 18);
        outer.AddChild(column);

        var title = new Label { Text = "MoreRichTextLabel" };
        title.AddThemeFontSizeOverride("font_size", 34);
        title.AddThemeColorOverride("font_color", new Color("f4f6fb"));
        column.AddChild(title);

        var subtitle = new Label
        {
            Text = "Inline panels, margins, images, RichTextEffects, themes and word wrapping in one drop-in Control.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        subtitle.AddThemeFontSizeOverride("font_size", 15);
        subtitle.AddThemeColorOverride("font_color", new Color("9aa4b8"));
        column.AddChild(subtitle);

        var effect = GD.Load<RichTextEffect>("res://demo/effects/DemoPulseEffect.tres");
        if (effect != null)
        {
            _effects.Add(effect);
        }

        var defaultStyle = GD.Load<StyleBox>("res://stylebox/pink_white_cornered.tres");

        AddSection(column, "Inline panels and margins", InlineDoc, defaultStyle);

        AddSection(column, "Nine-slice panels", NineSliceDoc);

        AddSection(column, "Built-in and custom effects", EffectsDoc, null, _effects);

        AddAlignmentSection(column);

        AddThemeSection(column);

        var footer = new Label
        {
            Text = "Resize the window to see panels hug their content and plain text wrap word by word.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        footer.AddThemeFontSizeOverride("font_size", 13);
        footer.AddThemeColorOverride("font_color", new Color("7d879b"));
        column.AddChild(footer);
    }

    private void AddSection(
        VBoxContainer parent,
        string heading,
        string markup,
        StyleBox? defaultPanelStyle = null,
        Godot.Collections.Array<RichTextEffect>? effects = null,
        Theme? theme = null)
    {
        PanelContainer card = CreateCard();
        parent.AddChild(card);

        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 10);
        card.AddChild(column);

        column.AddChild(CreateHeading(heading));

        var body = new MoreRichTextLabel
        {
            Text = markup,
            DefaultPanelStyle = defaultPanelStyle,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };

        if (effects != null)
        {
            body.CustomEffects = effects;
        }

        if (theme != null)
        {
            body.Theme = theme;
        }

        column.AddChild(body);
    }

    private void AddAlignmentSection(VBoxContainer parent)
    {
        PanelContainer card = CreateCard();
        parent.AddChild(card);

        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 10);
        card.AddChild(column);

        column.AddChild(CreateHeading("Row alignment"));

        var note = new Label
        {
            Text = "Short text aligns to the tallest item in the row.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        note.AddThemeFontSizeOverride("font_size", 13);
        note.AddThemeColorOverride("font_color", new Color("9aa4b8"));
        column.AddChild(note);

        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 12);
        column.AddChild(row);

        AddAlignmentCell(row, "Top", InlineRowAlignment.Top);
        AddAlignmentCell(row, "Middle", InlineRowAlignment.Middle);
        AddAlignmentCell(row, "Bottom", InlineRowAlignment.Bottom);
    }

    private void AddAlignmentCell(HBoxContainer row, string labelText, InlineRowAlignment alignment)
    {
        var cell = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        cell.AddThemeConstantOverride("separation", 6);
        row.AddChild(cell);

        var label = new Label { Text = labelText };
        label.AddThemeFontSizeOverride("font_size", 13);
        label.AddThemeColorOverride("font_color", new Color("9aa4b8"));
        cell.AddChild(label);

        var body = new MoreRichTextLabel
        {
            Text = AlignmentDoc,
            RowAlignment = alignment,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0.0f, 72.0f),
        };
        cell.AddChild(body);
    }

    private void AddThemeSection(VBoxContainer parent)
    {
        var theme = new Theme();
        theme.SetFontSize("normal_font_size", "RichTextLabel", 21);
        theme.SetFontSize("bold_font_size", "RichTextLabel", 21);
        theme.SetFontSize("italics_font_size", "RichTextLabel", 21);
        theme.SetFontSize("mono_font_size", "RichTextLabel", 19);

        AddSection(parent, "Theme support", ThemeDoc, null, null, theme);
    }

    private PanelContainer CreateCard()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color("1a1d26"),
            BorderColor = new Color("303646"),
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(8);
        style.SetContentMarginAll(18);

        var card = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        card.AddThemeStyleboxOverride("panel", style);
        return card;
    }

    private Label CreateHeading(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 19);
        label.AddThemeColorOverride("font_color", new Color("dce2ee"));
        return label;
    }
}
