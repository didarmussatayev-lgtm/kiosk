using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KioskLocker;

public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

public partial class Form1 : Form
{
    public static Form1 Instance;
    private const bool DebugBlockedZone = true; // ← подсветка включена
    private static readonly Size CloseButtonSize = new Size(18, 18);

    // ══════════════════════════════════════════════════
    // КООРДИНАТЫ ЗАБЛОКИРОВАННОЙ ЗОНЫ
    // Измените эти значения под нужное место на экране:
    //   blockedZoneX      — отступ от левого края (пиксели)
    //   blockedZoneY      — отступ от верхнего края (пиксели)
    //   blockedZoneWidth  — ширина зоны
    //   blockedZoneHeight — высота зоны
    //
    // Чтобы найти нужные координаты — просто наведите
    // мышь на кнопку: координаты покажутся в углу экрана.
    // ══════════════════════════════════════════════════
   private const int blockedZoneX      = 1722;
private const int blockedZoneY      = 0;
private const int blockedZoneWidth  = 178;
private const int blockedZoneHeight = 75;

    Process? chromeProcess;
    string adminPassword = "1234";
    private static IntPtr _kbdHookID = IntPtr.Zero;
    private static IntPtr _mouseHookID = IntPtr.Zero;
    private readonly System.Windows.Forms.Timer watchdogTimer = new System.Windows.Forms.Timer();
    private readonly Rectangle closeButtonBounds = new Rectangle(new Point(0, 0), CloseButtonSize);
    private Rectangle blockedZone;

    // Координаты мыши для отображения на экране
    private Point _mousePos = Point.Empty;
    private readonly Rectangle _coordLabelBounds = new Rectangle(30, 0, 200, 20);

    public Form1()
    {
        Instance = this;
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.Lime;
        TransparencyKey = Color.Lime;
        Size = Screen.PrimaryScreen.Bounds.Size;
        Location = new Point(0, 0);
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        Enabled = false;

        UpdateBlockedZone();

        watchdogTimer.Interval = 1000;
        watchdogTimer.Tick += (s, e) =>
        {
            HideTaskbar();
            UpdateBlockedZone();
            Invalidate();
            if (chromeProcess != null && !chromeProcess.HasExited)
                SetWindowPos(chromeProcess.MainWindowHandle, 0, 0, 0, Screen.PrimaryScreen.Bounds.Width, Screen.PrimaryScreen.Bounds.Height, 0x0040);
        };
        watchdogTimer.Start();

        HideTaskbar();
        _kbdHookID = SetKeyboardHook(KeyboardCallback);
        _mouseHookID = SetMouseHook(MouseCallback);
        StartChrome();
    }

    void StartChrome()
    {
        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string userDataDir = Path.Combine(localAppData, @"Google\Chrome\User Data");
            string profileName = Directory.Exists(Path.Combine(userDataDir, "Profile 5")) ? "Profile 5" : "Default";
            string chromePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
            if (!File.Exists(chromePath)) chromePath = @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe";

            foreach (var p in Process.GetProcessesByName("chrome"))
            {
                try { p.Kill(); } catch { }
            }

            chromeProcess = Process.Start(new ProcessStartInfo
            {
                FileName = chromePath,
                Arguments = $"--kiosk --restore-last-session --disable-infobars --force-device-scale-factor=1 --user-data-dir=\"{userDataDir}\" --profile-directory=\"{profileName}\" \"https://kaspi.kz/mc/#/orders-new?status=NEW\" \"https://supabase.com/dashboard/project/egujoosgipgrwegzaemr/editor/17633?sort=title%3Adesc&sort=current_stock%3Adesc\"",
                UseShellExecute = true
            });
        }
        catch { }
    }

    public void ShowAdminExit()
    {
        watchdogTimer.Stop();
        using (Form prompt = new Form())
        {
            prompt.Width = 300;
            prompt.Height = 150;
            prompt.Text = "Администрирование";
            prompt.StartPosition = FormStartPosition.CenterScreen;
            prompt.TopMost = true;
            prompt.FormBorderStyle = FormBorderStyle.FixedDialog;
            prompt.ControlBox = false;

            TextBox txt = new TextBox() { Left = 20, Top = 40, Width = 240, PasswordChar = '*' };
            Button btn = new Button() { Text = "ОК", Left = 160, Top = 80, DialogResult = DialogResult.OK };
            prompt.Controls.Add(txt);
            prompt.Controls.Add(btn);
            prompt.AcceptButton = btn;
            prompt.Shown += (s, e) => { txt.Focus(); };

            if (prompt.ShowDialog() == DialogResult.OK && txt.Text == adminPassword)
            {
                UnhookWindowsHookEx(_kbdHookID);
                UnhookWindowsHookEx(_mouseHookID);
                RestoreTaskbar();
                try { chromeProcess?.Kill(); } catch { }
                Application.Exit();
            }
            else
            {
                watchdogTimer.Start();
            }
        }
    }

    private void UpdateBlockedZone()
    {
        // Зона задаётся константами вверху файла — просто меняйте их
        blockedZone = new Rectangle(blockedZoneX, blockedZoneY, blockedZoneWidth, blockedZoneHeight);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        // Кнопка × (выход через пароль)
        TextRenderer.DrawText(
            e.Graphics,
            "×",
            new Font("Segoe UI", 9, FontStyle.Bold),
            closeButtonBounds,
            Color.Black,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        // Подсветка заблокированной зоны
        if (DebugBlockedZone)
        {
            using SolidBrush debugBrush = new SolidBrush(Color.FromArgb(90, Color.Red));
            e.Graphics.FillRectangle(debugBrush, blockedZone);
            using Pen debugPen = new Pen(Color.Red, 2);
            e.Graphics.DrawRectangle(debugPen, blockedZone);
            TextRenderer.DrawText(
                e.Graphics,
                "ЗАПРЕЩЕНО",
                new Font("Segoe UI", 10, FontStyle.Bold),
                blockedZone,
                Color.DarkRed,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        // ── Координаты мыши — показываем рядом с кнопкой × ──────
        if (_mousePos != Point.Empty)
        {
            string coordText = $"X={_mousePos.X}  Y={_mousePos.Y}";

            // Фон под текстом чтобы было читаемо
            using SolidBrush bgBrush = new SolidBrush(Color.FromArgb(200, Color.Black));
            e.Graphics.FillRectangle(bgBrush, _coordLabelBounds);

            TextRenderer.DrawText(
                e.Graphics,
                coordText,
                new Font("Courier New", 9, FontStyle.Bold),
                _coordLabelBounds,
                Color.Lime,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private static IntPtr SetKeyboardHook(LowLevelKeyboardProc proc)
    {
        using (Process curProcess = Process.GetCurrentProcess())
        using (ProcessModule? curModule = curProcess.MainModule)
            return SetWindowsHookEx(13, proc, GetModuleHandle(curModule!.ModuleName), 0);
    }

    private static IntPtr SetMouseHook(LowLevelMouseProc proc)
    {
        using (Process curProcess = Process.GetCurrentProcess())
        using (ProcessModule? curModule = curProcess.MainModule)
            return SetWindowsHookEx(14, proc, GetModuleHandle(curModule!.ModuleName), 0);
    }

    private static IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            Keys key = (Keys)vkCode;
            bool isAltDown   = (GetAsyncKeyState(0x12) & 0x8000) != 0;
            bool isCtrlDown  = (GetAsyncKeyState(0x11) & 0x8000) != 0;
            bool isShiftDown = (GetAsyncKeyState(0x10) & 0x8000) != 0;

            if (key == Keys.LWin || key == Keys.RWin ||
                (isAltDown && key == Keys.Tab) ||
                (isAltDown && key == Keys.F4))
                return (IntPtr)1;

            if (isCtrlDown && (key == Keys.Add || key == Keys.Oemplus ||
                               key == Keys.Subtract || key == Keys.OemMinus))
                return (IntPtr)1;

            if (isCtrlDown && !isShiftDown && key == Keys.N)
                return (IntPtr)1;

            if (isCtrlDown && isShiftDown && key == Keys.N)
                return (IntPtr)1;

            if (isCtrlDown && key == Keys.T)
                return (IntPtr)1;
        }
        return CallNextHookEx(_kbdHookID, nCode, wParam, lParam);
    }

    private static IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            Point pt = new Point(hookStruct.pt.x, hookStruct.pt.y);

            // ── Обновляем координаты мыши на экране при каждом движении ──
            if (wParam == (IntPtr)0x0200) // WM_MOUSEMOVE
            {
                Instance.BeginInvoke(new Action(() =>
                {
                    Instance._mousePos = pt;
                    Instance.Invalidate(Instance._coordLabelBounds);
                }));
            }

            // Ctrl + колесо мыши — блокируем масштаб
            if (wParam == (IntPtr)0x020A) // WM_MOUSEWHEEL
            {
                bool isCtrlDown = (GetAsyncKeyState(0x11) & 0x8000) != 0;
                if (isCtrlDown)
                    return (IntPtr)1;
            }

            // Клик левой кнопкой
            if (wParam == (IntPtr)0x0201) // WM_LBUTTONDOWN
            {
                if (Instance.closeButtonBounds.Contains(pt))
                {
                    Instance.BeginInvoke(new Action(() => Instance.ShowAdminExit()));
                    return (IntPtr)1;
                }

                if (Instance.blockedZone.Contains(pt))
                    return (IntPtr)1;
            }
        }
        return CallNextHookEx(_mouseHookID, nCode, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int x; public int y; }
    [StructLayout(LayoutKind.Sequential)] struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public IntPtr dwExtraInfo; }

    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, int hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, Delegate lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string lpModuleName);
    [DllImport("user32.dll")] static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    void HideTaskbar() { ShowWindow(FindWindow("Shell_TrayWnd", ""), 0); }
    void RestoreTaskbar() { ShowWindow(FindWindow("Shell_TrayWnd", ""), 5); }
    protected override void OnFormClosing(FormClosingEventArgs e) { UnhookWindowsHookEx(_kbdHookID); UnhookWindowsHookEx(_mouseHookID); base.OnFormClosing(e); }
}
