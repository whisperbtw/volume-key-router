using Windows.Media.Control;
using Windows.Storage.Streams;

namespace VolumeKeyRouter;

internal sealed class MediaSessionInfoProvider
{
    private const int MaxArtworkBytes = 4 * 1024 * 1024;

    public async Task<MediaTrackInfo?> GetCurrentTrackAsync(string? preferredTarget, CancellationToken cancellationToken = default)
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager
                .RequestAsync()
                .AsTask(cancellationToken);

            var sessions = manager.GetSessions();
            var orderedSessions = OrderSessions(manager.GetCurrentSession(), sessions, preferredTarget);

            foreach (var session in orderedSessions)
            {
                var properties = await session.TryGetMediaPropertiesAsync().AsTask(cancellationToken);
                var title = NullIfWhiteSpace(properties.Title);
                if (title is null)
                {
                    continue;
                }

                var artworkBytes = await ReadArtworkBytesAsync(properties.Thumbnail, cancellationToken);
                return new MediaTrackInfo(
                    title,
                    NullIfWhiteSpace(properties.Artist),
                    artworkBytes,
                    ToPlaybackState(session.GetPlaybackInfo().PlaybackStatus));
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static async Task<byte[]?> ReadArtworkBytesAsync(
        IRandomAccessStreamReference? thumbnail,
        CancellationToken cancellationToken)
    {
        if (thumbnail is null)
        {
            return null;
        }

        using var stream = await thumbnail.OpenReadAsync().AsTask(cancellationToken);
        if (stream.Size == 0 || stream.Size > MaxArtworkBytes)
        {
            return null;
        }

        var requestedLength = checked((uint)stream.Size);
        var buffer = new Windows.Storage.Streams.Buffer(requestedLength);
        var readBuffer = await stream.ReadAsync(buffer, requestedLength, InputStreamOptions.None).AsTask(cancellationToken);
        if (readBuffer.Length == 0)
        {
            return null;
        }

        var bytes = new byte[checked((int)readBuffer.Length)];
        using var reader = DataReader.FromBuffer(readBuffer);
        reader.ReadBytes(bytes);
        return bytes;
    }

    private static MediaPlaybackState ToPlaybackState(GlobalSystemMediaTransportControlsSessionPlaybackStatus status)
    {
        return status switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => MediaPlaybackState.Playing,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => MediaPlaybackState.Paused,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => MediaPlaybackState.Stopped,
            _ => MediaPlaybackState.Unknown
        };
    }

    private static IEnumerable<GlobalSystemMediaTransportControlsSession> OrderSessions(
        GlobalSystemMediaTransportControlsSession? currentSession,
        IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions,
        string? preferredTarget)
    {
        var yielded = new HashSet<GlobalSystemMediaTransportControlsSession>();
        var preferredSession = sessions.FirstOrDefault(session => MatchesTarget(session.SourceAppUserModelId, preferredTarget));

        if (preferredSession is not null && yielded.Add(preferredSession))
        {
            yield return preferredSession;
        }

        if (currentSession is not null && yielded.Add(currentSession))
        {
            yield return currentSession;
        }

        foreach (var session in sessions)
        {
            if (yielded.Add(session))
            {
                yield return session;
            }
        }
    }

    private static bool MatchesTarget(string? sourceAppUserModelId, string? preferredTarget)
    {
        var source = NormalizeForMatch(sourceAppUserModelId);
        var target = NormalizeForMatch(preferredTarget);

        return !string.IsNullOrWhiteSpace(source) &&
            !string.IsNullOrWhiteSpace(target) &&
            (source.Contains(target, StringComparison.OrdinalIgnoreCase) ||
                target.Contains(source, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeForMatch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        return normalized
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
