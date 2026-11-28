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

        /// <summary>Side-panel menu (garages, showrooms, lobby...): glass card on the left under the screen title,
        /// accent selection pill, toggles / sliders, description card below.</summary>
        public void Draw()
        {
            Clamp();
            Theme.FullscreenFrame();
            openT = Ds.Damp(openT, 1f, 16f);
            float a = Ds.Clamp01(openT * 1.6f), off = (1f - Ds.EaseOut(openT)) * -0.02f;
            var acc = Th.Accent;
            float x = 0.045f + off, w = 0.27f, rh = Th.Px(48f), pad = Th.Px(10f);
            if (VisibleRows > 8) { VisibleRows = 8; Clamp(); }
            int vis = VisibleRows, shown = Math.Min(vis, Items.Count - scroll);
            float y = 0.14f;
            float headH = Th.Px(78f), listH = Math.Max(1, shown) * rh + 2 * pad;
            Kit.Glass(x, y, w, headH + listH, Th.R_Lg, a);
            float ix = x + Ds.Px(Th.Px(20f)), iw = w - Ds.Px(Th.Px(40f));
            Ds.TM(Ds.Fit((Subtitle ?? "").ToUpperInvariant(), 0.24f, iw * 0.7f, Ds.Display), ix, y + Th.Px(24f), 0.24f, Ds.A(acc, a), Ds.Display);
            if (Items.Count > 0) Ds.TM((Selected + 1) + " / " + Items.Count, x + w - Ds.Px(Th.Px(20f)), y + Th.Px(24f), 0.24f, Ds.A(Th.Sub, a), Ds.Display, Alignment.Right);
            Ds.TM(Ds.Fit((Title ?? "").ToUpperInvariant(), 0.66f, iw, Ds.Display), ix, y + Th.Px(54f), 0.66f, Ds.A(Th.Text, a), Ds.Display);
            Ds.Rect(ix, y + headH - Th.Px(1f), iw, Th.Px(1f), Ds.A(Th.Line, a));
            y += headH + pad;
            float rx = x + Ds.Px(pad), rw = w - 2 * Ds.Px(pad);
            float target = y + (Selected - scroll) * rh;
            animSel = animSel < 0 ? target : Ds.Damp(animSel, target, 40f);
            if (Items.Count > 0) Kit.Round(rx, animSel, rw, rh - Th.Px(4f), Th.R_Md, Ds.A(acc, a));
            bool adjustable = false;
            for (int i = scroll; i < Items.Count && i < scroll + vis; i++)
            {
                var it = Items[i]; bool sel = i == Selected; float cy = y + (rh - Th.Px(4f)) / 2f;
                bool onBar = sel && Math.Abs(animSel - target) < rh * 0.5f;
                if (sel) adjustable = it.Enabled && it.OnAdjust != null;
                var fg = onBar ? Th.On(acc) : !it.Enabled ? Th.Faint : Th.Text;
                string v = it.Value != null ? Strip(it.Value()) : null;
                bool isToggle = v == "On" || v == "Off" || v == "Yes" || v == "No";
                float vw = string.IsNullOrEmpty(v) ? 0 : isToggle ? Ds.Px(Th.Px(44f)) : Math.Min(rw * 0.46f, Ds.Width(v, 0.32f) + (sel && it.OnAdjust != null ? Ds.Px(Th.Px(40f)) : 0));
                float lx = rx + Ds.Px(Th.Px(14f));
                if (it.Icon != null) { if (Textures.Draw("li_" + it.Icon, lx + Ds.Px(Th.Px(10f)), cy, Th.Px(20f), Ds.A(onBar ? fg : acc, a))) lx += Ds.Px(Th.Px(32f)); }
                Ds.TM(Ds.Fit(Strip(it.Label), 0.34f, rx + rw - lx - vw - Ds.Px(Th.Px(24f))), lx, cy, 0.34f, Ds.A(fg, a));
                float vx = rx + rw - Ds.Px(Th.Px(14f));
                if (isToggle) Kit.Toggle(vx - Ds.Px(Th.Px(40f)), cy, v == "On" || v == "Yes", onBar ? Color.White : acc);
                else if (!string.IsNullOrEmpty(v))
                {
                    var vc = onBar ? fg : Th.Sub;
                    if (sel && it.OnAdjust != null && it.Enabled)
                    {
                        float vwid = Math.Min(rw * 0.4f, Ds.Width(v, 0.32f));
                        Textures.Draw("li_chevron-right", vx - Ds.Px(Th.Px(6f)), cy, Th.Px(18f), Ds.A(vc, a));
                        Textures.Draw("li_chevron-left", vx - Ds.Px(Th.Px(28f)) - vwid, cy, Th.Px(18f), Ds.A(vc, a));
                        Ds.TM(Ds.Fit(v, 0.32f, rw * 0.4f), vx - Ds.Px(Th.Px(18f)), cy, 0.32f, Ds.A(vc, a), Ds.Body, Alignment.Right);
                    }
                    else Ds.TM(Ds.Fit(v, 0.32f, rw * 0.46f), vx, cy, 0.32f, Ds.A(vc, a), Ds.Body, Alignment.Right);
                }
                y += rh;
            }
            if (Items.Count > vis)
            {
                float th = rh * vis, top = y - rh * shown;
                Kit.Pill(x + w + Ds.Px(Th.Px(6f)), top, Ds.Px(Th.Px(4f)), th, Ds.A(Th.Track, a));
                Kit.Pill(x + w + Ds.Px(Th.Px(6f)), top + th * scroll / Items.Count, Ds.Px(Th.Px(4f)), th * vis / Items.Count, Ds.A(acc, a));
            }
            if (Items.Count > 0 && Items[Selected].Description != null)
            {
                var d = Strip(Items[Selected].Description());
                if (!string.IsNullOrEmpty(d))
                {
                    y += pad + Th.Px(10f);
                    int lines = Ds.WrapLines(d, 0.3f, w - Ds.Px(Th.Px(44f)), Ds.Body);
                    float dh = Th.Px(28f) + lines * Th.Px(30f);
                    Kit.Glass(x, y, w, dh, Th.R_Lg, a);
                    Kit.Pill(x + Ds.Px(Th.Px(12f)), y + Th.Px(14f), Ds.Px(Th.Px(4f)), dh - Th.Px(28f), Ds.A(acc, a));
                    Ds.T(d, x + Ds.Px(Th.Px(26f)), y + Th.Px(12f), 0.3f, Ds.A(Th.Sub, a), Ds.Body, Alignment.Left, w - Ds.Px(Th.Px(44f)));
                }
            }
            if (adjustable) Buttons.Draw(Control.FrontendLeft, "", Control.FrontendRight, "Change", Control.FrontendAccept, "Select", Control.FrontendCancel, "Back");
            else Buttons.Draw(Control.FrontendUp, "", Control.FrontendDown, "Navigate", Control.FrontendAccept, "Select", Control.FrontendCancel, "Back");
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
