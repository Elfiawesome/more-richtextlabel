using Godot;
using System.Collections.Generic;

namespace MoreRichTextLabelLib;

/// <summary>
/// Static C# TextServer implementation. Text is shaped with TextParagraph and drawn directly
/// by this control; panels/margins are laid out and drawn by the same custom block layout.
/// This renderer intentionally does not support RichTextEffect or custom effects.
/// </summary>
[Tool]
public partial class TextServerRichTextLabel : Control
{
    [Export(PropertyHint.MultilineText)]
    public string Text
    {
        get => _text;
        set
        {
            string next = value ?? "";
            if (_text == next)
            {
                return;
            }

            _text = next;
            if (IsInsideTree())
            {
                Rebuild();
            }
        }
    }

    [Export]
    public StyleBox? DefaultPanelStyle
    {
        get => _defaultPanelStyle;
        set
        {
            if (_defaultPanelStyle == value)
            {
                return;
            }

            _defaultPanelStyle = value;
            if (IsInsideTree())
            {
                Relayout();
                QueueRedraw();
            }
        }
    }

    [Export]
    public bool TrimBlockWhitespace
    {
        get => _trimBlockWhitespace;
        set
        {
            if (_trimBlockWhitespace == value)
            {
                return;
            }

            _trimBlockWhitespace = value;
            if (IsInsideTree())
            {
                Rebuild();
            }
        }
    }

    [Export]
    public InlineRowAlignment RowAlignment
    {
        get => _rowAlignment;
        set
        {
            if (_rowAlignment == value)
            {
                return;
            }

            _rowAlignment = value;
            if (IsInsideTree())
            {
                Relayout();
                QueueRedraw();
            }
        }
    }

    private static readonly StyleBoxEmpty EmptyStyle = new();

    private string _text = "";
    private InlineRowAlignment _rowAlignment = InlineRowAlignment.Top;
    private readonly List<TsTextBlock> _placedBlocks = new();
    private StyleBox? _defaultPanelStyle;
    private bool _trimBlockWhitespace = true;
    private readonly HashSet<StyleBox> _connectedStyles = new();
    private readonly List<StyleBox> _connectedStyleList = new();
    private DocSequence? _root;
    private readonly Dictionary<DocText, TsTextBlock> _blocks = new();
    private readonly Dictionary<DocText, float> _preferredTextWidths = new();
    private readonly List<DocPanel> _panels = new();
    private float _contentHeight;
    private bool _inLayout;
    private int _baseFontSize;
    private float _lineSpacing;
    private Font _normalFont = null!;
    private Font _boldFont = null!;
    private Font _italicsFont = null!;
    private Color _textColor = Colors.White;

    public float ContentHeight => _contentHeight;
    public int TextBlockCount => _blocks.Count;

    public override void _Ready()
    {
        CacheTheme();
        Rebuild();
    }

    public override void _ExitTree()
    {
        DisconnectStyleSignals();
    }

    public void Rebuild()
    {
        if (!IsInsideTree())
        {
            return;
        }

        CacheTheme();
        _blocks.Clear();
        _preferredTextWidths.Clear();
        _root = BbcodeDocumentParser.Parse(_text, TrimBlockWhitespace);
        CreateBlocks(_root);
        Relayout();
    }

    public void SetTextForced(string value)
    {
        _text = value ?? "";
        Rebuild();
    }

    public void ForceRelayout()
    {
        Relayout(Size.X);
    }

    public void ForceRelayoutAtWidth(float width)
    {
        Relayout(width);
    }

    private void CacheTheme()
    {
        _normalFont = GetThemeFont("normal_font", "RichTextLabel") ?? GetThemeFont("font", "Label");
        _boldFont = GetThemeFont("bold_font", "RichTextLabel") ?? _normalFont;
        _italicsFont = GetThemeFont("italics_font", "RichTextLabel") ?? _normalFont;
        _baseFontSize = GetThemeFontSize("normal_font_size", "RichTextLabel");
        if (_baseFontSize <= 0)
        {
            _baseFontSize = GetThemeFontSize("font_size", "Label");
        }
        if (_baseFontSize <= 0)
        {
            _baseFontSize = 16;
        }
        _lineSpacing = GetThemeConstant("line_separation", "RichTextLabel");
        _textColor = GetThemeColor("default_color", "RichTextLabel");
    }

    private void CreateBlocks(DocSequence sequence)
    {
        foreach (DocBlock block in sequence.Children)
        {
            switch (block)
            {
                case DocSequence childSequence:
                    CreateBlocks(childSequence);
                    break;

                case DocText textBlock:
                {
                    var parser = new TsBBCodeParser(
                        textBlock.Markup,
                        _normalFont,
                        _boldFont,
                        _italicsFont,
                        _baseFontSize,
                        _lineSpacing);
                    _blocks[textBlock] = parser.Parse();
                    break;
                }

                case DocPanel panel:
                    CreateBlocks(panel.Content);
                    break;

                case DocMargin margin:
                    CreateBlocks(margin.Content);
                    break;
            }
        }
    }

    private void RelayoutDeferred()
    {
        if (!IsInsideTree())
        {
            return;
        }

        _inLayout = false;
        Relayout();
    }

    private void RebuildDeferred()
    {
        if (IsInsideTree())
        {
            Rebuild();
        }
    }

    private void Relayout() => Relayout(Size.X);

    private void Relayout(float width)
    {
        if (_inLayout || _root == null)
        {
            return;
        }

        _inLayout = true;
        try
        {
            _panels.Clear();
            _placedBlocks.Clear();

            if (float.IsNaN(width) || width <= 0.0f)
            {
                width = 1.0f;
            }

            _contentHeight = LayoutSequence(_root, 0.0f, 0.0f, width);
        }
        finally
        {
            _inLayout = false;
        }

        UpdateStyleConnections();
        UpdateMinimumSize();
        QueueRedraw();
    }

    private readonly struct RowItem
    {
        public readonly int BlockStart;
        public readonly int BlockEnd;
        public readonly int PanelStart;
        public readonly int PanelEnd;
        public readonly float Height;

        public RowItem(int blockStart, int blockEnd, int panelStart, int panelEnd, float height)
        {
            BlockStart = blockStart;
            BlockEnd = blockEnd;
            PanelStart = panelStart;
            PanelEnd = panelEnd;
            Height = height;
        }
    }

    private float LayoutSequence(DocSequence sequence, float x, float y, float width)
    {
        float cursorY = y;
        float lineX = x;
        float lineHeight = 0.0f;
        bool hasLine = false;
        var rowItems = new List<RowItem>();

        foreach (DocBlock block in sequence.Children)
        {
            if (block is DocText blockText && IsBlockText(blockText))
            {
                if (hasLine)
                {
                    ApplyRowAlignment(rowItems, lineHeight);
                    rowItems.Clear();
                    cursorY += lineHeight;
                    lineX = x;
                    lineHeight = 0.0f;
                    hasLine = false;
                }

                cursorY += PlaceText(blockText, x, cursorY, width);
                continue;
            }

            float preferred = GetPreferredWidth(block);
            float itemWidth = Mathf.Min(preferred, width);

            if (itemWidth <= 0.01f)
            {
                continue;
            }

            if (hasLine && lineX + itemWidth > x + width)
            {
                if (block is DocText splitText && InlineTextUtil.CanSplit(splitText.Markup) &&
                    TryPlaceSplitText(splitText, x, width, rowItems, ref lineX, ref cursorY, ref lineHeight, ref hasLine))
                {
                    continue;
                }

                ApplyRowAlignment(rowItems, lineHeight);
                rowItems.Clear();
                cursorY += lineHeight;
                lineX = x;
                lineHeight = 0.0f;
                hasLine = false;
            }

            int blockStart = _placedBlocks.Count;
            int panelStart = _panels.Count;
            float itemHeight = PlaceBlock(block, lineX, cursorY, itemWidth);
            rowItems.Add(new RowItem(blockStart, _placedBlocks.Count, panelStart, _panels.Count, itemHeight));
            lineX += itemWidth;
            lineHeight = Mathf.Max(lineHeight, itemHeight);
            hasLine = true;
        }

        if (hasLine)
        {
            ApplyRowAlignment(rowItems, lineHeight);
            cursorY += lineHeight;
        }

        return cursorY - y;
    }

    private void ApplyRowAlignment(List<RowItem> rowItems, float lineHeight)
    {
        foreach (RowItem item in rowItems)
        {
            float offset = GetRowOffset(item.Height, lineHeight);
            if (offset == 0.0f)
            {
                continue;
            }

            for (int i = item.BlockStart; i < item.BlockEnd; i++)
            {
                TsTextBlock block = _placedBlocks[i];
                block.Position = block.Position + new Vector2(0.0f, offset);
            }

            for (int i = item.PanelStart; i < item.PanelEnd; i++)
            {
                DocPanel panel = _panels[i];
                panel.Rect = new Rect2(
                    panel.Rect.Position + new Vector2(0.0f, offset),
                    panel.Rect.Size);
            }
        }
    }

    private float GetRowOffset(float itemHeight, float lineHeight)
    {
        return _rowAlignment switch
        {
            InlineRowAlignment.Middle => (lineHeight - itemHeight) * 0.5f,
            InlineRowAlignment.Bottom => lineHeight - itemHeight,
            _ => 0.0f,
        };
    }

    private float PlaceBlock(DocBlock block, float x, float y, float width)
    {
        switch (block)
        {
            case DocText text:
                return PlaceText(text, x, y, width);

            case DocMargin margin:
            {
                float contentX = x + margin.Left;
                float contentY = y + margin.Top;
                float contentWidth = Mathf.Max(1.0f, width - margin.Left - margin.Right);
                float height = LayoutSequence(margin.Content, contentX, contentY, contentWidth);
                return height + margin.Top + margin.Bottom;
            }

            case DocPanel panel:
            {
                StyleBox style = GetPanelStyle(panel);
                float left = style.GetMargin(Side.Left);
                float top = style.GetMargin(Side.Top);
                float right = style.GetMargin(Side.Right);
                float bottom = style.GetMargin(Side.Bottom);
                float contentWidth = Mathf.Max(1.0f, width - left - right);

                panel.Rect = new Rect2(x, y, width, 0.0f);
                _panels.Add(panel);

                float contentHeight = LayoutSequence(panel.Content, x + left, y + top, contentWidth);
                float height = contentHeight + top + bottom;
                panel.Rect = new Rect2(x, y, width, height);
                return height;
            }

            case DocSequence sequence:
                return LayoutSequence(sequence, x, y, width);

            default:
                return 0.0f;
        }
    }

    private float PlaceText(DocText text, float x, float y, float width)
    {
        if (!_blocks.TryGetValue(text, out TsTextBlock? tsBlock))
        {
            return 0.0f;
        }

        return PlaceTextBlock(tsBlock, x, y, width);
    }

    private float PlaceTextBlock(TsTextBlock block, float x, float y, float width)
    {
        block.Paragraph.Width = width;
        float height = block.Paragraph.GetSize().Y;
        block.Position = new Vector2(x, y);
        block.Height = height;
        _placedBlocks.Add(block);
        return height;
    }

    private TsTextBlock BuildTextBlock(string markup)
    {
        var parser = new TsBBCodeParser(
            markup,
            _normalFont,
            _boldFont,
            _italicsFont,
            _baseFontSize,
            _lineSpacing);
        return parser.Parse();
    }

    private float MeasurePlainWidth(string text)
    {
        return _normalFont.GetStringSize(text, HorizontalAlignment.Left, -1.0f, _baseFontSize).X;
    }

    private bool TryPlaceSplitText(
        DocText text,
        float x,
        float width,
        List<RowItem> rowItems,
        ref float lineX,
        ref float cursorY,
        ref float lineHeight,
        ref bool hasLine)
    {
        List<string> words = InlineTextUtil.SplitWords(text.Markup);
        if (words.Count < 2)
        {
            return false;
        }

        float remaining = (x + width) - lineX;
        if (remaining <= 0.0f)
        {
            return false;
        }

        string headText = "";
        float headWidth = 0.0f;
        int fitCount = 0;

        for (int i = 0; i < words.Count; i++)
        {
            string candidate = headText + words[i];
            float candidateWidth = MeasurePlainWidth(candidate);

            if (candidateWidth > remaining)
            {
                break;
            }

            headText = candidate;
            headWidth = candidateWidth;
            fitCount++;
        }

        if (fitCount == 0 || fitCount >= words.Count)
        {
            return false;
        }

        TsTextBlock headBlock = BuildTextBlock(headText);
        int headBlockStart = _placedBlocks.Count;
        int headPanelStart = _panels.Count;
        float headHeight = PlaceTextBlock(headBlock, lineX, cursorY, headWidth);
        rowItems.Add(new RowItem(headBlockStart, _placedBlocks.Count, headPanelStart, _panels.Count, headHeight));

        float rowHeight = Mathf.Max(lineHeight, headHeight);
        ApplyRowAlignment(rowItems, rowHeight);
        rowItems.Clear();
        cursorY += rowHeight;
        lineX = x;
        lineHeight = 0.0f;
        hasLine = false;

        string tailText = "";
        for (int i = fitCount; i < words.Count; i++)
        {
            tailText += words[i];
        }
        tailText = tailText.TrimStart(' ', '\t');

        if (tailText.Length == 0)
        {
            return true;
        }

        TsTextBlock tailBlock = BuildTextBlock(tailText);
        float tailWidth = Mathf.Min(Mathf.Ceil(tailBlock.Paragraph.GetNonWrappedSize().X), width);
        if (tailWidth <= 0.01f)
        {
            tailWidth = width;
        }

        int tailBlockStart = _placedBlocks.Count;
        int tailPanelStart = _panels.Count;
        float tailHeight = PlaceTextBlock(tailBlock, x, cursorY, tailWidth);
        rowItems.Add(new RowItem(tailBlockStart, _placedBlocks.Count, tailPanelStart, _panels.Count, tailHeight));
        lineX = x + tailWidth;
        lineHeight = tailHeight;
        hasLine = true;
        return true;
    }
    private float GetPreferredWidth(DocBlock block)
    {
        switch (block)
        {
            case DocText text:
            {
                if (_preferredTextWidths.TryGetValue(text, out float cached))
                {
                    return cached;
                }

                if (!_blocks.TryGetValue(text, out TsTextBlock? tsBlock))
                {
                    return 0.0f;
                }

                float preferred = Mathf.Ceil(tsBlock.Paragraph.GetNonWrappedSize().X);
                _preferredTextWidths[text] = preferred;
                return preferred;
            }

            case DocPanel panel:
            {
                StyleBox style = GetPanelStyle(panel);
                return style.GetMargin(Side.Left) +
                       GetSequencePreferredWidth(panel.Content) +
                       style.GetMargin(Side.Right);
            }

            case DocMargin margin:
                return margin.Left + GetSequencePreferredWidth(margin.Content) + margin.Right;

            case DocSequence sequence:
                return GetSequencePreferredWidth(sequence);

            default:
                return 0.0f;
        }
    }

    private float GetSequencePreferredWidth(DocSequence sequence)
    {
        float width = 0.0f;

        foreach (DocBlock block in sequence.Children)
        {
            width += GetPreferredWidth(block);
        }

        return width;
    }

    private static bool IsBlockText(DocText text)
    {
        return text.Markup.IndexOf('\n') >= 0 || text.Markup.IndexOf('\r') >= 0;
    }
    private StyleBox GetPanelStyle(DocPanel panel)
    {
        return panel.Style ?? _defaultPanelStyle ?? EmptyStyle;
    }

    private void UpdateStyleConnections()
    {
        var needed = new HashSet<StyleBox>();

        if (_defaultPanelStyle != null)
        {
            needed.Add(_defaultPanelStyle);
        }

        foreach (DocPanel panel in _panels)
        {
            StyleBox style = GetPanelStyle(panel);
            if (style != EmptyStyle)
            {
                needed.Add(style);
            }
        }

        for (int i = _connectedStyleList.Count - 1; i >= 0; i--)
        {
            StyleBox style = _connectedStyleList[i];
            if (!needed.Contains(style))
            {
                if (IsInstanceValid(style))
                {
                    style.Changed -= OnStyleBoxChanged;
                }

                _connectedStyleList.RemoveAt(i);
                _connectedStyles.Remove(style);
            }
        }

        foreach (StyleBox style in needed)
        {
            if (_connectedStyles.Add(style))
            {
                style.Changed += OnStyleBoxChanged;
                _connectedStyleList.Add(style);
            }
        }
    }

    private void DisconnectStyleSignals()
    {
        foreach (StyleBox style in _connectedStyleList)
        {
            if (IsInstanceValid(style))
            {
                style.Changed -= OnStyleBoxChanged;
            }
        }

        _connectedStyleList.Clear();
        _connectedStyles.Clear();
    }

    private void OnStyleBoxChanged()
    {
        if (!IsInsideTree())
        {
            return;
        }

        Relayout();
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (DocPanel panel in _panels)
        {
            GetPanelStyle(panel).Draw(GetCanvasItem(), panel.Rect);
        }

        foreach (TsTextBlock block in _placedBlocks)
        {
            block.Paragraph.Draw(GetCanvasItem(), block.Position, _textColor);

            int lineCount = block.Paragraph.GetLineCount();
            for (int line = 0; line < lineCount; line++)
            {
                Godot.Collections.Array objects = block.Paragraph.GetLineObjects(line);
                foreach (Variant key in objects)
                {
                    int imageKey = key.AsInt32();
                    if (!block.Images.TryGetValue(imageKey, out Texture2D? texture))
                    {
                        continue;
                    }

                    Rect2 rect = block.Paragraph.GetLineObjectRect(line, key);
                    rect.Position += block.Position;
                    texture.DrawRect(GetCanvasItem(), rect, false, Colors.White);
                }
            }
        }
    }

    public override Vector2 _GetMinimumSize()
    {
        return new Vector2(0.0f, _contentHeight);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
        {
            Relayout();
        }
        else if (what == NotificationThemeChanged)
        {
            _preferredTextWidths.Clear();
            Callable.From(RebuildDeferred).CallDeferred();
        }
        else if (what == NotificationEnterTree)
        {
            _inLayout = false;

            if (IsNodeReady())
            {
                Callable.From(RebuildDeferred).CallDeferred();
            }
        }
        else if (what == NotificationExitTree)
        {
            _inLayout = false;
        }
        else if (what == NotificationVisibilityChanged)
        {
            if (IsVisibleInTree())
            {
                Callable.From(RelayoutDeferred).CallDeferred();
            }
        }
    }
}
