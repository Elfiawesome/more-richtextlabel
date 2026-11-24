using Godot;
using MoreRichTextLabelLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

public partial class Benchmark : Control
{
    private const float Width = 560.0f;

    private GDScript _gdScript = null!;
    private MoreRichTextLabel _cs = null!;
    private Control _gd = null!;
    private TextServerRichTextLabel _ts = null!;
    private readonly Dictionary<string, object> _results = new();

    public override async void _Ready()
    {
        _cs = new MoreRichTextLabel { Position = Vector2.Zero, Size = new Vector2(Width, 4000) };
        AddChild(_cs);

        _gdScript = GD.Load<GDScript>("res://gdscript/more_rich_text_label_gd.gd");
        _gd = (Control)_gdScript.New();
        _gd.Position = Vector2.Zero;
        _gd.Size = new Vector2(Width, 4000);
        AddChild(_gd);

        _ts = new TextServerRichTextLabel { Position = Vector2.Zero, Size = new Vector2(Width, 4000) };
        AddChild(_ts);

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        GD.Print("=== MoreRichTextLabel benchmark ===");
        GD.Print($"width={Width}, engine={Engine.GetVersionInfo()["string"]}");

        MeasureBuild(1, 80);
        MeasureBuild(20, 20);
        MeasureBuild(80, 6);
        MeasureResize(40, 120);
        MeasureNodesAndHeights(40);
        CheckWidths(40);

        string json = JsonSerializer.Serialize(_results, new JsonSerializerOptions { WriteIndented = true });
        string path = ProjectSettings.GlobalizePath("res://bench/results/benchmark.json");
        File.WriteAllText(path, json);
        GD.Print("Wrote " + path);

        GetTree().Quit();
    }

    private void MeasureBuild(int repeats, int iterations)
    {
        string docA = BuildDocument(repeats);

        WarmupBuild(docA);
        var cs = new List<double>();
        var gd = new List<double>();
        var ts = new List<double>();

        for (int i = 0; i < iterations; i++)
        {
            cs.Add(TimeMs(() => _cs.SetTextForced(docA)));
            gd.Add(TimeMs(() => _gd.Call("set_markup", docA)));
            ts.Add(TimeMs(() => _ts.SetTextForced(docA)));
        }

        ReportBuild($"build repeats={repeats}", cs, gd, ts, iterations);
    }

    private void WarmupBuild(string doc)
    {
        _cs.SetTextForced(doc);
        _gd.Call("set_markup", doc);
        _ts.Text = doc;
        _cs.ForceRelayoutAtWidth(Width);
        _gd.Call("force_relayout_at_width", Width);
        _ts.ForceRelayoutAtWidth(Width);
    }

    private void MeasureResize(int repeats, int iterations)
    {
        string doc = BuildDocument(repeats);
        _cs.SetTextForced(doc);
        _gd.Call("set_markup", doc);
        _ts.Text = doc;
        _cs.ForceRelayoutAtWidth(Width);
        _gd.Call("force_relayout_at_width", Width);
        _ts.ForceRelayoutAtWidth(Width);

        var cs = new List<double>();
        var gd = new List<double>();
        var ts = new List<double>();

        for (int i = 0; i < iterations; i++)
        {
            float width = (i & 1) == 0 ? 400.0f : Width;

            cs.Add(TimeMs(() => _cs.ForceRelayoutAtWidth(width)));
            gd.Add(TimeMs(() => _gd.Call("force_relayout_at_width", width)));
            ts.Add(TimeMs(() => _ts.ForceRelayoutAtWidth(width)));
        }

        ReportBuild($"relayout repeats={repeats}", cs, gd, ts, iterations);
    }

    private void CheckWidths(int repeats)
    {
        string doc = BuildDocument(repeats);
        _cs.SetTextForced(doc);
        _gd.Call("set_markup", doc);
        _ts.SetTextForced(doc);

        GD.Print("--- height parity by width ---");
        var rows = new Dictionary<string, object>();

        foreach (float width in new[] { 560.0f, 400.0f, 300.0f })
        {
            _cs.ForceRelayoutAtWidth(width);
            _gd.Call("force_relayout_at_width", width);
            _ts.ForceRelayoutAtWidth(width);

            float csHeight = _cs.ContentHeight;
            float gdHeight = _gd.GetCombinedMinimumSize().Y;
            float tsHeight = _ts.ContentHeight;
            GD.Print($"width={width,3}: csharp={csHeight,8:0.##} gdscript={gdHeight,8:0.##} textserver={tsHeight,8:0.##}");
            rows[width.ToString("0")] = new Dictionary<string, float>
            {
                ["csharp_leaf"] = csHeight,
                ["gdscript_leaf"] = gdHeight,
                ["csharp_textserver"] = tsHeight,
            };
        }

        _results["height_parity"] = rows;
    }

    private void MeasureNodesAndHeights(int repeats)
    {
        string doc = BuildDocument(repeats);
        _cs.SetTextForced(doc);
        _gd.Call("set_markup", doc);
        _ts.Text = doc;
        _cs.ForceRelayoutAtWidth(Width);
        _gd.Call("force_relayout_at_width", Width);
        _ts.ForceRelayoutAtWidth(Width);

        var counts = new Dictionary<string, int>
        {
            ["csharp_leaf"] = CountNodes(_cs),
            ["gdscript_leaf"] = CountNodes(_gd),
            ["csharp_textserver"] = CountNodes(_ts),
        };

        var heights = new Dictionary<string, float>
        {
            ["csharp_leaf"] = _cs.ContentHeight,
            ["gdscript_leaf"] = _gd.GetCombinedMinimumSize().Y,
            ["csharp_textserver"] = _ts.ContentHeight,
        };

        _results["nodes"] = counts;
        _results["content_height"] = heights;

        GD.Print("--- nodes / height ---");
        foreach (var pair in counts)
        {
            GD.Print($"{pair.Key}: nodes={pair.Value}, height={heights[pair.Key]:0.##}");
        }
    }

    private void ReportBuild(string label, List<double> cs, List<double> gd, List<double> ts, int iterations)
    {
        PrintStats(label, "csharp_leaf", cs);
        PrintStats(label, "gdscript_leaf", gd);
        PrintStats(label, "csharp_textserver", ts);

        _results[label] = new Dictionary<string, object>
        {
            ["iterations"] = iterations,
            ["csharp_leaf"] = Stats(cs),
            ["gdscript_leaf"] = Stats(gd),
            ["csharp_textserver"] = Stats(ts),
        };
    }

    private static void PrintStats(string label, string impl, List<double> samples)
    {
        StatsData stats = Stats(samples);
        GD.Print($"{label,-24} {impl,-18} median={stats.Median,8:0.000} ms  mean={stats.Mean,8:0.000} ms  min={stats.Min,8:0.000} ms");
    }

    private static StatsData Stats(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return new StatsData
        {
            Median = Percentile(sorted, 0.5),
            Mean = sorted.Average(),
            Min = sorted.First(),
            Max = sorted.Last(),
        };
    }

    private static double Percentile(List<double> sorted, double percentile)
    {
        if (sorted.Count == 0)
        {
            return 0.0;
        }

        int index = (int)Math.Round((sorted.Count - 1) * percentile);
        return sorted[index];
    }

    private static double TimeMs(Action action)
    {
        ulong start = Time.GetTicksUsec();
        action();
        ulong end = Time.GetTicksUsec();
        return (end - start) / 1000.0;
    }

    private static int CountNodes(Node node)
    {
        int count = 1;

        // Do not count internal scrollbars/context menus owned by each RichTextLabel.
        if (node is RichTextLabel)
        {
            return count;
        }

        foreach (Node child in node.GetChildren(true))
        {
            count += CountNodes(child);
        }

        return count;
    }

    private static string BuildDocument(int repeats)
    {
        var builder = new StringBuilder(repeats * 300);

        for (int i = 0; i < repeats; i++)
        {
            builder.Append("[panel=res://stylebox/black_red.tres][margin=10]");
            builder.Append("[img=64x64]res://nine_slice_small_3x3.svg[/img] ");
            builder.Append("Keyword with [b]Icon[/b] that is fit within the margins and panel. ");
            builder.Append("This extra sentence makes the text long enough to wrap and forces the text engine to lay out multiple lines. ");
            builder.Append("[/margin][/panel]");
            builder.Append("And the description afterwards is a separate text block. ");
            builder.Append("[panel=res://stylebox/pink_white_cornered.tres]");
            builder.Append("This panel afterwards is self contained and its height is not affected by previous panels. ");
            builder.Append("[i]Italic[/i] and [b]bold[/b] text are still supported inside the panel.");
            builder.Append("[/panel]");
        }

        return builder.ToString();
    }

    private sealed class StatsData
    {
        public double Median { get; set; }
        public double Mean { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
    }
}
