using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Native;
using GTA.UI;

namespace MidnightClub.UI
{
    public sealed class MenuItem
    {
        public string Label;
        public Func<string> Value;          // right-aligned value text (optional)
        public Func<string> Description;    // shown under the menu (optional)
        public string Icon;                 // optional li_* icon name (hub list rows + detail panel)
        public Action OnSelect;
        public Action<int> OnAdjust;        // -1 / +1 for left/right
        public bool Enabled = true;
        public Action OnPreview, OnUnpreview;   // live preview while highlighted (mod shop)
        public MenuItem(string label, Action onSelect = null) { Label = label; OnSelect = onSelect; }
    }

    /// <summary>Self-contained Midnight Club styled menu (no external menu library needed).
    /// Navigation uses frontend controls so it works on keyboard and controller.</summary>
    public sealed class Menu
    {
        public string Title, Subtitle;
        public readonly List<MenuItem> Items = new List<MenuItem>();
        public int Selected;
        public Action OnBack;            // null = Back closes the menu
        public Action OnOpen;
        public Action<int> OnHighlight;  // called when the selection changes (live previews)
        public bool IsShopList;          // mod shop: items are purchasable parts (shows the price/detail panel)
        public int Tab = -1;             // mod shop: which tab this menu belongs to
        private int lastHighlight = -1;
        public int VisibleRows = 10;
        private int scroll;
        public int Scroll { get { return scroll; } }

        private const float X = 0.03f, W = 0.27f, RowH = 0.034f, HeadH = 0.085f, Top = 0.07f;

        public Menu(string title, string subtitle = null) { Title = title; Subtitle = subtitle; }

        public MenuItem Add(MenuItem item) { Items.Add(item); return item; }
        public void ResetHighlight() { lastHighlight = -1; }

        public void Clamp()
        {
            if (Items.Count == 0) { Selected = 0; return; }
            if (Selected >= Items.Count) Selected = Items.Count - 1;
            if (Selected < 0) Selected = 0;
            if (Selected < scroll) scroll = Selected;
            if (Selected >= scroll + VisibleRows) scroll = Selected - VisibleRows + 1;
            if (scroll > Math.Max(0, Items.Count - VisibleRows)) scroll = Math.Max(0, Items.Count - VisibleRows);
        }

        /// <summary>Handles input. Returns false when the user backed out of this menu.</summary>
        public bool ProcessInput()
        {
            Game.DisableControlThisFrame(Control.Phone);
            Game.DisableControlThisFrame(Control.FrontendPauseAlternate);   // Esc = back, not pause
            Game.DisableControlThisFrame(Control.VehicleRadioWheel);
            Game.DisableControlThisFrame(Control.VehicleCinCam);
            Game.DisableControlThisFrame(Control.VehicleHeadlight);
            Game.DisableControlThisFrame(Control.VehicleDuck);
            Game.DisableControlThisFrame(Control.VehicleSelectNextWeapon);
            if (Items.Count == 0) return !UI.Input.JustPressed(Control.FrontendCancel);

            if (UI.Input.Repeat(Control.FrontendUp)) { Selected = (Selected - 1 + Items.Count) % Items.Count; Sound("NAV_UP_DOWN"); }
            if (UI.Input.Repeat(Control.FrontendDown)) { Selected = (Selected + 1) % Items.Count; Sound("NAV_UP_DOWN"); }
            Clamp();
            if (Selected != lastHighlight)
            {
                if (lastHighlight >= 0 && lastHighlight < Items.Count && Items[lastHighlight].OnUnpreview != null) Items[lastHighlight].OnUnpreview();
                lastHighlight = Selected;
                if (OnHighlight != null) OnHighlight(Selected);
                if (Items[Selected].OnPreview != null) Items[Selected].OnPreview();
            }
            var it = Items[Selected];
            if (it.Enabled && it.OnAdjust != null)
            {
                if (UI.Input.Repeat(Control.FrontendLeft)) { it.OnAdjust(-1); Sound("NAV_LEFT_RIGHT"); }
                if (UI.Input.Repeat(Control.FrontendRight)) { it.OnAdjust(+1); Sound("NAV_LEFT_RIGHT"); }
            }
            if (UI.Input.JustPressed(Control.FrontendAccept))
            {
                if (it.Enabled && it.OnSelect != null) { Sound("SELECT"); if (it.OnUnpreview != null && it.OnPreview != null) it.OnUnpreview(); it.OnSelect(); lastHighlight = -1; }
                else Sound("ERROR");
            }
            if (UI.Input.JustPressed(Control.FrontendCancel)) { Sound("BACK"); if (it.OnUnpreview != null) it.OnUnpreview(); lastHighlight = -1; return false; }
            return true;
        }

        // animation state (panel style)
        private float animSel = -1f, openT;

        public void Draw()
        {
            Clamp();
            Theme.FullscreenFrame();
            openT = Ds.Damp(openT, 1f, 16f);
            float a = Ds.Clamp01(openT * 1.6f), off = (1f - Ds.EaseOut(openT)) * -0.02f;
            float x = 0.05f + off, w = 0.25f, rh = 0.044f;
            int vis = Math.Min(VisibleRows, 8);
            if (VisibleRows > 8) { VisibleRows = 8; Clamp(); }
            int shown = Math.Min(vis, Items.Count - scroll);
            float y = 0.135f;                                   // below Studio.Title (garages, showrooms)
            float listH = Math.Max(1, shown) * rh;
            // panel
            float pad = Ds.Px(0.012f);
            Ds.Rect(x - pad, y, w + 2 * pad, 0.088f + listH + 0.01f, Ds.A(Color.FromArgb(225, 12, 11, 17), a));
            Ds.Rect(x - pad, y, w + 2 * pad, 0.003f, Ds.A(Ds.Violet, a));
            // header
            Ds.TM(Ds.Fit((Subtitle ?? "").ToUpperInvariant(), 0.24f, w * 0.75f, Ds.Display), x, y + 0.022f, 0.24f, Ds.A(Ds.Violet, a), Ds.Display);
            if (Items.Count > 0) Ds.TM((Selected + 1) + " / " + Items.Count, x + w, y + 0.022f, 0.24f, Ds.A(Ds.Muted, a), Ds.Display, Alignment.Right);
            Ds.TM(Ds.Fit((Title ?? "").ToUpperInvariant(), 0.72f, w, Ds.Display), x, y + 0.058f, 0.72f, Ds.A(Color.White, a), Ds.Display);
            y += 0.088f;
            float target = y + (Selected - scroll) * rh;
            animSel = animSel < 0 ? target : Ds.Damp(animSel, target, 40f);
            if (Items.Count > 0)
            {
                Ds.Rect(x - pad, animSel, w + 2 * pad, rh, Ds.A(Color.FromArgb(255, 245, 245, 248), a));
                Ds.Rect(x - pad, animSel, Ds.Px(0.004f), rh, Ds.A(Ds.Violet, a));
            }
            bool adjustable = false;
            for (int i = scroll; i < Items.Count && i < scroll + vis; i++)
            {
                var it = Items[i]; bool sel = i == Selected; float cy = y + rh / 2f;
                bool onBar = sel && Math.Abs(animSel - target) < rh * 0.5f;
                if (sel) adjustable = it.Enabled && it.OnAdjust != null;
                string v = it.Value != null ? Strip(it.Value()) : null;
                float vw = string.IsNullOrEmpty(v) ? 0 : Math.Min(w * 0.48f, Ds.Width(v, 0.32f) + (sel && it.OnAdjust != null ? Ds.Px(0.03f) : 0));
                var c = !it.Enabled ? (onBar ? Color.FromArgb(255, 140, 140, 150) : Ds.Faint) : onBar ? Color.FromArgb(255, 14, 12, 20) : Color.FromArgb(255, 232, 230, 238);
                Ds.TM(Ds.Fit(Strip(it.Label), 0.34f, w - vw - Ds.Px(0.015f)), x + Ds.Px(0.006f), cy, 0.34f, Ds.A(c, a));
                if (!string.IsNullOrEmpty(v))
                {
                    var vc = onBar ? Color.FromArgb(255, 14, 12, 20) : Ds.Muted;
                    if (sel && it.OnAdjust != null && it.Enabled)
                    {
                        float vwid = Math.Min(w * 0.42f, Ds.Width(v, 0.32f));
                        if (!Textures.Draw("li_chevron-right", x + w - Ds.Px(0.006f), cy, 0.02f, Ds.A(vc, a))) Ds.TM(">", x + w, cy, 0.32f, Ds.A(vc, a), Ds.Body, Alignment.Right);
                        if (!Textures.Draw("li_chevron-left", x + w - Ds.Px(0.026f) - vwid, cy, 0.02f, Ds.A(vc, a))) Ds.TM("<", x + w - Ds.Px(0.024f) - vwid, cy, 0.32f, Ds.A(vc, a), Ds.Body, Alignment.Right);
                        Ds.TM(Ds.Fit(v, 0.32f, w * 0.42f), x + w - Ds.Px(0.018f), cy, 0.32f, Ds.A(vc, a), Ds.Body, Alignment.Right);
                    }
                    else Ds.TM(Ds.Fit(v, 0.32f, w * 0.48f), x + w - Ds.Px(0.002f), cy, 0.32f, Ds.A(vc, a), Ds.Body, Alignment.Right);
                }
                y += rh;
            }
            if (Items.Count > vis)
            {
                float th = rh * vis, top = y - rh * shown;
                Ds.Rect(x + w + pad + Ds.Px(0.004f), top, Ds.Px(0.002f), th, Ds.A(Ds.Stroke, a));
                Ds.Rect(x + w + pad + Ds.Px(0.004f), top + th * scroll / Items.Count, Ds.Px(0.002f), th * vis / Items.Count, Ds.A(Ds.Violet, a));
            }
            if (Items.Count > 0 && Items[Selected].Description != null)
            {
                var d = Strip(Items[Selected].Description());
                if (!string.IsNullOrEmpty(d))
                {
                    y += 0.01f;
                    int lines = 1 + (int)(Ds.Width(d, 0.3f) / (w - Ds.Px(0.01f)));
                    float dh = 0.02f + lines * 0.029f;
                    Ds.Rect(x - pad, y, w + 2 * pad, dh, Ds.A(Color.FromArgb(215, 12, 11, 17), a));
                    Ds.T(d, x, y + 0.009f, 0.3f, Ds.A(Color.FromArgb(255, 200, 198, 210), a), Ds.Body, Alignment.Left, w);
                }
            }
            if (adjustable) Buttons.Draw(Control.FrontendAccept, "Select", Control.FrontendCancel, "Back", Control.FrontendRight, "", Control.FrontendLeft, "Change");
            else Buttons.Draw(Control.FrontendAccept, "Select", Control.FrontendCancel, "Back", Control.FrontendDown, "Navigate");
        }

        private static string Strip(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('~') < 0) return s;
            return System.Text.RegularExpressions.Regex.Replace(s, "~[a-zA-Z]~", "");
        }

        /// <summary>Restarts the open animation (called when the menu becomes current).</summary>
        public void Reanimate() { openT = 0f; animSel = -1f; }

        private static void Rect(float x, float y, float w, float h, Color c) { Theme.Rect(x, y, w, h, c); }

        private static void Text(string s, float x, float y, float scale, Color c, GTA.UI.Font f, bool right, float wrap = 0f)
        { Theme.Text(s, x, y, scale, c, f, right ? Alignment.Right : Alignment.Left, wrap); }

        private static void Sound(string name)
        {
            Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, name, "HUD_FRONTEND_DEFAULT_SOUNDSET", true);
        }
    }

    /// <summary>Stack of menus; one instance drives all Midnight Club menus.</summary>
    public sealed class MenuStack
    {
        private readonly Stack<Menu> stack = new Stack<Menu>();
        public bool IsOpen { get { return stack.Count > 0; } }
        /// <summary>True while the current menus are shown inside the phone.</summary>
        public bool PhoneStyle;
        /// <summary>Optional renderer for this stack (e.g. the mod shop).</summary>
        public Action<Menu> CustomRenderer;
        public int Depth { get { return stack.Count; } }
        public static Action<Menu> PhoneRenderer;
        public Menu Current { get { return stack.Count > 0 ? stack.Peek() : null; } }

        public void Open(Menu m) { stack.Clear(); PhoneStyle = false; ClosedByBack = false; Push(m); }
        public void Push(Menu m) { stack.Push(m); if (m.OnOpen != null) m.OnOpen(); m.ResetHighlight(); m.Reanimate(); }
        public bool ClosedByBack;
        public void Close() { stack.Clear(); PhoneStyle = false; ClosedByBack = false; }
        public void Pop() { if (stack.Count > 0) stack.Pop(); if (Current != null && Current.OnOpen != null) Current.OnOpen(); }

        public void Tick()
        {
            if (!IsOpen) return;
            var m = Current;
            if (!m.ProcessInput())
            {
                if (m.OnBack != null) m.OnBack(); else Pop();
                if (!IsOpen) ClosedByBack = true;
            }
            if (!IsOpen) PhoneStyle = false;
            else if (CustomRenderer != null) CustomRenderer(Current);
            else if (PhoneStyle && PhoneRenderer != null) PhoneRenderer(Current);
            else Current.Draw();
        }
    }
}
