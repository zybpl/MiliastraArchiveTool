namespace MiliastraArchiveTool.Automation;

public interface IGameInputAutomationService
{
    Task ClickAsync(GameWindowInfo window, double normalizedX, double normalizedY, CancellationToken cancellationToken);

    Task MoveAsync(GameWindowInfo window, double normalizedX, double normalizedY, CancellationToken cancellationToken);

    Task PressKeyAsync(
        GameWindowInfo window,
        GameVirtualKey key,
        CancellationToken cancellationToken,
        bool allowPostMessageFallback = true,
        bool forcePostMessageFallback = false);

    Task ReplaceTextAsync(GameWindowInfo window, string text, CancellationToken cancellationToken);

    Task ReplaceMultilanguageTextAsync(GameWindowInfo window, string text, CancellationToken cancellationToken)
        => ReplaceTextAsync(window, text, cancellationToken);

    Task ScrollAsync(GameWindowInfo window, double normalizedX, double normalizedY, int wheelDelta, CancellationToken cancellationToken);
}

public interface IWindowsFileDialogAutomationService
{
    Task SelectFilesAsync(GameWindowInfo owner, IReadOnlyList<string> filePaths, TimeSpan timeout, CancellationToken cancellationToken)
        => SelectFilesAsync(filePaths, timeout, cancellationToken);

    Task SelectFileAsync(GameWindowInfo owner, string filePath, TimeSpan timeout, CancellationToken cancellationToken)
        => SelectFileAsync(filePath, timeout, cancellationToken);

    Task SelectFileAsync(string filePath, TimeSpan timeout, CancellationToken cancellationToken);

    Task SelectFilesAsync(IReadOnlyList<string> filePaths, TimeSpan timeout, CancellationToken cancellationToken);
}

public interface IGameWindowActivationService
{
    void RestoreAndActivate(GameWindowInfo window);

    ForegroundWindowInfo GetForegroundWindowInfo(GameWindowInfo target);

    bool TryRestoreAndActivate(GameWindowInfo window, out string diagnostic);
}

public interface IGameWindowFinder
{
    GameWindowInfo FindGameWindow();
}
