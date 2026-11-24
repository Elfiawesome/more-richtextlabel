using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MoreRichTextLabelLib;

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
