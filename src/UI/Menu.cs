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
            if (Items.Count == 0) return !Game.IsControlJustPressed(Control.FrontendCancel);

            if (Game.IsControlJustPressed(Control.FrontendUp)) { Selected = (Selected - 1 + Items.Count) % Items.Count; Sound("NAV_UP_DOWN"); }
            if (Game.IsControlJustPressed(Control.FrontendDown)) { Selected = (Selected + 1) % Items.Count; Sound("NAV_UP_DOWN"); }
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
                if (Game.IsControlJustPressed(Control.FrontendLeft)) { it.OnAdjust(-1); Sound("NAV_LEFT_RIGHT"); }
                if (Game.IsControlJustPressed(Control.FrontendRight)) { it.OnAdjust(+1); Sound("NAV_LEFT_RIGHT"); }
            }
            if (Game.IsControlJustPressed(Control.FrontendAccept))
            {
                if (it.Enabled && it.OnSelect != null) { Sound("SELECT"); if (it.OnUnpreview != null && it.OnPreview != null) it.OnUnpreview(); it.OnSelect(); lastHighlight = -1; }
                else Sound("ERROR");
            }
            if (Game.IsControlJustPressed(Control.FrontendCancel)) { Sound("BACK"); if (it.OnUnpreview != null) it.OnUnpreview(); lastHighlight = -1; return false; }
            return true;
        }

        // animation state (panel style)
        private float animSel = -1f, openT;

        public void Draw()
        {
            Clamp();
            Theme.FullscreenFrame();
            openT = Ds.Damp(openT, 1f, 12f);
            float a = Ds.Clamp01(openT * 1.4f), off = (1f - Ds.EaseOut(openT)) * -0.03f;
            float x = 0.04f + off, w = 0.26f, y = 0.075f, rh = 0.04f;
            // soft backdrop on the left third so text always reads
            Ds.HGrad(0f, 0f, 0.42f, 1f, Ds.A(Color.FromArgb(255, 6, 5, 10), 0.82f * a), Ds.A(Color.FromArgb(255, 6, 5, 10), 0f), 18);
            // header
            Ds.Tag("MIDNIGHT CLUB", x, y, Ds.Violet, Ds.Text, 0.26f, a);
            Ds.T(Ds.Fit(Title.ToUpperInvariant(), 0.95f, w, Ds.Display), x - 0.001f, y + 0.026f, 0.95f, Ds.A(Ds.Text, a), Ds.Display);
            y += 0.1f;
            Ds.T(Ds.Fit((Subtitle ?? "").ToUpperInvariant(), 0.25f, w * 0.8f), x, y, 0.25f, Ds.A(Ds.Muted, a));
            if (Items.Count > 0) Ds.T((Selected + 1) + " / " + Items.Count, x + w, y, 0.25f, Ds.A(Ds.Faint, a), Ds.Body, Alignment.Right);
            y += 0.03f;
            Ds.Stripes(x, y, w, Ds.Violet, Ds.Cyan, a);
            y += 0.02f;
            int shown = Math.Min(VisibleRows, Items.Count - scroll);
            float target = y + (Selected - scroll) * rh;
            animSel = animSel < 0 ? target : Ds.Damp(animSel, target, 26f);
            if (Items.Count > 0)
            {
                float sl = Ds.SlantFor(rh - 0.004f);
                Ds.Skew(x - Ds.Px(0.012f), animSel, w + Ds.Px(0.02f), rh - 0.004f, sl, Ds.A(Ds.Violet, a));
                Ds.Shine(x - Ds.Px(0.012f), animSel, w + Ds.Px(0.02f), rh - 0.004f, sl, 2.6f, a);
            }
            bool adjustable = false;
            for (int i = scroll; i < Items.Count && i < scroll + VisibleRows; i++)
            {
                var it = Items[i]; bool sel = i == Selected;
                if (sel) adjustable = it.Enabled && it.OnAdjust != null;
                string v = it.Value != null ? it.Value() : null;
                float vw = string.IsNullOrEmpty(v) ? 0 : Math.Min(w * 0.5f, Ds.Width(v, 0.3f) + (sel && it.OnAdjust != null ? Ds.Px(0.03f) : 0));
                var c = !it.Enabled ? Ds.Faint : sel ? Color.White : Color.FromArgb(255, 214, 210, 230);
                Ds.T(Ds.Fit(it.Label, 0.33f, w - vw - Ds.Px(0.015f)), x + Ds.Px(0.004f), y + 0.0075f, 0.33f, Ds.A(c, a));
                if (!string.IsNullOrEmpty(v))
                {
                    if (sel && it.OnAdjust != null && it.Enabled)
                    {
                        Theme.Sprite("commonmenu", "arrowright", x + w - Ds.Px(0.012f), y + 0.011f, Ds.Px(0.016f), 0.016f, Ds.A(Color.White, a));
                        Theme.Sprite("commonmenu", "arrowleft", x + w - Ds.Px(0.03f) - Math.Min(w * 0.5f, Ds.Width(v, 0.3f)), y + 0.011f, Ds.Px(0.016f), 0.016f, Ds.A(Color.White, a));
                        Ds.T(Ds.Fit(v, 0.3f, w * 0.5f), x + w - Ds.Px(0.016f), y + 0.009f, 0.3f, Ds.A(Color.White, a), Ds.Body, Alignment.Right);
                    }
                    else Ds.T(Ds.Fit(v, 0.3f, w * 0.5f), x + w - Ds.Px(0.002f), y + 0.009f, 0.3f, Ds.A(sel ? Color.White : Ds.Muted, a), Ds.Body, Alignment.Right);
                }
                y += rh;
            }
            if (Items.Count > VisibleRows)
            {
                float th = rh * VisibleRows, top = y - rh * shown;
                Ds.Rect(x + w + Ds.Px(0.014f), top, Ds.Px(0.002f), th, Ds.A(Ds.Stroke, a));
                Ds.Rect(x + w + Ds.Px(0.014f), top + th * scroll / Items.Count, Ds.Px(0.002f), th * VisibleRows / Items.Count, Ds.A(Ds.Violet, a));
            }
            if (Items.Count > 0 && Items[Selected].Description != null)
            {
                var d = Items[Selected].Description();
                if (!string.IsNullOrEmpty(d))
                {
                    y += 0.014f;
                    int lines = 1 + (int)(Ds.Width(d, 0.28f) / (w - Ds.Px(0.02f)));
                    Ds.Rect(x - Ds.Px(0.008f), y, w + Ds.Px(0.016f), 0.022f + lines * 0.026f, Ds.A(Ds.Surface, 0.94f * a));
                    Ds.Rect(x - Ds.Px(0.008f), y, Ds.Px(0.0025f), 0.022f + lines * 0.026f, Ds.A(Ds.Violet, a));
                    Ds.T(d, x + Ds.Px(0.004f), y + 0.01f, 0.28f, Ds.A(Ds.Muted, a), Ds.Body, Alignment.Left, w - Ds.Px(0.012f));
                }
            }
            if (adjustable) Buttons.Draw(Control.FrontendAccept, "Select", Control.FrontendCancel, "Back", Control.FrontendRight, "Change", Control.FrontendLeft, "");
            else Buttons.Draw(Control.FrontendAccept, "Select", Control.FrontendCancel, "Back", Control.FrontendDown, "Navigate");
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
