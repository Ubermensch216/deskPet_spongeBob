using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BikiniFriends
{
    /// <summary>친구 한 마리를 보여주는 테두리 없는 픽셀 알파(레이어드) 창. 투명한 곳은 클릭이 통과한다.</summary>
    internal sealed class PetWindow : Form
    {
        public readonly Pet Pet;
        private readonly App app;
        private string lastKey;
        private PointF origin;
        private int curX = int.MinValue, curY = int.MinValue;
        private bool mouseDown, dragging, firstPresented;
        private Point downPos;
        internal bool HasPresented { get { return firstPresented; } }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                var p = base.CreateParams;
                p.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                return p;
            }
        }

        public PetWindow(App app, Pet pet)
        {
            this.app = app; Pet = pet;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; TopMost = true;
            Text = "비키니친구들"; Size = new Size(1, 1); Location = new Point(-32000, -32000);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        public void Present(double now)
        {
            if (!IsHandleCreated) return;
            Pet.Look look = Pet.GetLook(now);
            string key = look.Key() + "|opacity:" + PlayOptions.Current.PetOpacityPercent;
            Bitmap bitmap = null;
            if (key != lastKey) { bitmap = Pet.Render(look, out origin); lastKey = key; }
            int x = (int)Math.Round(Pet.X + Pet.FeatureJitterX - origin.X), y = (int)Math.Round(Pet.Y + look.HopY - origin.Y);
            if (bitmap != null)
            {
                using (bitmap) PresentBitmap(bitmap, x, y);
                curX = x; curY = y;
            }
            else if (x != curX || y != curY)
            {
                Native.SetWindowPos(Handle, IntPtr.Zero, x, y, 0, 0, 533);
                curX = x; curY = y;
            }
        }

        public void Invalidate2() { lastKey = null; }

        public void KeepOnTop()
        {
            if (IsHandleCreated && Visible) Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, 19);
        }

        private void PresentBitmap(Bitmap bitmap, int x, int y)
        {
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero) return;
            IntPtr dc = IntPtr.Zero, image = IntPtr.Zero, previous = IntPtr.Zero;
            try
            {
                dc = Native.CreateCompatibleDC(screen);
                byte[] bytes = LayerPixels.Premultiplied(bitmap);
                var info = new Native.BitmapInfo { Size = 40, Width = bitmap.Width, Height = -bitmap.Height, Planes = 1, Bits = 32, ImageSize = (uint)bytes.Length };
                IntPtr bits; image = Native.CreateDIBSection(screen, ref info, 0, out bits, IntPtr.Zero, 0);
                if (image == IntPtr.Zero || bits == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                Marshal.Copy(bytes, 0, bits, bytes.Length); previous = Native.SelectObject(dc, image);
                var size = new Native.SIZE(bitmap.Width, bitmap.Height); var src = new Native.POINT(0, 0); var dst = new Native.POINT(x, y);
                var blend = new Native.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = PlayOptions.Current.PetAlpha, AlphaFormat = 1 };
                if (!Native.UpdateLayeredWindow(Handle, screen, ref dst, ref size, dc, ref src, 0, ref blend, Native.ULW_ALPHA)) throw new Win32Exception(Marshal.GetLastWin32Error());
                firstPresented = true;
            }
            catch (Exception e) { FeatureLog.Write(e); }
            finally
            {
                if (previous != IntPtr.Zero && dc != IntPtr.Zero) Native.SelectObject(dc, previous);
                if (image != IntPtr.Zero) Native.DeleteObject(image);
                if (dc != IntPtr.Zero) Native.DeleteDC(dc);
                Native.ReleaseDC(IntPtr.Zero, screen);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) { mouseDown = true; Pet.InputHeld = true; dragging = false; downPos = Cursor.Position; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!mouseDown) return;
            Point p = Cursor.Position;
            if (!dragging)
            {
                int dx = p.X - downPos.X, dy = p.Y - downPos.Y;
                if (dx * dx + dy * dy >= 16) { dragging = true; Pet.BeginDrag(app, downPos.X, downPos.Y); app.BringToFront(this); }
            }
            if (dragging) Pet.DragTo(app, p.X, p.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && mouseDown)
            {
                mouseDown = false; Pet.InputHeld = false;
                if (dragging)
                {
                    dragging = false; Point p = Cursor.Position;
                    if (Pet.State == PetState.Drag) Pet.DragTo(app, p.X, p.Y);
                    Pet.EndDrag(app);
                }
                else Pet.Poke(app);
            }
            else if (e.Button == MouseButtons.Right) app.ShowPetMenu(this, Cursor.Position);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture) Pet.InputHeld = false;
            if (dragging && !Capture) { dragging = false; mouseDown = false; Pet.InputHeld = false; Pet.EndDrag(app); }
        }
    }
}
