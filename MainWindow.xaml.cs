using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32.SafeHandles;

namespace BatteryWattWidget
{
    public partial class MainWindow : Window
    {
        private System.Windows.Forms.NotifyIcon _trayIcon = null!;
        private DispatcherTimer _timer = null!;
        private readonly WidgetConfig _config;

        public MainWindow()
        {
            InitializeComponent();

            // Load configuration
            _config = WidgetConfig.Load();

            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Visible = true,
                Text = "Battery Watt Widget — Loading...",
                Icon = RenderTextIcon("--"),
                ContextMenuStrip = BuildContextMenu()
            };

            _trayIcon.DoubleClick += (_, _) =>
            {
                _trayIcon.ShowBalloonTip(
                    3000,
                    "Battery Watt Widget",
                    _trayIcon.Text,
                    System.Windows.Forms.ToolTipIcon.Info);
            };

            UpdateBatteryReading();

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(_config.PollIntervalMs)
            };
            _timer.Tick += (_, _) => UpdateBatteryReading();
            _timer.Start();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Remove window from Alt+Tab switcher by setting WS_EX_TOOLWINDOW
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, (exStyle | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);

            this.Hide();
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_APPWINDOW = 0x00040000;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        // ───────────────────────────────────────────────
        //  Icon rendering
        // ───────────────────────────────────────────────

        private System.Drawing.Icon RenderTextIcon(string text, Color? color = null)
        {
            var textColor = color ?? _config.ColorDefault;
            int size = _config.IconSize;

            using var bitmap = new Bitmap(size, size);
            using var g = Graphics.FromImage(bitmap);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.None,
                FormatFlags = StringFormatFlags.NoWrap
            };

            float fontSize = _config.GetFontSize(text.Length);

            using var font = new Font(_config.FontFamily, fontSize, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(textColor);

            var rect = new RectangleF(0, 0, size, size);
            g.DrawString(text, font, brush, rect, sf);

            IntPtr hIcon = bitmap.GetHicon();
            var icon = System.Drawing.Icon.FromHandle(hIcon);
            var cloned = (System.Drawing.Icon)icon.Clone();
            DestroyIcon(hIcon);
            return cloned;
        }

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        // ───────────────────────────────────────────────
        //  Battery reading — update tray icon
        // ───────────────────────────────────────────────

        private void UpdateBatteryReading()
        {
            try
            {
                var (watts, state) = GetBatteryInfo();

                switch (state)
                {
                    case BatteryState.Discharging:
                        string label = watts < 10 ? $"{watts:F1}" : $"{watts:F0}";
                        _trayIcon.Icon?.Dispose();
                        _trayIcon.Icon = RenderTextIcon(label, _config.GetWattColor(watts));
                        _trayIcon.Text = $"Discharging: {watts:F1} W";
                        break;

                    case BatteryState.Charging:
                        _trayIcon.Icon?.Dispose();
                        _trayIcon.Icon = RenderTextIcon("AC", _config.ColorAc);
                        _trayIcon.Text = "On AC power (charging)";
                        break;

                    case BatteryState.Full:
                        _trayIcon.Icon?.Dispose();
                        _trayIcon.Icon = RenderTextIcon("AC", _config.ColorAc);
                        _trayIcon.Text = "On AC power (full)";
                        break;

                    default:
                        _trayIcon.Icon?.Dispose();
                        _trayIcon.Icon = RenderTextIcon("??", Color.Gray);
                        _trayIcon.Text = "Battery status unknown";
                        break;
                }
            }
            catch (Exception ex)
            {
                _trayIcon.Icon?.Dispose();
                _trayIcon.Icon = RenderTextIcon("!!", Color.Red);
                _trayIcon.Text = $"Error: {ex.Message}";
            }
        }

        private enum BatteryState { Unknown, Discharging, Charging, Full }

        // ───────────────────────────────────────────────
        //  Battery data — WMI (primary) → IOCTL (fallback)
        // ───────────────────────────────────────────────

        private (double watts, BatteryState state) GetBatteryInfo()
        {
            // Method 1: WMI BatteryStatus (root\WMI)
            // Same source as G-Helper:
            //   Get-CimInstance -Query "SELECT * FROM BatteryStatus" -Namespace "root\WMI"
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "root\\WMI",
                    "SELECT Discharging, DischargeRate, ChargeRate, PowerOnline FROM BatteryStatus WHERE Voltage > 0");

                foreach (ManagementObject obj in searcher.Get())
                {
                    bool discharging = (bool)obj["Discharging"];
                    bool powerOnline = (bool)obj["PowerOnline"];

                    if (discharging)
                    {
                        long dischargeRate = Convert.ToInt64(obj["DischargeRate"]);
                        if (dischargeRate > 0 && dischargeRate < 200000)
                            return (dischargeRate / 1000.0, BatteryState.Discharging);
                    }

                    if (powerOnline)
                    {
                        long chargeRate = Convert.ToInt64(obj["ChargeRate"]);
                        return (chargeRate / 1000.0, chargeRate > 0 ? BatteryState.Charging : BatteryState.Full);
                    }
                }
            }
            catch { }

            // Method 2: Kernel IOCTL (fallback)
            try
            {
                var result = ReadBatteryIoctl();
                if (result.HasValue)
                    return result.Value;
            }
            catch { }

            // Method 3: Win32_Battery estimation (last resort)
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Battery");

                foreach (ManagementObject obj in searcher.Get())
                {
                    ushort status = (ushort)obj["BatteryStatus"];
                    if (status != 1)
                        return (0, status >= 3 ? BatteryState.Charging : BatteryState.Full);

                    var estimatedRunTime = obj["EstimatedRunTime"];
                    var charge = obj["EstimatedChargeRemaining"];

                    if (estimatedRunTime != null && charge != null)
                    {
                        uint runTimeMin = Convert.ToUInt32(estimatedRunTime);
                        ushort chargePercent = Convert.ToUInt16(charge);

                        if (runTimeMin > 0 && runTimeMin < 71582788 && chargePercent > 0)
                        {
                            double whRemaining = _config.BatteryCapacityWh * chargePercent / 100.0;
                            double hoursRemaining = runTimeMin / 60.0;
                            return (whRemaining / hoursRemaining, BatteryState.Discharging);
                        }
                    }
                }
            }
            catch { }

            return (0, BatteryState.Unknown);
        }

        // ───────────────────────────────────────────────
        //  IOCTL_BATTERY_STATUS — kernel-level battery API
        // ───────────────────────────────────────────────

        private (double watts, BatteryState state)? ReadBatteryIoctl()
        {
            var batteryGuid = new Guid(0x72631E54, 0x78A4, 0x11D0,
                0xBC, 0xF7, 0x00, 0xAA, 0x00, 0xB7, 0xB3, 0x2A);

            IntPtr deviceInfoSet = SetupDiGetClassDevs(
                ref batteryGuid, IntPtr.Zero, IntPtr.Zero,
                DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);

            if (deviceInfoSet == INVALID_HANDLE_VALUE)
                return null;

            try
            {
                var interfaceData = new SP_DEVICE_INTERFACE_DATA();
                interfaceData.cbSize = Marshal.SizeOf(interfaceData);

                if (!SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero,
                    ref batteryGuid, 0, ref interfaceData))
                    return null;

                int requiredSize = 0;
                SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData,
                    IntPtr.Zero, 0, ref requiredSize, IntPtr.Zero);

                IntPtr detailData = Marshal.AllocHGlobal(requiredSize);
                try
                {
                    Marshal.WriteInt32(detailData, IntPtr.Size == 8 ? 8 : 6);

                    if (!SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData,
                        detailData, requiredSize, ref requiredSize, IntPtr.Zero))
                        return null;

                    string devicePath = Marshal.PtrToStringUni(detailData + 4)!;
                    return QueryBattery(devicePath);
                }
                finally
                {
                    Marshal.FreeHGlobal(detailData);
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(deviceInfoSet);
            }
        }

        private (double watts, BatteryState state)? QueryBattery(string devicePath)
        {
            using var handle = CreateFile(devicePath,
                FileAccess.ReadWrite, FileShare.ReadWrite,
                IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);

            if (handle.IsInvalid)
                return null;

            int batteryTag = 0;
            int waitTimeout = 0;
            int bytesReturned = 0;

            if (!DeviceIoControl(handle, IOCTL_BATTERY_QUERY_TAG,
                ref waitTimeout, 4, ref batteryTag, 4, ref bytesReturned, IntPtr.Zero))
                return null;

            if (batteryTag == 0)
                return null;

            var query = new BATTERY_WAIT_STATUS { BatteryTag = batteryTag, Timeout = 0 };
            var status = new BATTERY_STATUS();

            if (!DeviceIoControl(handle, IOCTL_BATTERY_QUERY_STATUS,
                ref query, Marshal.SizeOf(query),
                ref status, Marshal.SizeOf(status),
                ref bytesReturned, IntPtr.Zero))
                return null;

            int rateMilliwatts = status.Rate;

            if ((status.PowerState & BATTERY_POWER_ONLINE) != 0)
            {
                if (rateMilliwatts > 0)
                    return (rateMilliwatts / 1000.0, BatteryState.Charging);
                return (0, BatteryState.Full);
            }

            if ((status.PowerState & BATTERY_DISCHARGING) != 0)
            {
                double watts = Math.Abs(rateMilliwatts) / 1000.0;
                if (watts > 0 && watts < 200)
                    return (watts, BatteryState.Discharging);
            }

            return null;
        }

        // ───────────────────────────────────────────────
        //  Win32 P/Invoke declarations
        // ───────────────────────────────────────────────

        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
        private const int DIGCF_PRESENT = 0x02;
        private const int DIGCF_DEVICEINTERFACE = 0x10;
        private const int IOCTL_BATTERY_QUERY_TAG = 0x294040;
        private const int IOCTL_BATTERY_QUERY_STATUS = 0x29404C;
        private const int BATTERY_POWER_ONLINE = 0x00000001;
        private const int BATTERY_DISCHARGING = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BATTERY_WAIT_STATUS
        {
            public int BatteryTag;
            public int Timeout;
            public int PowerState;
            public int LowCapacity;
            public int HighCapacity;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BATTERY_STATUS
        {
            public int PowerState;
            public int Capacity;
            public int Voltage;
            public int Rate;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SetupDiGetClassDevs(
            ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, int flags);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto)]
        private static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr deviceInfoSet, IntPtr deviceInfoData,
            ref Guid interfaceClassGuid, int memberIndex,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
            IntPtr deviceInterfaceDetailData, int deviceInterfaceDetailDataSize,
            ref int requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll")]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName, [MarshalAs(UnmanagedType.U4)] FileAccess dwDesiredAccess,
            [MarshalAs(UnmanagedType.U4)] FileShare dwShareMode,
            IntPtr lpSecurityAttributes,
            [MarshalAs(UnmanagedType.U4)] FileMode dwCreationDisposition,
            int dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice, int dwIoControlCode,
            ref int lpInBuffer, int nInBufferSize,
            ref int lpOutBuffer, int nOutBufferSize,
            ref int lpBytesReturned, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice, int dwIoControlCode,
            ref BATTERY_WAIT_STATUS lpInBuffer, int nInBufferSize,
            ref BATTERY_STATUS lpOutBuffer, int nOutBufferSize,
            ref int lpBytesReturned, IntPtr lpOverlapped);

        // ───────────────────────────────────────────────
        //  Context menu
        // ───────────────────────────────────────────────

        private System.Windows.Forms.ContextMenuStrip BuildContextMenu()
        {
            var menu = new System.Windows.Forms.ContextMenuStrip();

            var refreshItem = new System.Windows.Forms.ToolStripMenuItem("Refresh Now");
            refreshItem.Click += (_, _) => UpdateBatteryReading();
            menu.Items.Add(refreshItem);

            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var reloadItem = new System.Windows.Forms.ToolStripMenuItem("Reload Config");
            reloadItem.Click += (_, _) =>
            {
                // Reload config — requires restart for full effect,
                // but we can update colors and polling live
                var newConfig = WidgetConfig.Load();
                _timer.Interval = TimeSpan.FromMilliseconds(newConfig.PollIntervalMs);
                _trayIcon.ShowBalloonTip(
                    2000,
                    "Battery Watt Widget",
                    "Config reloaded. Restart for font/size changes.",
                    System.Windows.Forms.ToolTipIcon.Info);
            };
            menu.Items.Add(reloadItem);

            var openConfigItem = new System.Windows.Forms.ToolStripMenuItem("Open Config File");
            openConfigItem.Click += (_, _) =>
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
                if (File.Exists(configPath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = configPath,
                        UseShellExecute = true
                    });
                }
                else
                {
                    _trayIcon.ShowBalloonTip(
                        3000,
                        "Battery Watt Widget",
                        "config.json not found next to the executable.",
                        System.Windows.Forms.ToolTipIcon.Warning);
                }
            };
            menu.Items.Add(openConfigItem);

            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var exitItem = new System.Windows.Forms.ToolStripMenuItem("Exit");
            exitItem.Click += (_, _) =>
            {
                _timer?.Stop();
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                Application.Current.Shutdown();
            };
            menu.Items.Add(exitItem);

            return menu;
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer?.Stop();
            _trayIcon.Visible = false;
            _trayIcon?.Dispose();
            base.OnClosed(e);
        }
    }
}
