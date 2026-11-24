using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MoreRichTextLabelLib;

/// <summary>
/// Splits a BBCode string into block nodes. Only [panel] and [margin] are custom block tags;
/// all other BBCode is preserved for the text renderer.
/// </summary>
public sealed class BbcodeDocumentParser
{
    private sealed class OpenTag
    {
        public string Name = "";
        public string RawOpen = "";
        public string RawClose = "";
    }

    private static readonly HashSet<string> VoidTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "br", "hr", "lb", "rb",
    };

    private static readonly Dictionary<string, StyleBox?> StyleCache = new(StringComparer.Ordinal);

    private readonly string _source;
    private readonly bool _trimBlockWhitespace;
    private readonly StringBuilder _buffer = new();
    private readonly List<OpenTag> _openTags = new();
    private List<OpenTag> _bufferStartTags = new();
    private int _pos;

    private BbcodeDocumentParser(string source, bool trimBlockWhitespace)
    {
        _source = source;
        _trimBlockWhitespace = trimBlockWhitespace;
    }

    public static DocSequence Parse(string source, bool trimBlockWhitespace = true)
    {
        var parser = new BbcodeDocumentParser(source ?? "", trimBlockWhitespace);
        var root = new DocSequence();
        parser.ParseSequence(root, null);
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

    private static void ParseTag(string inner, out string name, out bool isClosing, out string value)
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

    private static StyleBox? LoadStyle(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (StyleCache.TryGetValue(path, out StyleBox? cached))
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

        StyleCache[path] = style;
        return style;
    }

    private static void ApplyMargin(DocMargin margin, string value)
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

    private static bool TryFloat(string[] parts, int index, out float value)
    {
        return float.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static void SetMargin(DocMargin margin, float value)
    {
        margin.Left = value;
        margin.Top = value;
        margin.Right = value;
        margin.Bottom = value;
    }
}
