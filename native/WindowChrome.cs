using System;
using System.Drawing;
using System.Windows.Forms;

namespace Orbit {
    // A narrow native gutter retains Windows resize behavior while WebView2 owns
    // the visible dark title area through its non-client-region support.
    internal sealed class WindowChrome : NativeWindow, IDisposable {
        private const int WM_NCHITTEST=0x84, HTCLIENT=1, HTLEFT=10, HTRIGHT=11, HTTOP=12, HTTOPLEFT=13, HTTOPRIGHT=14, HTBOTTOM=15, HTBOTTOMLEFT=16, HTBOTTOMRIGHT=17;
        private readonly MainWindow form;
        internal WindowChrome(MainWindow form) {
            this.form=form;
            form.HandleCreated+=delegate { AssignHandle(form.Handle); UpdateMaximizedBounds(); };
            form.HandleDestroyed+=delegate { ReleaseHandle(); };
            form.Resize+=delegate { UpdateMaximizedBounds(); };
            form.LocationChanged+=delegate { UpdateMaximizedBounds(); };
        }
        internal void Minimize() { form.WindowState=FormWindowState.Minimized; }
        internal void ToggleMaximize() { form.WindowState=form.WindowState==FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; }
        internal void CloseWindow() { form.Close(); }
        private void UpdateMaximizedBounds() { if(form.IsHandleCreated) form.SetChromeMaximizedBounds(Screen.FromHandle(form.Handle).WorkingArea); }
        protected override void WndProc(ref Message message) {
            if(message.Msg==WM_NCHITTEST && form.WindowState==FormWindowState.Normal) {
                long packed=message.LParam.ToInt64();
                Point point=form.PointToClient(new Point((short)(packed&0xffff),(short)((packed>>16)&0xffff))); int border=6;
                bool left=point.X<border,right=point.X>=form.ClientSize.Width-border,top=point.Y<border,bottom=point.Y>=form.ClientSize.Height-border;
                if(top && left) { message.Result=(IntPtr)HTTOPLEFT; return; }
                if(top && right) { message.Result=(IntPtr)HTTOPRIGHT; return; }
                if(bottom && left) { message.Result=(IntPtr)HTBOTTOMLEFT; return; }
                if(bottom && right) { message.Result=(IntPtr)HTBOTTOMRIGHT; return; }
                if(left) { message.Result=(IntPtr)HTLEFT; return; }
                if(right) { message.Result=(IntPtr)HTRIGHT; return; }
                if(top) { message.Result=(IntPtr)HTTOP; return; }
                if(bottom) { message.Result=(IntPtr)HTBOTTOM; return; }
            }
            base.WndProc(ref message);
        }
        public void Dispose() { ReleaseHandle(); }
    }
}
