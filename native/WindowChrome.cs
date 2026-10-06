using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Orbit {
    // A narrow native gutter retains Windows resize behavior while WebView2 owns
    // the visible dark title area through its non-client-region support.
    internal sealed class WindowChrome : NativeWindow, IDisposable {
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X,Y; }
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left,Top,Right,Bottom;public Rectangle Rectangle {get{return Rectangle.FromLTRB(Left,Top,Right,Bottom);}} }
        [StructLayout(LayoutKind.Sequential)] private struct MINMAXINFO { public POINT Reserved,MaxSize,MaxPosition,MinTrackSize,MaxTrackSize; }
        [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int Size;public RECT Monitor,Work;public int Flags; }
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
        [DllImport("user32.dll",SetLastError=true)] private static extern bool GetMonitorInfo(IntPtr monitor,ref MONITORINFO info);
        private const int WM_GETMINMAXINFO=0x24, WM_NCHITTEST=0x84, HTCLIENT=1, HTLEFT=10, HTRIGHT=11, HTTOP=12, HTTOPLEFT=13, HTTOPRIGHT=14, HTBOTTOM=15, HTBOTTOMLEFT=16, HTBOTTOMRIGHT=17;
        private const uint MONITOR_DEFAULTTONEAREST=2;
        private readonly MainWindow form;
        internal WindowChrome(MainWindow form) {
            this.form=form;
            form.HandleCreated+=delegate { AssignHandle(form.Handle); };
            form.HandleDestroyed+=delegate { ReleaseHandle(); };
        }
        internal void Minimize() { form.WindowState=FormWindowState.Minimized; }
        internal void ToggleMaximize() { form.WindowState=form.WindowState==FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; }
        internal void CloseWindow() { form.Close(); }
        internal static Rectangle MaximizeMetrics(Rectangle monitor,Rectangle work) {return new Rectangle(work.Left-monitor.Left,work.Top-monitor.Top,work.Width,work.Height);}
        protected override void WndProc(ref Message message) {
            if(message.Msg==WM_GETMINMAXINFO) {
                base.WndProc(ref message);
                IntPtr monitor=MonitorFromWindow(Handle,MONITOR_DEFAULTTONEAREST);
                var info=new MONITORINFO {Size=Marshal.SizeOf(typeof(MONITORINFO))};
                if(monitor!=IntPtr.Zero&&GetMonitorInfo(monitor,ref info)) {
                    Rectangle metrics=MaximizeMetrics(info.Monitor.Rectangle,info.Work.Rectangle);
                    var limits=(MINMAXINFO)Marshal.PtrToStructure(message.LParam,typeof(MINMAXINFO));
                    limits.MaxPosition.X=metrics.X;limits.MaxPosition.Y=metrics.Y;
                    limits.MaxSize.X=metrics.Width;limits.MaxSize.Y=metrics.Height;
                    Marshal.StructureToPtr(limits,message.LParam,false);
                    message.Result=IntPtr.Zero;
                }
                return;
            }
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
