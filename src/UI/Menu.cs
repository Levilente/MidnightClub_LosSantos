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
        private const int Visible = 10;
        private int scroll;

        private static readonly Color Bg = Color.FromArgb(215, 10, 8, 18);
        private static readonly Color Head = Color.FromArgb(235, 70, 20, 120);
        private static readonly Color Sel = Color.FromArgb(230, 150, 60, 255);
        private static readonly Color Dim = Color.FromArgb(150, 160, 160, 170);
        private const float X = 0.02f, W = 0.26f, RowH = 0.034f, HeadH = 0.07f, Top = 0.06f;

        public Menu(string title, string subtitle = null) { Title = title; Subtitle = subtitle; }

        public MenuItem Add(MenuItem item) { Items.Add(item); return item; }

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
            Rect(X, Top, W, HeadH, Head);
            Text(Title.ToUpperInvariant(), X + 0.01f, Top + 0.008f, 0.75f, Color.White, GTA.UI.Font.Pricedown, false);
            float y = Top + HeadH;
            Rect(X, y, W, RowH, Color.FromArgb(240, 0, 0, 0));
            Text((Subtitle ?? "").ToUpperInvariant(), X + 0.008f, y + 0.005f, 0.32f, Color.FromArgb(255, 80, 220, 255), GTA.UI.Font.ChaletLondon, false);
            Text(Items.Count == 0 ? "" : (Selected + 1) + " / " + Items.Count, X + W - 0.008f, y + 0.005f, 0.32f, Dim, GTA.UI.Font.ChaletLondon, true);
            y += RowH;
            for (int i = scroll; i < Items.Count && i < scroll + Visible; i++)
            {
                var it = Items[i]; bool sel = i == Selected;
                Rect(X, y, W, RowH, sel ? Sel : Bg);
                var c = !it.Enabled ? Dim : Color.White;
                Text(it.Label, X + 0.008f, y + 0.004f, 0.36f, c, GTA.UI.Font.ChaletLondon, false);
                string v = it.Value != null ? it.Value() : null;
                if (!string.IsNullOrEmpty(v))
                    Text(it.OnAdjust != null && sel ? "< " + v + " >" : v, X + W - 0.008f, y + 0.004f, 0.36f, c, GTA.UI.Font.ChaletLondon, true);
                y += RowH;
            }
            if (Items.Count > 0 && Items[Selected].Description != null)
            {
                var d = Items[Selected].Description();
                if (!string.IsNullOrEmpty(d))
                {
                    Rect(X, y + 0.004f, W, RowH * 1.6f, Color.FromArgb(225, 0, 0, 0));
                    Text(d, X + 0.008f, y + 0.008f, 0.32f, Color.White, GTA.UI.Font.ChaletLondon, false, W - 0.016f);
                }
            }
        }

        private static void Rect(float x, float y, float w, float h, Color c)
        {
            Function.Call(Hash.DRAW_RECT, x + w / 2f, y + h / 2f, w, h, c.R, c.G, c.B, c.A, false);
        }

        private static void Text(string s, float x, float y, float scale, Color c, GTA.UI.Font f, bool right, float wrap = 0f)
        {
            var t = new TextElement(s, new PointF(x * 1280f, y * 720f), scale, c, f, right ? Alignment.Right : Alignment.Left, false, true, wrap * 1280f);
            t.ScaledDraw();
        }

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
        public void Push(Menu m) { stack.Push(m); if (m.OnOpen != null) m.OnOpen(); }
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
