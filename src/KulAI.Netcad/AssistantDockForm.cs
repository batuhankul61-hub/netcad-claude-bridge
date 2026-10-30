using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using KulAI.Core.UI;
using KulAI.Netcad.Tools;
using Netcad.Interop.DockManager;

namespace KulAI.Netcad
{
    /// <summary>
    /// NETCAD'in yerleştirilebilir paneli (sağ/sol/alt kenar). İçinde çekirdeğin WPF sohbet arayüzü (ChatPanel)
    /// ElementHost ile barındırılır. NETCAD, CreateForm ile paneli kendi penceresine alır, Resize ile boyutlandırır.
    /// </summary>
    public sealed class AssistantDockForm : Form, INetcadDockForm
    {
        public AssistantDockForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(0x25, 0x29, 0x30);

            Tools = new NetcadToolHost();
            Chat = new ChatPanel(Tools, NetcadPrompt.Text, NetcadPrompt.ExampleRequest, ShowSettings);
            Controls.Add(new ElementHost { Dock = DockStyle.Fill, Child = Chat });
        }

        public NetcadToolHost Tools { get; }
        public ChatPanel Chat { get; }

        private void ShowSettings()
        {
            var window = new SettingsWindow();
            // Ayarlar penceresi NETCAD'in arkasında kalmasın.
            new System.Windows.Interop.WindowInteropHelper(window).Owner = Handle;
            window.ShowDialog();
            Chat.RefreshProvider();
        }

        // ---- INetcadDockForm ----------------------------------------------------------

        public uint FormHandle { get => (uint)Handle.ToInt64(); set { } }
        public int DllInstance { get; set; }
        public string ResourceBmpName { get; set; } = "";
        public string Caption { get; set; } = "Kul AI";
        public TNCDockingType DockType { get; set; } = TNCDockingType.ncdtRight;
        public int DockPriority { get; set; }
        public bool CanClose { get; set; } = true;
        public bool CanFloat { get; set; } = true;
        public string DockTarget { get; set; } = "";
        public bool ProjectSpecific { get; set; }

        public void CreateForm(uint parentWindowHandle)
        {
            MainModule.Log($"Panel CreateForm: parent={parentWindowHandle}");
            // Üst düzey pencereyi NETCAD'in panel kabının alt penceresi yap.
            var style = GetWindowLong(Handle, GWL_STYLE);
            SetWindowLong(Handle, GWL_STYLE, (style | WS_CHILD) & ~WS_POPUP);
            SetParent(Handle, new IntPtr(parentWindowHandle));
            Visible = true;
        }

        public int FreeForm()
        {
            MainModule.Log("Panel FreeForm");
            Visible = false;
            return 0;
        }

        /// <summary>NETCAD panelin tercih ettiği konum ve boyutu sorar.</summary>
        public bool Position(out int ALeft, out int ATop, out int AWidth, out int AHeight)
        {
            ALeft = 0;
            ATop = 0;
            AWidth = Math.Max(Width, 380);
            AHeight = Math.Max(Height, 600);
            return true;
        }

        void INetcadDockForm.Resize(int NewWidth, int NewHeight)
        {
            SetBounds(0, 0, NewWidth, NewHeight);
        }

        // ---- Win32 ----------------------------------------------------------------------

        private const int GWL_STYLE = -16;
        private const int WS_CHILD = 0x40000000;
        private const int WS_POPUP = unchecked((int)0x80000000);

        [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
    }
}
