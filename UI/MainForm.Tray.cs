using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VolumeKeyRouter;

internal sealed partial class MainForm
{
    private void BuildTray()
    {
        trayShowItem.Text = "Abrir";
        trayShowItem.Click += (_, _) => RestoreFromTray();

        trayToggleCaptureItem.Click += (_, _) => ToggleCapture();

        var refreshItem = new ToolStripMenuItem("Atualizar", null, (_, _) =>
        {
            RestoreFromTray();
            RefreshDevices();
        });

        var exitItem = new ToolStripMenuItem("Sair", null, (_, _) => ExitApplication());

        trayMenu.Items.Add(trayShowItem);
        trayMenu.Items.Add(trayToggleCaptureItem);
        trayMenu.Items.Add(refreshItem);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(exitItem);

        trayIcon.Text = Program.AppDisplayName;
        trayIcon.Icon = appIcon;
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.Visible = true;
        trayIcon.MouseClick += (_, eventArgs) =>
        {
            if (eventArgs.Button == MouseButtons.Left)
            {
                RestoreFromTray();
            }
        };
        trayIcon.DoubleClick += (_, _) => RestoreFromTray();

        ApplyDarkTheme(trayMenu);
        UpdateTrayMenu();
    }

    private void HideToTray()
    {
        if (!minimizeToTrayBox.Checked && !startHiddenToTray)
        {
            return;
        }

        Hide();
        ShowInTaskbar = false;
        WindowState = FormWindowState.Minimized;
        trayIcon.Visible = true;
        UpdateTrayMenu();
    }

    private void RestoreFromTray()
    {
        startHiddenToTray = false;
        Show();
        ShowInTaskbar = true;
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Activate();
        TopMost = true;
        TopMost = false;
        NativeMethods.SetForegroundWindow(Handle);
        UpdateTrayMenu();
    }

    private void ExitApplication()
    {
        allowClose = true;
        SaveSettings();
        Close();
    }

    private void UpdateTrayMenu()
    {
        trayToggleCaptureItem.Text = captureActive ? "Pausar captura" : "Ativar captura";
        trayShowItem.Text = Visible && WindowState != FormWindowState.Minimized ? "Abrir" : "Mostrar janela";
    }
}
