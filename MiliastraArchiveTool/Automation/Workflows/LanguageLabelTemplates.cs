using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using MiliastraArchiveTool.Infrastructure;

namespace MiliastraArchiveTool.Automation.Workflows;

internal static class LanguageLabelTemplates
{
    internal sealed record Match(string Language, double X, double Y, double Score);
    internal static bool IsStableTable(IReadOnlyList<Match>? before, IReadOnlyList<Match> after, bool textDialogVisible) =>
        !textDialogVisible && before != null && after.Count > 0 &&
        after.Select(m => m.Language).SequenceEqual(before.Select(m => m.Language)) && !HasMoved(before, after);
    internal static bool HasMoved(IReadOnlyList<Match> before, IReadOnlyList<Match> after) =>
        after.Count > 0 && (after.Any(m => !before.Any(old => old.Language == m.Language)) ||
            after.Any(m => before.Any(old => old.Language == m.Language && Math.Abs(old.Y-m.Y) > .012)));
    private static readonly Dictionary<string, byte[]> Templates = Load();
    private sealed class FrameMatches
    {
        internal IReadOnlyList<Match>? Table;
        internal IReadOnlyList<Match>? Options;
    }
    private static readonly ConditionalWeakTable<BitmapSource, FrameMatches> Cache = new();
    private static readonly ConcurrentDictionary<(int Width, int Height, bool Options, string Language), int> Scales = new();
    private static Dictionary<string, byte[]> Load()
    {
        var assembly = typeof(LanguageLabelTemplates).Assembly;
        var prefix = "MiliastraArchiveTool.Automation.Workflows.Assets.languages.";
        return assembly.GetManifestResourceNames().Where(n => n.StartsWith(prefix)).ToDictionary(
            n => n[prefix.Length..^4], n => { using var stream = assembly.GetManifestResourceStream(n)!;
                using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray(); });
    }

    internal static IReadOnlyList<Match> Locate(BitmapSource bitmap, bool options = false)
    {
        // Cache only immutable snapshots; a new capture always gets a fresh result.
        if (!bitmap.IsFrozen) return LocateCore(bitmap, options);
        var cache = Cache.GetValue(bitmap, _ => new FrameMatches());
        lock (cache)
        {
            return options ? cache.Options ??= LocateCore(bitmap, true) : cache.Table ??= LocateCore(bitmap, false);
        }
    }

    private static IReadOnlyList<Match> LocateCore(BitmapSource bitmap, bool options)
    {
        using var timing = WorkflowPerformanceTrace.Measure("LanguageTemplate.Match", new { Options = options, bitmap.PixelWidth, bitmap.PixelHeight });
        var w = bitmap.PixelWidth; var h = bitmap.PixelHeight;
        var rect = options ? new System.Windows.Int32Rect((int)(w*.18),(int)(h*.15),(int)(w*.64),(int)(h*.70))
            : new System.Windows.Int32Rect((int)(w*.14),(int)(h*.22),(int)(w*.14),(int)(h*.55));
        var gray = new FormatConvertedBitmap(new CroppedBitmap(bitmap,rect),PixelFormats.Gray8,null,0);
        var bytes = new byte[gray.PixelWidth*gray.PixelHeight]; gray.CopyPixels(bytes,gray.PixelWidth,0);
        using var source = new Mat(gray.PixelHeight,gray.PixelWidth,MatType.CV_8UC1);
        System.Runtime.InteropServices.Marshal.Copy(bytes,0,source.Data,bytes.Length);
        var matches = new List<Match>();
        foreach(var (language,data) in Templates)
        {
            using var template = Cv2.ImDecode(data,ImreadModes.Grayscale);
            double best=0; Point bestPoint=default; int bestW=0,bestH=0,bestStep=0;
            var key = (w,h,options,language);
            var hasScale = Scales.TryGetValue(key, out var knownScale);
            var steps = Enumerable.Range(0,16).Select(i=>85+i*2);
            if (hasScale) steps = new[]{knownScale}.Concat(steps.Where(s=>s!=knownScale));
            foreach(var step in steps)
            {
                var scale=h/1080.0*step/100.0;
                var tw=(int)Math.Round(template.Width*scale); var th=(int)Math.Round(template.Height*scale);
                if(tw<4||th<4||tw>source.Width||th>source.Height)continue;
                using var resized=new Mat();using var result=new Mat();
                Cv2.Resize(template,resized,new Size(tw,th));
                Cv2.MatchTemplate(source,resized,result,TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result,out var min,out var max,out var minPoint,out var maxPoint);
                var score=options?Math.Max(max,-min):max;var point=options&&-min>max?minPoint:maxPoint;
                if(double.IsFinite(score)&&score>best){best=score;bestPoint=point;bestW=tw;bestH=th;bestStep=step;}
                // Revalidate the learned scale on every frame. Weak evidence still searches all scales.
                if (hasScale && step==knownScale && best>=.90) break;
            }
            if(best>=.90) Scales[key]=bestStep;
            if(best>=.82) matches.Add(new(language,(rect.X+bestPoint.X+bestW/2.0)/w,(rect.Y+bestPoint.Y+bestH/2.0)/h,best));
        }
        // Competing language labels at the same location must have a clear winner.
        return matches.Where(m=>!matches.Any(other=>other.Language!=m.Language&&Math.Abs(other.X-m.X)<.07&&
                Math.Abs(other.Y-m.Y)<.018&&other.Score>m.Score-.06))
            .OrderBy(m=>m.Y).ToArray();
    }
}
