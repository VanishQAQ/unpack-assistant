using System;
using System.Windows.Forms;

namespace LayerUnpacker
{
    // Native password mode disables Windows IME. Enable normal editing while
    // focused, then mask the value again on leaving the field.
    public sealed class ImePasswordBox : TextBox
    {
        bool reveal;
        public bool Composing { get; private set; }
        public bool Reveal
        {
            get { return reveal; }
            set { reveal = value; UpdateMask(Focused); }
        }
        public ImePasswordBox() { UseSystemPasswordChar = true; }
        void UpdateMask(bool editing)
        {
            UseSystemPasswordChar = !(editing || reveal);
            if (!UseSystemPasswordChar) ImeMode = ImeMode.NoControl;
        }
        protected override void OnEnter(EventArgs e) { UpdateMask(true); base.OnEnter(e); }
        protected override void OnLeave(EventArgs e) { base.OnLeave(e); UpdateMask(false); }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x010D) Composing = true; // WM_IME_STARTCOMPOSITION
            if (message.Msg == 0x010E) Composing = false; // WM_IME_ENDCOMPOSITION
            base.WndProc(ref message);
        }
    }
}
