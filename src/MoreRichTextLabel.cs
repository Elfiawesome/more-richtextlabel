using Godot;
using System.Collections.Generic;
using System.Text;

namespace MoreRichTextLabelLib;

public enum InlineRowAlignment
{
    Top,
    Middle,
    Bottom,
}

/// <summary>
/// C# implementation using one RichTextLabel per text block and a custom vertical block layout.
/// Panels and margins are drawn/laid out by this control. The RichTextLabel leaves are internal
/// children so the full RichTextLabel feature set (including RichTextEffects) remains available.
/// </summary>
[Tool]
public partial class MoreRichTextLabel : Control
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
    public bool SelectionEnabled
    {
        get => _selectionEnabled;
        set
        {
            if (_selectionEnabled == value)
            {
                return;
            }

            _selectionEnabled = value;
            foreach (DocText text in _textNodes)
            {
                if (text.View != null)
                {
                    ConfigureLeafInput(text.View);
                }
            }

            foreach (SplitTextViews split in _splitViews.Values)
            {
                if (split.Head != null && IsInstanceValid(split.Head))
                {
                    ConfigureLeafInput(split.Head);
                }

                if (split.Tail != null && IsInstanceValid(split.Tail))
                {
                    ConfigureLeafInput(split.Tail);
                }
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
    public Godot.Collections.Array<RichTextEffect> CustomEffects
    {
        get => _customEffects;
        set
        {
            _customEffects = value ?? new Godot.Collections.Array<RichTextEffect>();
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
    private readonly List<RichTextLabel> _placedViews = new();
    private readonly Dictionary<DocText, SplitTextViews> _splitViews = new();

    private sealed class SplitTextViews
    {
        public RichTextLabel Head = null!;
        public RichTextLabel Tail = null!;
    }
    private StyleBox? _defaultPanelStyle;
    private bool _selectionEnabled;
    private bool _trimBlockWhitespace = true;
    private Godot.Collections.Array<RichTextEffect> _customEffects = new();
    private DocSequence? _root;
    private readonly List<DocText> _textNodes = new();
    private readonly List<DocPanel> _panels = new();
    private readonly Dictionary<DocText, float> _preferredTextWidths = new();
    private readonly HashSet<StyleBox> _connectedStyles = new();
    private readonly List<StyleBox> _connectedStyleList = new();
    private float _contentHeight;
    private bool _inLayout;

    public float ContentHeight => _contentHeight;
    public int TextBlockCount => _textNodes.Count;

    public override void _Ready()
    {
        Rebuild();
    }

    public override void _ExitTree()
    {
        DisconnectStyleSignals();
        _inLayout = false;
    }

    public void Rebuild()
    {
        if (!IsInsideTree())
        {
            return;
        }

        foreach (DocText text in _textNodes)
        {
            if (text.View != null && IsInstanceValid(text.View))
            {
                RemoveChild(text.View);
                text.View.Free();
                text.View = null;
            }
        }

        _textNodes.Clear();
        _preferredTextWidths.Clear();

        foreach (SplitTextViews split in _splitViews.Values)
        {
            foreach (RichTextLabel view in new[] { split.Head, split.Tail })
            {
                if (view != null && IsInstanceValid(view))
                {
                    RemoveChild(view);
                    view.Free();
                }
            }
        }

        _splitViews.Clear();
        _root = BbcodeDocumentParser.Parse(_text, TrimBlockWhitespace);
        CreateViews(_root);
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

    private void CreateViews(DocSequence sequence)
    {
        foreach (DocBlock block in sequence.Children)
        {
            switch (block)
            {
                case DocSequence childSequence:
                    CreateViews(childSequence);
                    break;

                case DocText textBlock:
                    CreateLeaf(textBlock);
                    break;

                case DocPanel panel:
                    CreateViews(panel.Content);
                    break;

                case DocMargin margin:
                    CreateViews(margin.Content);
                    break;
            }
        }
    }

    private void CreateLeaf(DocText block)
    {
        var leaf = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = false,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            ClipContents = false,
        };

        leaf.AddThemeStyleboxOverride("normal", EmptyStyle);

        foreach (RichTextEffect effect in _customEffects)
        {
            if (effect != null)
            {
                leaf.InstallEffect(effect);
            }
        }

        ConfigureLeafInput(leaf);
        AddChild(leaf, false, InternalMode.Front);
        leaf.Text = block.Markup;

        block.View = leaf;
        _textNodes.Add(block);
    }

    private void ConfigureLeafInput(RichTextLabel leaf)
    {
        leaf.SelectionEnabled = _selectionEnabled;
        leaf.MouseFilter = _selectionEnabled
            ? MouseFilterEnum.Stop
            : MouseFilterEnum.Ignore;
        leaf.FocusMode = _selectionEnabled
            ? FocusModeEnum.Click
            : FocusModeEnum.None;
    }

    private void Relayout() => Relayout(Size.X);

    private void Relayout(float width)
    {
        if (_inLayout)
        {
            return;
        }

        if (_root == null)
        {
            return;
        }

        if (HasInvalidViews())
        {
            Callable.From(Rebuild).CallDeferred();
            return;
        }

        HideAllTextViews();

        _inLayout = true;
        try
        {
            _panels.Clear();
            _placedViews.Clear();

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
        public readonly int TextStart;
        public readonly int TextEnd;
        public readonly int PanelStart;
        public readonly int PanelEnd;
        public readonly float Height;

        public RowItem(int textStart, int textEnd, int panelStart, int panelEnd, float height)
        {
            TextStart = textStart;
            TextEnd = textEnd;
            PanelStart = panelStart;
            PanelEnd = panelEnd;
            Height = height;
        }
    }

    private bool HasInvalidViews()
    {
        foreach (DocText text in _textNodes)
        {
            if (text.View == null || !IsInstanceValid(text.View))
            {
                return true;
            }
        }

        foreach (SplitTextViews split in _splitViews.Values)
        {
            if (split.Head == null || split.Tail == null ||
                !IsInstanceValid(split.Head) || !IsInstanceValid(split.Tail))
            {
                return true;
            }
        }

        return false;
    }

    private void RelayoutDeferred()
    {
        if (!IsInsideTree())
        {
            return;
        }

        _inLayout = false;

        if (_root == null || HasInvalidViews())
        {
            Rebuild();
            return;
        }

        Relayout();
    }

    private void RebuildDeferred()
    {
        if (IsInsideTree())
        {
            Rebuild();
        }
    }
    private void HideAllTextViews()
    {
        foreach (DocText text in _textNodes)
        {
            if (text.View != null && IsInstanceValid(text.View))
            {
                text.View.Visible = false;
            }
        }

        foreach (SplitTextViews split in _splitViews.Values)
        {
            if (split.Head != null && IsInstanceValid(split.Head))
            {
                split.Head.Visible = false;
            }

            if (split.Tail != null && IsInstanceValid(split.Tail))
            {
                split.Tail.Visible = false;
            }
        }
    }

    private SplitTextViews GetSplitViews(DocText text)
    {
        if (_splitViews.TryGetValue(text, out SplitTextViews? split))
        {
            return split;
        }

        split = new SplitTextViews
        {
            Head = CreateAuxiliaryLeaf(),
            Tail = CreateAuxiliaryLeaf(),
        };

        _splitViews[text] = split;
        return split;
    }

    private RichTextLabel CreateAuxiliaryLeaf()
    {
        var leaf = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = false,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            ClipContents = false,
            Visible = false,
        };

        leaf.AddThemeStyleboxOverride("normal", EmptyStyle);
        ConfigureLeafInput(leaf);
        AddChild(leaf, false, InternalMode.Front);
        return leaf;
    }

    private static float GetViewPreferredWidth(RichTextLabel view)
    {
        if (!IsInstanceValid(view))
        {
            return 0.0f;
        }

        TextServer.AutowrapMode previousAutowrap = view.AutowrapMode;
        Vector2 previousSize = view.Size;

        view.AutowrapMode = TextServer.AutowrapMode.Off;
        view.Size = new Vector2(100000.0f, 1.0f);
        float preferred = view.GetContentWidth();

        view.AutowrapMode = previousAutowrap;
        view.Size = previousSize;
        return preferred;
    }

    private float PlaceTextView(RichTextLabel view, float x, float y, float width)
    {
        if (!IsInstanceValid(view))
        {
            return 0.0f;
        }

        view.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        view.Position = new Vector2(x, y);
        view.Size = new Vector2(width, 1.0f);
        float height = view.GetContentHeight();
        view.Size = new Vector2(width, height);
        view.Visible = true;
        _placedViews.Add(view);
        return height;
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

        SplitTextViews split = GetSplitViews(text);
        RichTextLabel head = split.Head;
        RichTextLabel tail = split.Tail;

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
            head.Text = candidate;
            float candidateWidth = GetViewPreferredWidth(head);

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

        head.Text = headText;
        int headTextStart = _placedViews.Count;
        int headPanelStart = _panels.Count;
        float headHeight = PlaceTextView(head, lineX, cursorY, headWidth);
        rowItems.Add(new RowItem(headTextStart, _placedViews.Count, headPanelStart, _panels.Count, headHeight));

        float rowHeight = Mathf.Max(lineHeight, headHeight);
        ApplyRowAlignment(rowItems, rowHeight);
        rowItems.Clear();
        cursorY += rowHeight;
        lineX = x;
        lineHeight = 0.0f;
        hasLine = false;

        var tailBuilder = new StringBuilder();
        for (int i = fitCount; i < words.Count; i++)
        {
            tailBuilder.Append(words[i]);
        }

        string tailText = tailBuilder.ToString().TrimStart(' ', '\t');
        if (tailText.Length == 0)
        {
            return true;
        }

        tail.Text = tailText;
        float tailWidth = Mathf.Min(GetViewPreferredWidth(tail), width);
        if (tailWidth <= 0.01f)
        {
            tailWidth = width;
        }

        int tailTextStart = _placedViews.Count;
        int tailPanelStart = _panels.Count;
        float tailHeight = PlaceTextView(tail, x, cursorY, tailWidth);
        rowItems.Add(new RowItem(tailTextStart, _placedViews.Count, tailPanelStart, _panels.Count, tailHeight));
        lineX = x + tailWidth;
        lineHeight = tailHeight;
        hasLine = true;
        return true;
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

            int textStart = _placedViews.Count;
            int panelStart = _panels.Count;
            float itemHeight = PlaceBlock(block, lineX, cursorY, itemWidth);
            rowItems.Add(new RowItem(textStart, _placedViews.Count, panelStart, _panels.Count, itemHeight));
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

            for (int i = item.TextStart; i < item.TextEnd; i++)
            {
                RichTextLabel view = _placedViews[i];
                if (IsInstanceValid(view))
                {
                    view.Position = view.Position + new Vector2(0.0f, offset);
                }
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
        RichTextLabel? view = text.View;
        if (view == null || !IsInstanceValid(view))
        {
            return 0.0f;
        }

        return PlaceTextView(view, x, y, width);
    }
    private float GetPreferredWidth(DocBlock block)
    {
        switch (block)
        {
            case DocText text:
                return GetTextPreferredWidth(text);

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

    private float GetTextPreferredWidth(DocText text)
    {
        if (_preferredTextWidths.TryGetValue(text, out float cached))
        {
            return cached;
        }

        RichTextLabel? view = text.View;
        if (view == null || !IsInstanceValid(view))
        {
            return 0.0f;
        }

        float preferred = GetViewPreferredWidth(view);
        _preferredTextWidths[text] = preferred;
        return preferred;
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
            Callable.From(RelayoutDeferred).CallDeferred();
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
