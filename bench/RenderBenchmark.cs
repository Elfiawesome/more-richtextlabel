using Godot;
using MoreRichTextLabelLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

public partial class RenderBenchmark : Control
{
    private const int Repeats = 9;
    private static readonly Vector2I ViewportSize = new(760, 2000);
    private readonly Dictionary<string, object> _results = new();

    public override async void _Ready()
    {
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;
        string doc = BuildDocument();
        GD.Print("=== render benchmark (windowed) ===");
        GD.Print($"viewport={ViewportSize}, repeats={Repeats}");

        await Measure("csharp_leaf", new MoreRichTextLabel(), doc);
        await Measure("gdscript_leaf", CreateGdScript(), doc);
        await Measure("csharp_textserver", new TextServerRichTextLabel(), doc);

        string json = JsonSerializer.Serialize(_results, new JsonSerializerOptions { WriteIndented = true });
        string path = ProjectSettings.GlobalizePath("res://bench/results/render_benchmark.json");
        File.WriteAllText(path, json);
        GD.Print("Wrote " + path);
        GetTree().Quit();
    }

    private static Control CreateGdScript()
    {
        var script = GD.Load<GDScript>("res://gdscript/more_rich_text_label_gd.gd");
        return (Control)script.New();
    }

    private async System.Threading.Tasks.Task Measure(string name, Control impl, string doc)
    {
        var viewport = new SubViewport
        {
            Name = name,
            Size = ViewportSize,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = false,
        };

        AddChild(viewport);

        var background = new ColorRect
        {
            Color = new Color(0.12f, 0.12f, 0.14f),
            Size = new Vector2(ViewportSize.X, ViewportSize.Y),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        viewport.AddChild(background);

        impl.Position = Vector2.Zero;
        impl.Size = new Vector2(ViewportSize.X, ViewportSize.Y);
        viewport.AddChild(impl);

        if (impl is MoreRichTextLabel csharp)
        {
            csharp.SetTextForced(doc);
            csharp.ForceRelayoutAtWidth(ViewportSize.X);
        }
        else if (impl is TextServerRichTextLabel textServer)
        {
            textServer.SetTextForced(doc);
            textServer.ForceRelayoutAtWidth(ViewportSize.X);
        }
        else
        {
            impl.Call("set_markup", doc);
            impl.Call("force_relayout_at_width", (float)ViewportSize.X);
        }

        for (int i = 0; i < 12; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        Rid viewportRid = viewport.GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(viewportRid, true);

        var cpu = new List<double>();
        var gpu = new List<double>();
        var frame = new List<double>();

        for (int i = 0; i < 120; i++)
        {
            ulong start = Time.GetTicksUsec();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ulong end = Time.GetTicksUsec();

            frame.Add((end - start) / 1000.0);
            cpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewportRid));
            gpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewportRid));
        }

        _results[name] = new Dictionary<string, object>
        {
            ["frame_cpu_ms"] = Stats(frame),
            ["render_cpu_ms"] = Stats(cpu),
            ["render_gpu_ms"] = Stats(gpu),
        };

        PrintStats(name, "frame", frame);
        PrintStats(name, "render-cpu", cpu);
        PrintStats(name, "render-gpu", gpu);

        viewport.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static void PrintStats(string impl, string metric, List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        double median = Percentile(sorted, 0.5);
        double mean = sorted.Average();
        GD.Print($"{impl,-18} {metric,-12} median={median,8:0.000} ms  mean={mean,8:0.000} ms  min={sorted.First(),8:0.000} ms");
    }

    private static object Stats(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return new
        {
            median = Percentile(sorted, 0.5),
            mean = sorted.Average(),
            min = sorted.First(),
            max = sorted.Last(),
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

    private static string BuildDocument()
    {
        var builder = new System.Text.StringBuilder();

        for (int i = 0; i < Repeats; i++)
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
}
