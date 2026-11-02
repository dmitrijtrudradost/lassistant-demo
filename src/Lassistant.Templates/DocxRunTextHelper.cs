using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Lassistant.Templates;

internal static class DocxRunTextHelper
{
    public static void ReplaceSpan(List<Text> textNodes, int start, int end, string replacement)
    {
        var nodeStarts = BuildNodeStarts(textNodes);
        var affected = GetAffectedNodes(textNodes, nodeStarts, start, end);
        if (affected.Count == 0)
            return;

        var combined = string.Concat(textNodes.Select(t => t.Text ?? string.Empty));
        var first = affected[0];
        var last = affected[^1];
        var firstStart = nodeStarts[textNodes.IndexOf(first)];
        var lastStart = nodeStarts[textNodes.IndexOf(last)];
        var lastLen = last.Text?.Length ?? 0;

        var prefix = combined.Substring(firstStart, start - firstStart);
        var suffix = combined.Substring(end, lastStart + lastLen - end);

        if (affected.Count == 1)
            first.Text = prefix + replacement + suffix;
        else
        {
            first.Text = prefix + replacement;
            for (var i = 1; i < affected.Count - 1; i++)
                affected[i].Text = string.Empty;
            last.Text = suffix;
        }

        first.Space = SpaceProcessingModeValues.Preserve;
        if (affected.Count > 1)
            last.Space = SpaceProcessingModeValues.Preserve;
    }

    private static List<int> BuildNodeStarts(List<Text> textNodes)
    {
        var starts = new List<int>(textNodes.Count);
        var pos = 0;
        foreach (var node in textNodes)
        {
            starts.Add(pos);
            pos += node.Text?.Length ?? 0;
        }

        return starts;
    }

    private static List<Text> GetAffectedNodes(List<Text> textNodes, List<int> nodeStarts, int start, int end)
    {
        var affected = new List<Text>();
        for (var i = 0; i < textNodes.Count; i++)
        {
            var nodeStart = nodeStarts[i];
            var nodeEnd = nodeStart + (textNodes[i].Text?.Length ?? 0);
            if (nodeEnd <= start || nodeStart >= end)
                continue;

            affected.Add(textNodes[i]);
        }

        return affected;
    }
}
