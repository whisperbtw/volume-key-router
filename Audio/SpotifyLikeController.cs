using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace VolumeKeyRouter;

internal sealed record SpotifyLikeResult(bool Success, string Message);

// Run UI Automation outside WPF's STA. Never activate Spotify or inject global input.
internal sealed class SpotifyLikeController
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<SpotifyLikeResult> LikeAsync(string title)
    {
        if (!await gate.WaitAsync(0))
            return new(false, "Curtida já em andamento.");

        try
        {
            return await Task.Run(() => Like(title));
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or UnauthorizedAccessException
            or ElementNotAvailableException or ElementNotEnabledException or System.ComponentModel.Win32Exception)
        {
            return new(false, "Não consegui acessar o botão de curtir do Spotify.");
        }
        finally
        {
            gate.Release();
        }
    }

    private static SpotifyLikeResult Like(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return new(false, "Nenhuma música selecionada.");

        foreach (var process in Process.GetProcessesByName("Spotify"))
        {
            using (process)
            {
                var handle = process.MainWindowHandle;
                if (handle == IntPtr.Zero) continue;
                if (IsIconic(handle))
                    return new(false, "Deixe o Spotify aberto em segundo plano, sem minimizar.");

                var root = AutomationElement.FromHandle(handle);
                var bar = root.FindFirst(TreeScope.Descendants, new OrCondition(
                    new PropertyCondition(AutomationElement.NameProperty, "Barra Tocando agora"),
                    new PropertyCondition(AutomationElement.NameProperty, "Now playing bar")));
                if (bar is null || !MatchesTrack(bar, title))
                    return new(false, "A música mudou ou o player do Spotify está indisponível.");

                var button = FindButton(bar, "Adicionar a Músicas Curtidas", "Add to Liked Songs");
                if (button is null)
                {
                    var savedButton = FindButton(bar, "Adicionar à playlist", "Add to playlist");
                    return savedButton is not null
                        ? LikeFromPicker(handle, root, bar, savedButton, title)
                        : new(false, "O Spotify não expôs um botão de curtir compatível.");
                }

                if (!button.Current.IsEnabled || !MatchesTrack(bar, title))
                    return new(false, "A música mudou ou o botão está indisponível.");

                // Chromium receives input on the top-level HWND, not RenderWidgetHostHWND.
                // InvokePattern activates Spotify; these messages don't move the user's cursor.
                if (!ClickWithoutFocus(handle, button))
                    return new(false, "O Windows não entregou o comando ao Spotify.");

                // Verify with fresh elements; a sent message alone does not prove a saved track.
                for (var attempt = 0; attempt < 8; attempt++)
                {
                    Thread.Sleep(150);
                    if (!MatchesTrack(bar, title))
                        return new(false, "A música mudou durante a curtida; confira no Spotify.");
                    if (FindButton(bar, "Adicionar a Músicas Curtidas", "Add to Liked Songs") is null &&
                        FindButton(bar, "Adicionar à playlist", "Add to playlist") is not null)
                        return new(true, "Música curtida no Spotify.");
                }

                return new(false, "Comando enviado; o Spotify não confirmou a curtida.");
            }
        }

        return new(false, "Spotify desktop não encontrado.");
    }

    private static SpotifyLikeResult LikeFromPicker(IntPtr handle, AutomationElement root,
        AutomationElement bar, AutomationElement savedButton, string title)
    {
        // Saved to another playlist does not necessarily mean saved to Liked Songs.
        var choice = FindLikedChoice(root);
        if (choice is null && !ClickWithoutFocus(handle, savedButton))
            return new(false, "Não consegui acessar a lista de curtidas.");

        try
        {
            for (var attempt = 0; attempt < 8 && choice is null; attempt++)
            {
                Thread.Sleep(150);
                choice = FindLikedChoice(root);
            }

            if (!MatchesTrack(bar, title) || choice is null ||
                !choice.TryGetCurrentPattern(TogglePattern.Pattern, out var pattern))
                return new(false, "Não consegui confirmar a curtida desta música.");

            var toggle = (TogglePattern)pattern;
            if (toggle.Current.ToggleState == ToggleState.On)
                return new(true, "Música já curtida no Spotify.");
            if (toggle.Current.ToggleState != ToggleState.Off || !ClickWithoutFocus(handle, choice))
                return new(false, "Não consegui marcar a música como curtida.");

            for (var attempt = 0; attempt < 8; attempt++)
            {
                Thread.Sleep(150);
                if (!MatchesTrack(bar, title)) break;
                if (toggle.Current.ToggleState == ToggleState.On)
                    return new(true, "Música curtida no Spotify.");
            }
            return new(false, "O Spotify não confirmou a curtida.");
        }
        finally
        {
            // Close only Spotify's picker; no global Escape or keyboard state changes.
            PostMessage(handle, 0x0100, new IntPtr(0x1b), new IntPtr(1));
            PostMessage(handle, 0x0101, new IntPtr(0x1b), new IntPtr(unchecked((int)0xc0000001)));
        }
    }

    private static AutomationElement? FindLikedChoice(AutomationElement root) =>
        root.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox),
            new OrCondition(new PropertyCondition(AutomationElement.NameProperty, "Músicas curtidas"),
                new PropertyCondition(AutomationElement.NameProperty, "Liked Songs"))));

    private static bool ClickWithoutFocus(IntPtr handle, AutomationElement element)
    {
        if (IsIconic(handle) || !element.Current.IsEnabled) return false;
        var bounds = element.Current.BoundingRectangle;
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return false;
        var point = new NativePoint
        {
            X = (int)(bounds.Left + bounds.Width / 2),
            Y = (int)(bounds.Top + bounds.Height / 2)
        };
        if (!ScreenToClient(handle, ref point) || !GetClientRect(handle, out var client) ||
            point.X < 0 || point.Y < 0 || point.X >= client.Right || point.Y >= client.Bottom)
            return false;

        var coordinates = new IntPtr((point.Y << 16) | (point.X & 0xffff));
        return PostMessage(handle, 0x0201, new IntPtr(1), coordinates) &&
            PostMessage(handle, 0x0202, IntPtr.Zero, coordinates);
    }

    private static bool MatchesTrack(AutomationElement bar, string title) =>
        bar.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Hyperlink),
            new PropertyCondition(AutomationElement.NameProperty, title))) is not null;

    private static AutomationElement? FindButton(AutomationElement bar, params string[] names) =>
        bar.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
            new OrCondition(names.Select(name => (Condition)new PropertyCondition(
                AutomationElement.NameProperty, name)).ToArray())));

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr handle);
    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr handle, ref NativePoint point);
    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr handle, out NativeRect rectangle);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
}
