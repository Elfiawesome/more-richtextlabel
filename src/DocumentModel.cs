using Godot;
using System.Collections.Generic;

namespace MoreRichTextLabelLib;

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
