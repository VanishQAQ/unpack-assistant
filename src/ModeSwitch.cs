using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LayerUnpacker
{
    public sealed class ModeSwitch : Control
    {
        readonly Timer animation = new Timer { Interval = 15 };
        readonly Stopwatch watch = new Stopwatch();
        bool selected;
        float position, origin;
        int downX;
        public event EventHandler ValueChanged;
        public bool VolumeMode
        {
            get { return selected; }
            set
            {
                if (selected == value) return;
                selected = value; origin = position; watch.Restart(); animation.Start();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }
        public ModeSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            Size = new Size(93, 28); TabStop = true; Cursor = Cursors.Hand;
            AccessibleName = "解压模式：普通解压或分卷解压"; AccessibleRole = AccessibleRole.CheckButton;
            animation.Tick += delegate {
                float t = Math.Min(1, watch.ElapsedMilliseconds / 200f);
                float eased = 1 - (1 - t) * (1 - t) * (1 - t);
                position = origin + ((selected ? 1 : 0) - origin) * eased;
                Invalidate(); if (t >= 1) animation.Stop();
            };
        }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { Focus(); downX = e.X; } }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left) VolumeMode = Math.Abs(e.X - downX) > 15 ? e.X > downX : e.X >= Width / 2;
        }
        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { VolumeMode = !VolumeMode; e.Handled = true; }
            else if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right) { VolumeMode = e.KeyCode == Keys.Right; e.Handled = true; }
        }
        static GraphicsPath Rounded(RectangleF r)
        {
            float d = Math.Min(14, r.Height); var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var track = Rounded(new RectangleF(0, 0, Width - 1, Height - 1)))
            using (var brush = new SolidBrush(Color.FromArgb(226, 233, 244))) e.Graphics.FillPath(brush, track);
            float half = (Width - 8) / 2f;
            using (var thumb = Rounded(new RectangleF(4 + half * position, 4, half, Height - 8)))
            using (var brush = new SolidBrush(Enabled ? Color.FromArgb(39, 103, 224) : Color.Gray)) e.Graphics.FillPath(brush, thumb);
            TextRenderer.DrawText(e.Graphics, Language.T("普通"), Font, new Rectangle(4, 0, (int)half, Height), !selected ? Color.White : ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics, Language.T("分卷"), Font, new Rectangle(4 + (int)half, 0, (int)half, Height), selected ? Color.White : ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        protected override void Dispose(bool disposing) { if (disposing) animation.Dispose(); base.Dispose(disposing); }
    }
}
