namespace MiliastraArchiveTool.Automation.Recognition;

internal sealed class PageObservationStreak
{
    private GamePage? _previous;
    private int _count;

    public bool Observe(GamePage page, bool eligible, int required = 2)
    {
        _count = eligible ? (_previous == page ? _count + 1 : 1) : 0;
        _previous = eligible ? page : null;
        return _count >= required;
    }
}
