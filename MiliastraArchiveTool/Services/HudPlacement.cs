using System.Windows;

namespace MiliastraArchiveTool.Services;

/// <summary>All bounds are screen pixels, never a mixture of pixels and WPF DIPs.</summary>
public static class HudPlacement
{
    public static Rect? Find(Rect game, IReadOnlyList<Rect> workAreas, Size size, double margin = 12)
    {
        if (game.IsEmpty || game.Width <= 0 || game.Height <= 0 || size.Width <= 0 || size.Height <= 0)
            return null;
        foreach (var area in workAreas)
        {
            if (area.Width < size.Width + 2 * margin || area.Height < size.Height + 2 * margin)
                continue;
            var x = Math.Clamp(game.Left + margin, area.Left + margin, area.Right - size.Width - margin);
            var y = Math.Clamp(game.Top + margin, area.Top + margin, area.Bottom - size.Height - margin);
            var candidates = new[]
            {
                new Point(game.Right + margin, y), new Point(game.Left - size.Width - margin, y),
                new Point(x, game.Top - size.Height - margin), new Point(x, game.Bottom + margin),
                new Point(area.Left + margin, area.Top + margin),
                new Point(area.Right - size.Width - margin, area.Top + margin),
                new Point(area.Left + margin, area.Bottom - size.Height - margin),
                new Point(area.Right - size.Width - margin, area.Bottom - size.Height - margin)
            };
            foreach (var point in candidates)
            {
                var candidate = new Rect(point, size);
                if (area.Contains(candidate) && !candidate.IntersectsWith(game))
                    return candidate;
            }
        }
        return null;
    }
}
