using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SpeedExplorer;

/// <summary>
/// Wraps image-overlay text while keeping ordinary punctuation with its word.
/// The caller provides the text measurement function so the wrapping policy can
/// be tested without a graphics device.
/// </summary>
internal static class ImageViewerOverlayTextWrapper
{
    internal static List<string> Wrap(string text, float maxWidth, Func<string, float> measureLineWidth)
    {
        ArgumentNullException.ThrowIfNull(measureLineWidth);

        var lines = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return lines;

        maxWidth = Math.Max(1f, maxWidth);
        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        foreach (string rawParagraph in normalized.Split('\n'))
        {
            string paragraph = rawParagraph.Trim();
            if (paragraph.Length == 0)
            {
                lines.Add("");
                continue;
            }

            string current = "";
            foreach (var token in Tokenize(paragraph))
            {
                string separator = !string.IsNullOrEmpty(current) && token.SpaceBefore ? " " : "";
                string candidate = current + separator + token.Text;

                // CJK closing punctuation must remain on the preceding line.  It is
                // better to exceed the target width very slightly than to draw it at
                // the beginning of a line.
                if (!string.IsNullOrEmpty(current) && token.AttachesToPrevious)
                {
                    current = candidate;
                    continue;
                }

                if (string.IsNullOrEmpty(current))
                {
                    StartLineWithToken(lines, ref current, token.Text, maxWidth, measureLineWidth);
                    continue;
                }

                if (Fits(candidate, maxWidth, measureLineWidth))
                {
                    current = candidate;
                    continue;
                }

                lines.Add(current);
                StartLineWithToken(lines, ref current, token.Text, maxWidth, measureLineWidth);
            }

            if (!string.IsNullOrEmpty(current))
                lines.Add(current);
        }

        return lines;
    }

    private static void StartLineWithToken(
        List<string> lines,
        ref string current,
        string token,
        float maxWidth,
        Func<string, float> measureLineWidth)
    {
        if (Fits(token, maxWidth, measureLineWidth))
        {
            current = token;
            return;
        }

        List<string> parts = SplitLongToken(token, maxWidth, measureLineWidth);
        if (parts.Count == 0)
        {
            // A quoted token, URL, or other unbreakable sequence is safer left
            // intact. The overlay font-fitting pass can reduce the font size for it.
            current = token;
            return;
        }

        for (int i = 0; i < parts.Count - 1; i++)
            lines.Add(parts[i]);
        current = parts[^1];
    }

    private static List<OverlayTextToken> Tokenize(string text)
    {
        var tokens = new List<OverlayTextToken>();
        var word = new StringBuilder();
        bool pendingSpace = false;
        bool wordSpaceBefore = false;
        TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            string element = enumerator.GetTextElement();
            if (string.IsNullOrEmpty(element))
                continue;

            char ch = element[0];
            if (char.IsWhiteSpace(ch))
            {
                FlushWordToken(tokens, word, wordSpaceBefore);
                wordSpaceBefore = false;
                pendingSpace = true;
                continue;
            }

            // CJK characters are meaningful break opportunities even without
            // whitespace. Western punctuation stays in its word: this binds both
            // quote marks and hyphens to the text they qualify.
            if (IsCjkTextElement(element) || IsCjkPunctuation(ch))
            {
                FlushWordToken(tokens, word, wordSpaceBefore);
                wordSpaceBefore = false;
                tokens.Add(new OverlayTextToken(element, pendingSpace, IsCjkClosingPunctuation(ch)));
                pendingSpace = false;
                continue;
            }

            if (word.Length == 0)
            {
                wordSpaceBefore = pendingSpace;
                pendingSpace = false;
            }
            word.Append(element);
        }

        FlushWordToken(tokens, word, wordSpaceBefore);
        return tokens;
    }

    private static void FlushWordToken(List<OverlayTextToken> tokens, StringBuilder word, bool spaceBefore)
    {
        if (word.Length == 0)
            return;

        tokens.Add(new OverlayTextToken(word.ToString(), spaceBefore, false));
        word.Clear();
    }

    private static List<string> SplitLongToken(string token, float maxWidth, Func<string, float> measureLineWidth)
    {
        var result = new List<string>();
        string remaining = token;
        while (!Fits(remaining, maxWidth, measureLineWidth))
        {
            int naturalBreak = FindLastFittingHyphenBreak(remaining, maxWidth, measureLineWidth);
            if (naturalBreak > 0)
            {
                result.Add(remaining[..naturalBreak]);
                remaining = remaining[naturalBreak..];
                continue;
            }

            if (!IsPlainWord(remaining) || !TrySplitPlainWord(remaining, maxWidth, measureLineWidth, out string prefix, out string suffix))
                return new List<string>();

            result.Add(prefix);
            remaining = suffix;
        }

        if (result.Count > 0)
            result.Add(remaining);
        return result;
    }

    private static int FindLastFittingHyphenBreak(string text, float maxWidth, Func<string, float> measureLineWidth)
    {
        int lastFittingBreak = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '-' || i == text.Length - 1)
                continue;

            int breakAfterHyphen = i + 1;
            if (Fits(text[..breakAfterHyphen], maxWidth, measureLineWidth))
                lastFittingBreak = breakAfterHyphen;
        }

        return lastFittingBreak;
    }

    private static bool TrySplitPlainWord(
        string text,
        float maxWidth,
        Func<string, float> measureLineWidth,
        out string prefix,
        out string suffix)
    {
        var elements = GetTextElements(text);
        prefix = "";
        suffix = "";
        if (elements.Count <= 1)
            return false;

        int splitAt = 0;
        var candidate = new StringBuilder();
        for (int i = 0; i < elements.Count - 1; i++)
        {
            candidate.Append(elements[i]);
            if (Fits(candidate + "-", maxWidth, measureLineWidth))
                splitAt = i + 1;
        }

        if (splitAt == 0)
            return false;

        prefix = string.Concat(elements.Take(splitAt)) + "-";
        suffix = string.Concat(elements.Skip(splitAt));
        return true;
    }

    private static bool IsPlainWord(string token)
        => token.Length >= 2 && token.All(char.IsLetterOrDigit) && !token.Any(IsCjkChar);

    private static List<string> GetTextElements(string text)
    {
        var elements = new List<string>();
        TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
            elements.Add(enumerator.GetTextElement());
        return elements;
    }

    private static bool Fits(string text, float maxWidth, Func<string, float> measureLineWidth)
        => measureLineWidth(text) <= maxWidth + 0.5f;

    private static bool IsCjkTextElement(string textElement)
        => textElement.Any(IsCjkChar);

    private static bool IsCjkChar(char ch)
        => ch is >= '\u3040' and <= '\u30FF'
            or >= '\u3400' and <= '\u4DBF'
            or >= '\u4E00' and <= '\u9FFF'
            or >= '\uAC00' and <= '\uD7AF';

    private static bool IsCjkClosingPunctuation(char ch)
        => "、。，．：；！？）］｝〕〉》」』】〗〙〛".IndexOf(ch) >= 0;

    private static bool IsCjkPunctuation(char ch)
        => ch is >= '\u3000' and <= '\u303F'
            or >= '\uFF00' and <= '\uFFEF';

    private readonly record struct OverlayTextToken(string Text, bool SpaceBefore, bool AttachesToPrevious);
}
