using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using Microsoft.Win32;

namespace VolumeKeyRouter;

internal static class Program
{
    public const string AppDisplayName = "Volume Key Router";
    public const string AppExecutableName = "volume-key-router.exe";
    private const string SingleInstanceMutexName = @"Local\VolumeKeyRouter.SingleInstance";
    private const string SingleInstanceEventName = @"Local\VolumeKeyRouter.ActivateMainWindow";
    private const string SingleInstanceShutdownEventName = @"Local\VolumeKeyRouter.ShutdownMainWindow";

    [STAThread]
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            ConsoleHost.EnsureConsole();
            Console.Error.WriteLine("Este utilitario usa APIs nativas de audio/teclado do Windows.");
            return 1;
        }

        if (args.Any(arg => arg.Equals("--shutdown-existing", StringComparison.OrdinalIgnoreCase)))
        {
            SignalExistingInstanceShutdown();
            return 0;
        }

        var startMinimized = args.Any(IsStartMinimizedArg);
        var isUiLaunch = args.Length == 0 ||
            startMinimized ||
            args.Any(arg => arg.Equals("--ui", StringComparison.OrdinalIgnoreCase));
        if (isUiLaunch)
        {
            using var singleInstance = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
            if (!createdNew)
            {
                if (!startMinimized)
                {
                    SignalExistingInstance();
                }

                return 0;
            }

            using var activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, SingleInstanceEventName);
            using var shutdownEvent = new EventWaitHandle(false, EventResetMode.AutoReset, SingleInstanceShutdownEventName);
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            var app = new WpfApplication
            {
                ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose
            };
            app.Run(new MainWindow(activationEvent, shutdownEvent, startMinimized));
            return 0;
        }

        ConsoleHost.EnsureConsole();
        return RunCli(args);
    }

    private static bool IsStartMinimizedArg(string arg)
    {
        return arg.Equals("--start-minimized", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("--tray", StringComparison.OrdinalIgnoreCase);
    }

    private static void SignalExistingInstance()
    {
        try
        {
            using var activationEvent = EventWaitHandle.OpenExisting(SingleInstanceEventName);
            activationEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // Older builds only listened for the window message fallback below.
        }
        catch (UnauthorizedAccessException)
        {
            // Fall through to the message fallback.
        }

        NativeMethods.PostMessage(
            NativeMethods.HwndBroadcast,
            NativeMethods.WmShowExistingApp,
            IntPtr.Zero,
            IntPtr.Zero);
    }

    private static void SignalExistingInstanceShutdown()
    {
        try
        {
            using var shutdownEvent = EventWaitHandle.OpenExisting(SingleInstanceShutdownEventName);
            shutdownEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // Builds before the WPF migration do not expose the shutdown event.
        }
        catch (UnauthorizedAccessException)
        {
            // The publish script still has process-level fallbacks for older builds.
        }
    }

    private static int RunCli(string[] args)
    {
        Options options;
        try
        {
            options = Options.Parse(args.Where(arg => !arg.Equals("--cli", StringComparison.OrdinalIgnoreCase)).ToArray());
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            Options.PrintUsage();
            return 2;
        }

        if (options.ShowHelp)
        {
            Options.PrintUsage();
            return 0;
        }

        using var audioManager = new AudioManager();

        if (options.ListDevices)
        {
            foreach (var device in audioManager.ListOutputDevices())
            {
                var marker = device.IsDefault ? " padrao" : string.Empty;
                Console.WriteLine($"- {device.Name}{marker}");
                Console.WriteLine($"  Id: {device.Id}");
            }

            return 0;
        }

        if (options.ListSessions)
        {
            foreach (var device in audioManager.ListOutputDevices())
            {
                Console.WriteLine(device.IsDefault ? $"{device.Name} (padrao)" : device.Name);
                var sessions = audioManager.ListSessions(device.Id);
                if (sessions.Count == 0)
                {
                    Console.WriteLine("  nenhuma sessao");
                    continue;
                }

                foreach (var session in sessions)
                {
                    Console.WriteLine(
                        $"  - PID={session.ProcessId} Processo={session.ProcessName} Estado={session.State} Volume={session.Volume:P0}");

                    if (!string.IsNullOrWhiteSpace(session.DisplayName))
                    {
                        Console.WriteLine($"    DisplayName: {session.DisplayName}");
                    }

                    if (!string.IsNullOrWhiteSpace(session.SessionIdentifier))
                    {
                        Console.WriteLine($"    SessionId: {session.SessionIdentifier}");
                    }
                }
            }

            return 0;
        }

        var defaultDevice = audioManager.ListOutputDevices().FirstOrDefault(device => device.IsDefault);
        if (defaultDevice is null)
        {
            Console.Error.WriteLine("Nenhum dispositivo de saida padrao encontrado.");
            return 1;
        }

        var mainThreadId = NativeMethods.GetCurrentThreadId();
        using var queue = new BlockingCollection<VolumeCommand>(new ConcurrentQueue<VolumeCommand>());
        using var cancellation = new CancellationTokenSource();

        var worker = Task.Run(() =>
        {
            var lastMissingLog = DateTimeOffset.MinValue;
            try
            {
                foreach (var command in queue.GetConsumingEnumerable(cancellation.Token))
                {
                    var result = audioManager.ApplyToSessions(
                        defaultDevice.Id,
                        new SessionTarget(
                            null,
                            null,
                            null,
                            options.ProcessNames,
                            options.ProcessIds),
                        options.Step,
                        options.Minimum,
                        options.Maximum,
                        command);

                    if (result.ChangedSessions > 0)
                    {
                        if (result.IsMuteCommand)
                        {
                            Console.WriteLine(
                                $"{DateTime.Now:HH:mm:ss} {result.TargetLabel}: {(result.IsMuted ? "mutado" : "desmutado")} ({result.ChangedSessions} sessao/s)");
                        }
                        else
                        {
                            Console.WriteLine(
                                $"{DateTime.Now:HH:mm:ss} {result.TargetLabel}: {result.Before:P0} -> {result.After:P0} ({result.ChangedSessions} sessao/s)");
                        }

                        continue;
                    }

                    if (DateTimeOffset.UtcNow - lastMissingLog > TimeSpan.FromSeconds(3))
                    {
                        Console.WriteLine($"{DateTime.Now:HH:mm:ss} nenhuma sessao de audio encontrada para {options.TargetDescription}.");
                        lastMissingLog = DateTimeOffset.UtcNow;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Erro ao ajustar volume: {ex.Message}");
            }
        });

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
            queue.CompleteAdding();
            NativeMethods.PostThreadMessage(mainThreadId, NativeMethods.WmQuit, IntPtr.Zero, IntPtr.Zero);
        };

        using var hook = new VolumeKeyHook(
            command =>
            {
                if (!queue.IsAddingCompleted)
                {
                    queue.TryAdd(command);
                }

                return true;
            },
            () => true);

        hook.Install();

        Console.WriteLine("volume-key-router ativo.");
        Console.WriteLine($"Alvo: {options.TargetDescription}");
        Console.WriteLine($"Dispositivo: {defaultDevice.Name}");
        Console.WriteLine($"Passo: {options.Step:P0}");
        Console.WriteLine("Use Fn+F2/F3 ou as teclas de volume do teclado. Ctrl+C encerra.");
        Console.WriteLine();

        RunMessageLoop();

        cancellation.Cancel();
        queue.CompleteAdding();
        worker.Wait(TimeSpan.FromSeconds(2));

        return 0;
    }

    private static void RunMessageLoop()
    {
        while (true)
        {
            var result = NativeMethods.GetMessage(out var message, IntPtr.Zero, 0, 0);
            if (result == -1 || result == 0)
            {
                break;
            }

            NativeMethods.TranslateMessage(ref message);
            NativeMethods.DispatchMessage(ref message);
        }
    }
}
