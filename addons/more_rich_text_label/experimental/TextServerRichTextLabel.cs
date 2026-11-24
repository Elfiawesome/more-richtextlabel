// TextServerRichTextLabel - self-contained static TextServer renderer for Godot 4.7+
//
// Copy this single file into your project and attach TextServerRichTextLabel.cs
// to a Control node. This renderer supports panels, margins, images and basic
// bold/italic formatting. It does not render colors or RichTextEffects, and
// intentionally keeps text static (no custom effects).

using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MoreRichTextLabelLib.Experimental;

// ---------------------------------------------------------------------------
// Document model
// ---------------------------------------------------------------------------
public enum DocBlockKind
{
    Sequence,
    Text,
    Panel,
    Margin,
}

public abstract class DocBlock
{
    public DocBlockKind Kind { get; protected set; }
}

public sealed class DocSequence : DocBlock
{
    public List<DocBlock> Children { get; } = new();

    public DocSequence()
    {
        Kind = DocBlockKind.Sequence;
    }
}

public sealed class DocText : DocBlock
{
    public string Markup { get; set; } = "";

    // Used by the RichTextLabel-leaf implementations.
    public RichTextLabel? View { get; set; }

    public DocText()
    {
        Kind = DocBlockKind.Text;
    }
}

public sealed class DocPanel : DocBlock
{
    public StyleBox? Style { get; set; }
    public DocSequence Content { get; } = new();
    public Rect2 Rect { get; set; }

    public DocPanel()
    {
        Kind = DocBlockKind.Panel;
    }
}

public sealed class DocMargin : DocBlock
{
    public float Left { get; set; }
    public float Top { get; set; }
    public float Right { get; set; }
    public float Bottom { get; set; }
    public DocSequence Content { get; } = new();

    public DocMargin()
    {
        Kind = DocBlockKind.Margin;
    }
}

// ---------------------------------------------------------------------------
// Inline text utilities
// ---------------------------------------------------------------------------


// ---------------------------------------------------------------------------
// BBCode document parser
// ---------------------------------------------------------------------------

/// <summary>
/// Splits a BBCode string into block nodes. Only [panel] and [margin] are custom block tags;
/// all other BBCode is preserved for the text renderer.
/// </summary>
// ---------------------------------------------------------------------------
// BBCode document parser
// ---------------------------------------------------------------------------
public sealed class BbcodeDocumentParser
{
    private sealed class OpenTag
    {
        public string Name = "";
        public string RawOpen = "";
        public string RawClose = "";
    }

    private readonly HashSet<string> VoidTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "br", "hr", "lb", "rb",
    };

    private readonly Dictionary<string, StyleBox?> _styleCache = new(StringComparer.Ordinal);

    private readonly string _source;
    private readonly bool _trimBlockWhitespace;
    private readonly StringBuilder _buffer = new();
    private readonly List<OpenTag> _openTags = new();
    private List<OpenTag> _bufferStartTags = new();
    private int _pos;

    public BbcodeDocumentParser(string source, bool trimBlockWhitespace)
    {
        _source = source;
        _trimBlockWhitespace = trimBlockWhitespace;
    }

    public DocSequence ParseDocument()
    {
        var root = new DocSequence();
        ParseSequence(root, null);
        return root;
    }

    private void ParseSequence(DocSequence destination, string? stopTag)
    {
        while (_pos < _source.Length)
        {
            int open = _source.IndexOf('[', _pos);
            if (open < 0)
            {
                _buffer.Append(_source, _pos, _source.Length - _pos);
                _pos = _source.Length;
                break;
            }

            if (open > _pos)
            {
                _buffer.Append(_source, _pos, open - _pos);
                _pos = open;
            }

            int close = _source.IndexOf(']', _pos + 1);
            if (close < 0)
            {
                _buffer.Append(_source, _pos, _source.Length - _pos);
                _pos = _source.Length;
                break;
            }

            string raw = _source.Substring(_pos, close - _pos + 1);
            string inner = _source.Substring(_pos + 1, close - _pos - 1);
            _pos = close + 1;

            ParseTag(inner, out string name, out bool isClosing, out string value);

            if (name.Equals("panel", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("margin", StringComparison.OrdinalIgnoreCase))
            {
                if (isClosing)
                {
                    if (stopTag != null && name.Equals(stopTag, StringComparison.OrdinalIgnoreCase))
                    {
                        FlushText(destination);
                        return;
                    }

                    // Unmatched custom closing tag: keep it as literal text.
                    _buffer.Append(raw);
                    continue;
                }

                FlushText(destination);

                if (name.Equals("panel", StringComparison.OrdinalIgnoreCase))
                {
                    var panel = new DocPanel { Style = LoadStyle(value) };
                    ParseSequence(panel.Content, "panel");
                    destination.Children.Add(panel);
                }
                else
                {
                    var margin = new DocMargin();
                    ApplyMargin(margin, value);
                    ParseSequence(margin.Content, "margin");
                    destination.Children.Add(margin);
                }

                continue;
            }

            AppendStandardTag(name, isClosing, raw);
        }

        FlushText(destination);
    }

    private void AppendStandardTag(string name, bool isClosing, string raw)
    {
        if (isClosing)
        {
            _buffer.Append(raw);

            for (int i = _openTags.Count - 1; i >= 0; i--)
            {
                if (_openTags[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    _openTags.RemoveAt(i);
                    break;
                }
            }

            return;
        }

        _buffer.Append(raw);

        if (!VoidTags.Contains(name))
        {
            _openTags.Add(new OpenTag
            {
                Name = name,
                RawOpen = raw,
                RawClose = $"[/{name}]",
            });
        }
    }

    private void FlushText(DocSequence destination)
    {
        if (_buffer.Length == 0)
        {
            _bufferStartTags = new List<OpenTag>(_openTags);
            return;
        }

        string raw = _buffer.ToString();
        _buffer.Clear();

        if (_trimBlockWhitespace)
        {
            raw = raw.Trim('\t', '\r', '\n');
        }

        if (raw.Length > 0)
        {
            var markup = new StringBuilder(raw.Length + 32);

            foreach (OpenTag tag in _bufferStartTags)
            {
                markup.Append(tag.RawOpen);
            }

            markup.Append(raw);

            for (int i = _openTags.Count - 1; i >= 0; i--)
            {
                markup.Append(_openTags[i].RawClose);
            }

            destination.Children.Add(new DocText { Markup = markup.ToString() });
        }

        _bufferStartTags = new List<OpenTag>(_openTags);
    }

    private void ParseTag(string inner, out string name, out bool isClosing, out string value)
    {
        name = "";
        value = "";
        isClosing = inner.StartsWith('/');

        int start = isClosing ? 1 : 0;
        int end = start;

        while (end < inner.Length && inner[end] != '=' && !char.IsWhiteSpace(inner[end]))
        {
            end++;
        }

        name = inner.Substring(start, end - start).Trim();

        if (!isClosing)
        {
            int equals = inner.IndexOf('=');
            if (equals >= 0)
            {
                value = inner.Substring(equals + 1).Trim();
            }
        }
    }

    private StyleBox? LoadStyle(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (_styleCache.TryGetValue(path, out StyleBox? cached))
        {
            return cached;
        }

        StyleBox? style = null;

        try
        {
            if (ResourceLoader.Exists(path))
            {
                style = ResourceLoader.Load<StyleBox>(path);
            }
        }
        catch (Exception exception)
        {
            GD.PushWarning($"MoreRichTextLabel: failed to load StyleBox '{path}': {exception.Message}");
        }

        if (style == null)
        {
            GD.PushWarning($"MoreRichTextLabel: StyleBox not found or wrong type: {path}");
        }

        _styleCache[path] = style;
        return style;
    }

    private void ApplyMargin(DocMargin margin, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            SetMargin(margin, 0);
            return;
        }

        string[] parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (!TryFloat(parts, 0, out float first))
        {
            GD.PushWarning($"MoreRichTextLabel: invalid margin value '{value}'");
            SetMargin(margin, 0);
            return;
        }

        switch (parts.Length)
        {
            case 1:
                SetMargin(margin, first);
                break;

            case 2 when TryFloat(parts, 1, out float second):
                margin.Left = first;
                margin.Right = first;
                margin.Top = second;
                margin.Bottom = second;
                break;

            case 4 when TryFloat(parts, 1, out float top) &&
                        TryFloat(parts, 2, out float right) &&
                        TryFloat(parts, 3, out float bottom):
                margin.Left = first;
                margin.Top = top;
                margin.Right = right;
                margin.Bottom = bottom;
                break;

            default:
                GD.PushWarning($"MoreRichTextLabel: invalid margin value '{value}'");
                SetMargin(margin, first);
                break;
        }
    }

    private bool TryFloat(string[] parts, int index, out float value)
    {
        return float.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private void SetMargin(DocMargin margin, float value)
    {
        margin.Left = value;
        margin.Top = value;
        margin.Right = value;
        margin.Bottom = value;
    }
}

// ---------------------------------------------------------------------------
// MoreRichTextLabel control
// ---------------------------------------------------------------------------
// ---------------------------------------------------------------------------
// TextServer BBCode parser
// ---------------------------------------------------------------------------
public sealed class TsTextBlock
{
    public TextParagraph Paragraph { get; } = new();
    public Dictionary<int, Texture2D> Images { get; } = new();
    public Vector2 Position { get; set; }
    public float Height { get; set; }
    public string Markup { get; set; } = "";
}

/// <summary>
/// Minimal BBCode parser for the TextServer renderer. Supports bold, italics and images.
/// Other formatting tags are stripped/ignored; TextParagraph draws uniform-colored text.
/// </summary>
public sealed class TsBBCodeParser
{
    private readonly string _source;
    private readonly Font _normalFont;
    private readonly Font _boldFont;
    private readonly Font _italicsFont;
    private readonly float _lineSpacing;
    private int _pos;

    private bool _bold;
    private bool _italics;
    private int _fontSize;
    private readonly StringBuilder _text = new();
    private int _nextImageKey;

    public TsBBCodeParser(
        string source,
        Font normalFont,
        Font boldFont,
        Font italicsFont,
        int baseFontSize,
        float lineSpacing)
    {
        _source = source ?? "";
        _normalFont = normalFont;
        _boldFont = boldFont;
        _italicsFont = italicsFont;
        _lineSpacing = lineSpacing;
        _fontSize = baseFontSize;
    }

    public TsTextBlock Parse()
    {
        var block = new TsTextBlock { Markup = _source };
        TextParagraph paragraph = block.Paragraph;

        paragraph.BreakFlags =
            TextServer.LineBreakFlag.WordBound |
            TextServer.LineBreakFlag.Adaptive |
            TextServer.LineBreakFlag.Mandatory |
            TextServer.LineBreakFlag.TrimStartEdgeSpaces |
            TextServer.LineBreakFlag.TrimEndEdgeSpaces;
        paragraph.LineSpacing = _lineSpacing;
        paragraph.Alignment = HorizontalAlignment.Left;
        paragraph.Width = 1.0f;

        while (_pos < _source.Length)
        {
            int open = _source.IndexOf('[', _pos);
            if (open < 0)
            {
                AppendText(_source.Substring(_pos));
                _pos = _source.Length;
                break;
            }

            if (open > _pos)
            {
                AppendText(_source.Substring(_pos, open - _pos));
                _pos = open;
            }

            int close = _source.IndexOf(']', _pos + 1);
            if (close < 0)
            {
                AppendText(_source.Substring(_pos));
                _pos = _source.Length;
                break;
            }

            string raw = _source.Substring(_pos, close - _pos + 1);
            string inner = _source.Substring(_pos + 1, close - _pos - 1);
            _pos = close + 1;

            ParseTag(inner, paragraph, block);
        }

        FlushText(paragraph);
        return block;
    }

    private void ParseTag(string inner, TextParagraph paragraph, TsTextBlock block)
    {
        bool closing = inner.StartsWith('/');
        int start = closing ? 1 : 0;
        int end = start;

        while (end < inner.Length && inner[end] != '=' && !char.IsWhiteSpace(inner[end]))
        {
            end++;
        }

        string name = inner.Substring(start, end - start).Trim().ToLowerInvariant();
        string value = "";
        int equals = inner.IndexOf('=');

        if (!closing && equals >= 0)
        {
            value = inner.Substring(equals + 1).Trim();
        }

        switch (name)
        {
            case "b":
                FlushText(paragraph);
                _bold = !closing;
                break;

            case "i":
                FlushText(paragraph);
                _italics = !closing;
                break;

            case "color":
            case "bgcolor":
            case "fgcolor":
            case "font":
            case "font_size":
            case "center":
            case "left":
            case "right":
                FlushText(paragraph);
                break;

            case "lb":
                AppendText("[");
                break;

            case "rb":
                AppendText("]");
                break;

            case "br":
                AppendText("\n");
                break;

            case "img":
                if (!closing)
                {
                    AddImage(value, paragraph, block);
                }
                break;

            default:
                // Unknown tags are dropped rather than shown as markup.
                break;
        }
    }

    private void AddImage(string value, TextParagraph paragraph, TsTextBlock block)
    {
        int close = _source.IndexOf("[/img]", _pos, StringComparison.OrdinalIgnoreCase);
        if (close < 0)
        {
            return;
        }

        string path = _source.Substring(_pos, close - _pos).Trim();
        _pos = close + "[/img]".Length;

        Texture2D? texture = null;
        try
        {
            texture = ResourceLoader.Load<Texture2D>(path);
        }
        catch (Exception exception)
        {
            GD.PushWarning($"TextServerRichTextLabel: failed to load image '{path}': {exception.Message}");
        }

        if (texture == null)
        {
            return;
        }

        Vector2 size = texture.GetSize();

        if (!string.IsNullOrEmpty(value))
        {
            string[] parts = value.ToLowerInvariant().Split('x');
            if (parts.Length == 2 &&
                float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float width) &&
                float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float height))
            {
                if (width > 0 && height > 0)
                {
                    size = new Vector2(width, height);
                }
                else if (width > 0)
                {
                    size = new Vector2(width, size.Y * width / size.X);
                }
                else if (height > 0)
                {
                    size = new Vector2(size.X * height / size.Y, height);
                }
            }
        }

        FlushText(paragraph);

        int key = _nextImageKey++;
        block.Images[key] = texture;
        paragraph.AddObject(key, size, InlineAlignment.Center, 1);
    }

    private void AppendText(string text)
    {
        _text.Append(text);
    }

    private Font CurrentFont()
    {
        if (_italics)
        {
            return _italicsFont;
        }

        if (_bold)
        {
            return _boldFont;
        }

        return _normalFont;
    }

    private void FlushText(TextParagraph paragraph)
    {
        if (_text.Length == 0)
        {
            return;
        }

        paragraph.AddString(_text.ToString(), CurrentFont(), _fontSize, "", new Variant());
        _text.Clear();
    }
}
public enum TextServerRowAlignment
{
    Top,
    Middle,
    Bottom,
}

// ---------------------------------------------------------------------------
// Static TextServer control
// ---------------------------------------------------------------------------
[Tool]
[GlobalClass]
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
    public TextServerRowAlignment RowAlignment
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

    private readonly StyleBoxEmpty _emptyStyle = new();

    private string _text = "";
    private TextServerRowAlignment _rowAlignment = TextServerRowAlignment.Top;
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
        _root = new BbcodeDocumentParser(_text, TrimBlockWhitespace).ParseDocument();
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

    private bool CanSplitPlainText(string markup)
    {
        if (markup.IndexOf('[') >= 0 ||
            markup.IndexOf('\n') >= 0 ||
            markup.IndexOf('\r') >= 0)
        {
            return false;
        }

        bool hasWord = false;
        bool hasWhitespace = false;

        foreach (char c in markup)
        {
            if (char.IsWhiteSpace(c))
            {
                hasWhitespace = true;
            }
            else
            {
                hasWord = true;
            }
        }

        return hasWord && hasWhitespace;
    }

    private List<string> SplitWords(string markup)
    {
        var words = new List<string>();
        int i = 0;

        while (i < markup.Length)
        {
            int start = i;

            while (i < markup.Length && char.IsWhiteSpace(markup[i]))
            {
                i++;
            }

            int wordStart = i;

            while (i < markup.Length && !char.IsWhiteSpace(markup[i]))
            {
                i++;
            }

            if (i > wordStart)
            {
                words.Add(markup.Substring(start, i - start));
            }
            else if (i == markup.Length && words.Count > 0 && start < i)
            {
                words[words.Count - 1] += markup.Substring(start);
            }
        }

        return words;
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
                if (block is DocText splitText && CanSplitPlainText(splitText.Markup) &&
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
            TextServerRowAlignment.Middle => (lineHeight - itemHeight) * 0.5f,
            TextServerRowAlignment.Bottom => lineHeight - itemHeight,
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
        List<string> words = SplitWords(text.Markup);
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

    private bool IsBlockText(DocText text)
    {
        return text.Markup.IndexOf('\n') >= 0 || text.Markup.IndexOf('\r') >= 0;
    }
    private StyleBox GetPanelStyle(DocPanel panel)
    {
        return panel.Style ?? _defaultPanelStyle ?? _emptyStyle;
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
            if (style != _emptyStyle)
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