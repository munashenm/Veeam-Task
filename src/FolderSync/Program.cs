using System.Runtime.InteropServices;

namespace FolderSync;

/// <summary>
/// Parses the command line, then synchronizes on a fixed interval until Ctrl+C or SIGTERM.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var parsed = ArgumentParser.Parse(args);
        if (parsed.HelpRequested)
        {
            Console.WriteLine(ArgumentParser.Usage);
            return 0;
        }

        if (!parsed.Success || parsed.Options is null)
        {
            Console.Error.WriteLine(parsed.Error ?? "Invalid arguments.");
            Console.Error.WriteLine();
            Console.Error.WriteLine(ArgumentParser.Usage);
            return 1;
        }

        var options = parsed.Options;
        SyncLogger logger;
        try
        {
            logger = new SyncLogger(options.LogFilePath);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            Console.Error.WriteLine($"Cannot open log file '{options.LogFilePath}': {exception.Message}");
            return 1;
        }

        using (logger)
            return await RunAsync(options, logger);
    }

    private static async Task<int> RunAsync(SyncOptions options, SyncLogger logger)
    {
        using var cancellation = new CancellationTokenSource();
        var stopRequests = 0;

        // SIGINT is Ctrl+C. SIGTERM is what a service manager sends.
        // The first request finishes the current file and exits.
        // A second request is left unhandled, so the runtime terminates the process.
        void RequestStop(PosixSignalContext context)
        {
            if (Interlocked.Increment(ref stopRequests) > 1)
                return;

            context.Cancel = true;
            cancellation.Cancel();
        }

        using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, RequestStop);
        using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, RequestStop);

        var synchronizer = new FolderSynchronizer(logger);
        using var timer = new PeriodicTimer(options.Interval);
        var seconds = (int)options.Interval.TotalSeconds;

        logger.Info(
            $"Folder sync is running. Source: {options.SourcePath} Replica: {options.ReplicaPath} " +
            $"Interval: {seconds}s Log: {options.LogFilePath}");

        // The loop awaits each pass before waiting for the next tick, so passes do not overlap.
        // FolderSynchronizer also refuses a re-entrant call if a future change adds another trigger.
        var stopRequested = false;
        try
        {
            do
            {
                try
                {
                    synchronizer.Synchronize(options.SourcePath, options.ReplicaPath, cancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    stopRequested = true;
                    break;
                }
                catch (Exception exception)
                {
                    logger.Error($"Sync failed unexpectedly: {exception.Message}");
                }
            }
            while (await timer.WaitForNextTickAsync(cancellation.Token));
        }
        catch (OperationCanceledException)
        {
            stopRequested = true;
        }

        if (stopRequested)
            logger.Info("Stop requested.");

        logger.Info("Folder sync stopped.");
        return 0;
    }
}
