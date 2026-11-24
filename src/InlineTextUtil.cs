using System.Collections.Generic;

namespace MoreRichTextLabelLib;

public static class InlineTextUtil
{
    public static bool CanSplit(string markup)
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

    public static List<string> SplitWords(string markup)
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
}
