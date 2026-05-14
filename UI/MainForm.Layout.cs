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
    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            RowCount = 5,
            ColumnCount = 1
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var title = new Label
        {
            AutoSize = true,
            Text = "Escolha o app ou a linha de saida que as teclas de volume vao controlar",
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 10)
        };
        root.Controls.Add(title, 0, 0);

        var controls = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ColumnCount = 4,
            RowCount = 3,
            Margin = new Padding(0, 0, 0, 12)
        };
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.Controls.Add(controls, 0, 1);

        controls.Controls.Add(new Label
        {
            Text = "Dispositivo de saida",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 3, 10, 8)
        }, 0, 0);

        deviceCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        deviceCombo.DrawMode = DrawMode.OwnerDrawFixed;
        deviceCombo.ItemHeight = 24;
        deviceCombo.Dock = DockStyle.Fill;
        deviceCombo.Margin = new Padding(0, 0, 10, 8);
        deviceCombo.DrawItem += DrawDarkComboItem;
        deviceCombo.SelectedIndexChanged += (_, _) =>
        {
            if (!refreshing)
            {
                CancelSavedTargetSearchFromUser();
                RefreshSessions();
            }
        };
        controls.Controls.Add(deviceCombo, 1, 0);

        refreshButton.Text = "Atualizar";
        refreshButton.AutoSize = true;
        refreshButton.Margin = new Padding(0, 0, 10, 8);
        refreshButton.Click += (_, _) => RefreshDevices();
        controls.Controls.Add(refreshButton, 2, 0);

        captureButton.AutoSize = true;
        captureButton.Margin = new Padding(0, 0, 0, 8);
        captureButton.Click += (_, _) =>
        {
            ToggleCapture();
        };
        controls.Controls.Add(captureButton, 3, 0);

        var modePanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 8)
        };
        controls.SetColumnSpan(modePanel, 2);
        controls.Controls.Add(modePanel, 1, 1);

        appModeButton.Text = "App selecionado";
        appModeButton.AutoSize = true;
        appModeButton.Checked = true;
        appModeButton.CheckedChanged += (_, _) =>
        {
            CancelSavedTargetSearchFromUser();
            UpdateTargetSnapshot();
        };
        modePanel.Controls.Add(appModeButton);

        deviceModeButton.Text = "Linha/dispositivo selecionado";
        deviceModeButton.AutoSize = true;
        deviceModeButton.Margin = new Padding(18, 3, 3, 3);
        deviceModeButton.CheckedChanged += (_, _) =>
        {
            CancelSavedTargetSearchFromUser();
            UpdateTargetSnapshot();
        };
        modePanel.Controls.Add(deviceModeButton);

        var stepPanel = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(10, 0, 0, 8),
            Anchor = AnchorStyles.Right
        };
        stepPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        stepPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        stepPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        controls.SetColumnSpan(stepPanel, 2);
        controls.Controls.Add(stepPanel, 2, 1);

        stepPanel.Controls.Add(new Label
        {
            Text = "Passo",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 4, 8, 0)
        }, 0, 0);

        stepPicker.Minimum = 1;
        stepPicker.Maximum = 50;
        stepPicker.Value = 5;
        stepPicker.Width = 58;
        stepPicker.TextAlign = HorizontalAlignment.Right;
        stepPicker.Margin = new Padding(0, 0, 4, 0);
        stepPicker.ValueChanged += (_, _) =>
        {
            UpdateStepValueLabel();
            UpdateTargetSnapshot();
        };
        stepPanel.Controls.Add(stepPicker, 1, 0);

        stepValueLabel.Text = "%";
        stepValueLabel.Width = 18;
        stepValueLabel.TextAlign = ContentAlignment.MiddleLeft;
        stepValueLabel.Anchor = AnchorStyles.Left;
        stepValueLabel.Margin = new Padding(0, 4, 0, 0);
        stepPanel.Controls.Add(stepValueLabel, 2, 0);

        controls.Controls.Add(new Label
        {
            Text = "Quanto cada tecla altera o volume",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 0, 4)
        }, 1, 2);

        targetLabel.AutoSize = true;
        targetLabel.Dock = DockStyle.Fill;
        targetLabel.Margin = new Padding(0, 0, 0, 4);
        controls.SetColumnSpan(targetLabel, 3);
        controls.Controls.Add(targetLabel, 1, 2);

        var optionPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 0, 0, 10)
        };
        root.Controls.Add(optionPanel, 0, 2);

        startWithWindowsBox.Text = "Iniciar com Windows";
        startWithWindowsBox.AutoSize = true;
        startWithWindowsBox.Margin = new Padding(0, 3, 18, 3);
        startWithWindowsBox.CheckedChanged += (_, _) =>
        {
            if (suppressSettingsSave)
            {
                return;
            }

            try
            {
                StartupManager.SetEnabled(startWithWindowsBox.Checked, startMinimizedBox.Checked);
                settings.StartWithWindows = startWithWindowsBox.Checked;
                SaveSettings();
                SetStatus(startWithWindowsBox.Checked
                    ? "Inicializacao com Windows ativada."
                    : "Inicializacao com Windows desativada.");
            }
            catch (Exception ex)
            {
                suppressSettingsSave = true;
                startWithWindowsBox.Checked = StartupManager.IsEnabled();
                suppressSettingsSave = false;
                SetStatus($"Nao consegui alterar a inicializacao: {ex.Message}");
            }
        };
        optionPanel.Controls.Add(startWithWindowsBox);

        minimizeToTrayBox.Text = "Minimizar para tray";
        minimizeToTrayBox.AutoSize = true;
        minimizeToTrayBox.Margin = new Padding(0, 3, 18, 3);
        minimizeToTrayBox.CheckedChanged += (_, _) =>
        {
            if (suppressSettingsSave)
            {
                return;
            }

            settings.MinimizeToTray = minimizeToTrayBox.Checked;
            SaveSettings();
        };
        optionPanel.Controls.Add(minimizeToTrayBox);

        startMinimizedBox.Text = "Iniciar minimizado com Windows";
        startMinimizedBox.AutoSize = true;
        startMinimizedBox.Margin = new Padding(0, 3, 18, 3);
        startMinimizedBox.CheckedChanged += (_, _) =>
        {
            if (suppressSettingsSave)
            {
                return;
            }

            settings.StartMinimized = startMinimizedBox.Checked;
            SaveSettings();
            if (startWithWindowsBox.Checked)
            {
                try
                {
                    StartupManager.SetEnabled(true, startMinimizedBox.Checked);
                }
                catch (Exception ex)
                {
                    SetStatus($"Nao consegui atualizar a inicializacao: {ex.Message}");
                }
            }
        };
        optionPanel.Controls.Add(startMinimizedBox);

        showOverlayBox.Text = "Mostrar overlay de volume";
        showOverlayBox.AutoSize = true;
        showOverlayBox.Margin = new Padding(0, 3, 18, 3);
        showOverlayBox.CheckedChanged += (_, _) =>
        {
            if (suppressSettingsSave)
            {
                return;
            }

            settings.ShowVolumeOverlay = showOverlayBox.Checked;
            SaveSettings();
        };
        optionPanel.Controls.Add(showOverlayBox);

        sessionList.View = View.Details;
        sessionList.OwnerDraw = true;
        sessionList.FullRowSelect = true;
        sessionList.HideSelection = false;
        sessionList.MultiSelect = false;
        sessionList.Dock = DockStyle.Fill;
        sessionList.DrawColumnHeader += DrawDarkListHeader;
        sessionList.DrawItem += (_, eventArgs) => eventArgs.DrawDefault = false;
        sessionList.DrawSubItem += DrawDarkListSubItem;
        sessionList.Columns.Add("App", 180);
        sessionList.Columns.Add("PID", 80);
        sessionList.Columns.Add("Estado", 90);
        sessionList.Columns.Add("Volume", 80);
        sessionList.Columns.Add("Sessao", 480);
        sessionList.SelectedIndexChanged += (_, _) =>
        {
            CancelSavedTargetSearchFromUser();
            UpdateTargetSnapshot();
        };
        root.Controls.Add(sessionList, 0, 3);

        statusLabel.AutoSize = false;
        statusLabel.Height = 28;
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        statusLabel.Margin = new Padding(0, 10, 0, 0);
        root.Controls.Add(statusLabel, 0, 4);

        UpdateCaptureButton();
    }

    private void DrawDarkComboItem(object? sender, DrawItemEventArgs eventArgs)
    {
        eventArgs.DrawBackground();

        var combo = (ComboBox)sender!;
        var selected = (eventArgs.State & DrawItemState.Selected) == DrawItemState.Selected;
        var bounds = eventArgs.Bounds;
        using var background = new SolidBrush(selected ? DarkControlHover : DarkControl);
        eventArgs.Graphics.FillRectangle(background, bounds);

        if (eventArgs.Index >= 0)
        {
            var text = combo.Items[eventArgs.Index]?.ToString() ?? string.Empty;
            TextRenderer.DrawText(
                eventArgs.Graphics,
                text,
                combo.Font,
                new Rectangle(bounds.X + 8, bounds.Y, bounds.Width - 12, bounds.Height),
                DarkText,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
    }

    private void DrawDarkListHeader(object? sender, DrawListViewColumnHeaderEventArgs eventArgs)
    {
        using var background = new SolidBrush(DarkPanel);
        using var border = new Pen(DarkBorder);
        eventArgs.Graphics.FillRectangle(background, eventArgs.Bounds);
        eventArgs.Graphics.DrawRectangle(border, eventArgs.Bounds.X, eventArgs.Bounds.Y, eventArgs.Bounds.Width - 1, eventArgs.Bounds.Height - 1);
        TextRenderer.DrawText(
            eventArgs.Graphics,
            eventArgs.Header?.Text ?? string.Empty,
            sessionList.Font,
            new Rectangle(eventArgs.Bounds.X + 8, eventArgs.Bounds.Y, eventArgs.Bounds.Width - 12, eventArgs.Bounds.Height),
            MutedText,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    private void DrawDarkListSubItem(object? sender, DrawListViewSubItemEventArgs eventArgs)
    {
        var selected = eventArgs.Item?.Selected ?? false;
        var even = eventArgs.ItemIndex % 2 == 0;
        var backgroundColor = selected
            ? Color.FromArgb(35, 71, 52)
            : even
                ? Color.FromArgb(18, 21, 27)
                : Color.FromArgb(22, 25, 31);
        var textColor = selected ? Color.White : DarkText;

        using var background = new SolidBrush(backgroundColor);
        using var border = new Pen(Color.FromArgb(36, 40, 49));
        eventArgs.Graphics.FillRectangle(background, eventArgs.Bounds);
        eventArgs.Graphics.DrawLine(border, eventArgs.Bounds.Left, eventArgs.Bounds.Bottom - 1, eventArgs.Bounds.Right, eventArgs.Bounds.Bottom - 1);

        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis;
        TextRenderer.DrawText(
            eventArgs.Graphics,
            eventArgs.SubItem?.Text ?? string.Empty,
            sessionList.Font,
            new Rectangle(eventArgs.Bounds.X + 8, eventArgs.Bounds.Y, eventArgs.Bounds.Width - 12, eventArgs.Bounds.Height),
            textColor,
            flags);
    }

    private static void ApplyDarkTheme(Control control)
    {
        control.BackColor = control is TextBoxBase or ComboBox or NumericUpDown ? DarkControl : DarkBack;
        control.ForeColor = DarkText;

        switch (control)
        {
            case Form form:
                form.BackColor = DarkBack;
                form.ForeColor = DarkText;
                break;

            case Button button:
                button.FlatStyle = FlatStyle.Flat;
                button.UseVisualStyleBackColor = false;
                button.BackColor = DarkControl;
                button.ForeColor = DarkText;
                button.FlatAppearance.BorderColor = DarkBorder;
                button.FlatAppearance.MouseOverBackColor = DarkControlHover;
                button.FlatAppearance.MouseDownBackColor = DarkControlPressed;
                break;

            case ComboBox comboBox:
                comboBox.BackColor = DarkControl;
                comboBox.ForeColor = DarkText;
                comboBox.FlatStyle = FlatStyle.Flat;
                break;

            case NumericUpDown numericUpDown:
                numericUpDown.BackColor = DarkControl;
                numericUpDown.ForeColor = DarkText;
                numericUpDown.BorderStyle = BorderStyle.FixedSingle;
                break;

            case ListView listView:
                listView.BackColor = Color.FromArgb(22, 22, 26);
                listView.ForeColor = DarkText;
                listView.GridLines = true;
                listView.BorderStyle = BorderStyle.FixedSingle;
                break;

            case Label label when label.Text.StartsWith("Alvo:", StringComparison.OrdinalIgnoreCase):
                label.ForeColor = MutedText;
                break;

            case Label label:
                label.BackColor = Color.Transparent;
                label.ForeColor = label.Text.StartsWith("Quanto ", StringComparison.OrdinalIgnoreCase)
                    ? MutedText
                    : DarkText;
                break;

            case TableLayoutPanel tableLayout:
                tableLayout.BackColor = DarkBack;
                break;

            case FlowLayoutPanel:
                control.BackColor = DarkBack;
                break;

            case CheckBox checkBox:
                checkBox.FlatStyle = FlatStyle.Flat;
                checkBox.UseVisualStyleBackColor = false;
                checkBox.FlatAppearance.BorderColor = DarkBorder;
                checkBox.FlatAppearance.CheckedBackColor = DarkControl;
                checkBox.FlatAppearance.MouseOverBackColor = DarkControlHover;
                checkBox.BackColor = DarkBack;
                checkBox.ForeColor = DarkText;
                break;

            case RadioButton radioButton:
                radioButton.FlatStyle = FlatStyle.Flat;
                radioButton.UseVisualStyleBackColor = false;
                radioButton.FlatAppearance.BorderColor = DarkBorder;
                radioButton.FlatAppearance.CheckedBackColor = DarkControl;
                radioButton.FlatAppearance.MouseOverBackColor = DarkControlHover;
                control.BackColor = DarkBack;
                control.ForeColor = DarkText;
                break;
        }

        foreach (Control child in control.Controls)
        {
            ApplyDarkTheme(child);
        }
    }

    private static void ApplyDarkTheme(ToolStrip toolStrip)
    {
        toolStrip.BackColor = DarkPanel;
        toolStrip.ForeColor = DarkText;
        toolStrip.Renderer = new DarkToolStripRenderer();
        foreach (ToolStripItem item in toolStrip.Items)
        {
            item.BackColor = DarkPanel;
            item.ForeColor = DarkText;
        }
    }
}
