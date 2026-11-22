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
        private int lastHighlight = -1;
        private const int Visible = 10;
        private int scroll;

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
            if (Selected >= scroll + Visible) scroll = Selected - Visible + 1;
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
            if (Selected != lastHighlight) { lastHighlight = Selected; if (OnHighlight != null) OnHighlight(Selected); }
            var it = Items[Selected];
            if (it.Enabled && it.OnAdjust != null)
            {
                if (Game.IsControlJustPressed(Control.FrontendLeft)) { it.OnAdjust(-1); Sound("NAV_LEFT_RIGHT"); }
                if (Game.IsControlJustPressed(Control.FrontendRight)) { it.OnAdjust(+1); Sound("NAV_LEFT_RIGHT"); }
            }
            if (Game.IsControlJustPressed(Control.FrontendAccept))
            {
                if (it.Enabled && it.OnSelect != null) { Sound("SELECT"); it.OnSelect(); }
                else Sound("ERROR");
            }
            if (Game.IsControlJustPressed(Control.FrontendCancel)) { Sound("BACK"); return false; }
            return true;
        }

        public void Draw()
        {
            Clamp();
            Theme.FullscreenFrame();
            float x = X, w = W, y = Top;
            // header: deep purple with a neon sheen
            Theme.Rect(x, y, w, HeadH, Theme.PurpleDeep);
            Theme.Sprite("commonmenu", "gradient_nav", x, y, w, HeadH, Color.FromArgb(150, 168, 85, 247));
            Theme.Rect(x, y + HeadH - 0.003f, w, 0.003f, Theme.Cyan);
            Theme.Text("MIDNIGHT CLUB", x + w - 0.008f, y + 0.006f, 0.26f, Color.FromArgb(200, 255, 255, 255), GTA.UI.Font.ChaletComprimeCologne, Alignment.Right);
            Theme.Text(Title.ToUpperInvariant(), x + 0.012f, y + 0.012f, 0.72f, Color.White, GTA.UI.Font.Pricedown);
            y += HeadH;
            Theme.Rect(x, y, w, RowH, Color.FromArgb(240, 0, 0, 0));
            Theme.Text((Subtitle ?? "").ToUpperInvariant(), x + 0.01f, y + 0.006f, 0.3f, Theme.Cyan, GTA.UI.Font.ChaletLondon);
            if (Items.Count > 0) Theme.Text((Selected + 1) + " / " + Items.Count, x + w - 0.01f, y + 0.006f, 0.3f, Theme.TextDim, GTA.UI.Font.ChaletLondon, Alignment.Right);
            y += RowH;
            int shown = Math.Min(Visible, Items.Count - scroll);
            Theme.Sprite("commonmenu", "gradient_bgd", x, y, w, RowH * Math.Max(1, shown), Color.FromArgb(240, 255, 255, 255));
            Theme.Rect(x, y, w, RowH * Math.Max(1, shown), Color.FromArgb(110, 8, 6, 18));
            bool adjustable = false;
            for (int i = scroll; i < Items.Count && i < scroll + Visible; i++)
            {
                var it = Items[i]; bool sel = i == Selected;
                if (sel)
                {
                    Theme.Sprite("commonmenu", "gradient_nav", x, y, w, RowH, Color.FromArgb(255, 168, 85, 247));
                    Theme.Rect(x, y, 0.003f, RowH, Color.White);
                    adjustable = it.Enabled && it.OnAdjust != null;
                }
                var c = !it.Enabled ? Color.FromArgb(150, 150, 150, 165) : Color.White;
                Theme.Text(it.Label, x + 0.011f, y + 0.0045f, 0.34f, c);
                string v = it.Value != null ? it.Value() : null;
                if (!string.IsNullOrEmpty(v))
                {
                    if (sel && it.OnAdjust != null)
                    {
                        Theme.Text(v, x + w - 0.022f, y + 0.0045f, 0.34f, c, GTA.UI.Font.ChaletLondon, Alignment.Right);
                        Theme.Sprite("commonmenu", "arrowright", x + w - 0.018f, y + 0.008f, 0.01f, 0.018f, Color.White);
                    }
                    else Theme.Text(v, x + w - 0.01f, y + 0.0045f, 0.34f, c, GTA.UI.Font.ChaletLondon, Alignment.Right);
                }
                y += RowH;
            }
            if (Items.Count > Visible)
            {
                Theme.Rect(x, y, w, RowH * 0.8f, Color.FromArgb(220, 0, 0, 0));
                Theme.Sprite("commonmenu", "shop_arrows_upanddown", x + w / 2f - 0.008f, y + 0.002f, 0.016f, RowH * 0.75f, Color.White);
                y += RowH * 0.8f;
            }
            if (Items.Count > 0 && Items[Selected].Description != null)
            {
                var d = Items[Selected].Description();
                if (!string.IsNullOrEmpty(d))
                {
                    y += 0.006f;
                    int lines = 1 + d.Length / 58;
                    Theme.Card(x, y, w, 0.016f + lines * 0.024f);
                    Theme.Text(d, x + 0.011f, y + 0.007f, 0.3f, Color.White, GTA.UI.Font.ChaletLondon, Alignment.Left, w - 0.02f);
                }
            }
            if (adjustable) Buttons.Draw(Control.FrontendAccept, "Select", Control.FrontendCancel, "Back", Control.FrontendRight, "Change", Control.FrontendLeft, "");
            else Buttons.Draw(Control.FrontendAccept, "Select", Control.FrontendCancel, "Back", Control.FrontendDown, "Navigate");
        }

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
        public Menu Current { get { return stack.Count > 0 ? stack.Peek() : null; } }

        public void Open(Menu m) { stack.Clear(); Push(m); }
        public void Push(Menu m) { stack.Push(m); if (m.OnOpen != null) m.OnOpen(); m.ResetHighlight(); }
        public void Close() { stack.Clear(); }
        public void Pop() { if (stack.Count > 0) stack.Pop(); if (Current != null && Current.OnOpen != null) Current.OnOpen(); }

        public void Tick()
        {
            if (!IsOpen) return;
            var m = Current;
            if (!m.ProcessInput())
            {
                if (m.OnBack != null) m.OnBack(); else Pop();
            }
            if (IsOpen) Current.Draw();
        }
    }
}
