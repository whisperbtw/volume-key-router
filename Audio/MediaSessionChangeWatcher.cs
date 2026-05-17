using Windows.Foundation;
using Windows.Media.Control;

namespace VolumeKeyRouter;

internal sealed class MediaSessionChangeWatcher : IDisposable
{
    private readonly Func<Task> onChangedAsync;
    private readonly object gate = new();
    private readonly Dictionary<GlobalSystemMediaTransportControlsSession, SessionSubscription> subscriptions = new();
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSessionManager, CurrentSessionChangedEventArgs> currentSessionChangedHandler;
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSessionManager, SessionsChangedEventArgs> sessionsChangedHandler;
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private CancellationTokenSource? signalDelay;
    private bool ready;
    private bool disposed;

    public MediaSessionChangeWatcher(Func<Task> onChangedAsync)
    {
        this.onChangedAsync = onChangedAsync;
        currentSessionChangedHandler = (_, _) =>
        {
            SyncSessionSubscriptions();
            QueueChangedSignal();
        };
        sessionsChangedHandler = (_, _) =>
        {
            SyncSessionSubscriptions();
            QueueChangedSignal();
        };
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        manager = await GlobalSystemMediaTransportControlsSessionManager
            .RequestAsync()
            .AsTask(cancellationToken);

        manager.CurrentSessionChanged += currentSessionChangedHandler;
        manager.SessionsChanged += sessionsChangedHandler;
        SyncSessionSubscriptions();

        await Task.Delay(750, cancellationToken);
        ready = true;
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            signalDelay?.Cancel();
            signalDelay?.Dispose();
            signalDelay = null;
        }

        if (manager is not null)
        {
            manager.CurrentSessionChanged -= currentSessionChangedHandler;
            manager.SessionsChanged -= sessionsChangedHandler;
        }

        foreach (var item in subscriptions)
        {
            item.Key.MediaPropertiesChanged -= item.Value.MediaPropertiesChangedHandler;
            item.Key.PlaybackInfoChanged -= item.Value.PlaybackInfoChangedHandler;
        }

        subscriptions.Clear();
    }

    private void SyncSessionSubscriptions()
    {
        lock (gate)
        {
            if (manager is null || disposed)
            {
                return;
            }

            var activeSessions = new HashSet<GlobalSystemMediaTransportControlsSession>(manager.GetSessions());
            var staleSessions = subscriptions.Keys
                .Where(session => !activeSessions.Contains(session))
                .ToArray();

            foreach (var session in staleSessions)
            {
                session.MediaPropertiesChanged -= subscriptions[session].MediaPropertiesChangedHandler;
                session.PlaybackInfoChanged -= subscriptions[session].PlaybackInfoChangedHandler;
                subscriptions.Remove(session);
            }

            foreach (var session in activeSessions)
            {
                if (subscriptions.ContainsKey(session))
                {
                    continue;
                }

                var mediaPropertiesChangedHandler = new TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs>(
                    (_, _) => QueueChangedSignal());
                var playbackInfoChangedHandler = new TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs>(
                    (_, _) => QueueChangedSignal());

                subscriptions[session] = new SessionSubscription(
                    mediaPropertiesChangedHandler,
                    playbackInfoChangedHandler);
                session.MediaPropertiesChanged += mediaPropertiesChangedHandler;
                session.PlaybackInfoChanged += playbackInfoChangedHandler;
            }
        }
    }

    private void QueueChangedSignal()
    {
        if (!ready || disposed)
        {
            return;
        }

        CancellationTokenSource delay;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            signalDelay?.Cancel();
            signalDelay?.Dispose();
            signalDelay = new CancellationTokenSource();
            delay = signalDelay;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(350, delay.Token);
                await onChangedAsync();
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
        });
    }

    private sealed record SessionSubscription(
        TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs> MediaPropertiesChangedHandler,
        TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> PlaybackInfoChangedHandler);
}
