using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Graphics.UI;
using Blish_HUD.Input;
using Blish_HUD.Modules;
using Blish_HUD.Settings;
using Blish_HUD.Settings.UI.Views;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Linq;

namespace BhModule.Lang5
{
    public class ModuleSettings
    {
        private readonly Lang5Module module;
        public SettingEntry<bool> AutoUpdate { get; private set; }
        public SettingEntry<KeyBinding> ChineseUIKey { get; private set; }
        public SettingEntry<bool> ChineseUI { get; private set; }
        public SettingEntry<KeyBinding> ChtKey { get; private set; }
        public SettingEntry<bool> Cht { get; private set; }
        public SettingEntry<string> ChtJson { get; private set; }
        public SettingEntry<bool> RestoreMem { get; private set; }
        public ModuleSettings(Lang5Module module, SettingCollection settings)
        {
            this.module = module;
            InitUISetting(settings);
        }
        private void InitUISetting(SettingCollection settings)
        {
            this.ChineseUI = settings.DefineSetting(nameof(this.ChineseUI), true, () => "Use Chinese UI", () => "");
            this.ChineseUI.SettingChanged += (sender, args) => { module.MemService.SetZhUI(ChineseUI.Value); };
            this.ChineseUIKey = settings.DefineSetting(nameof(this.ChineseUIKey), new KeyBinding(Keys.P), () => "Toggle Chinese UI", () => "");
            this.ChineseUIKey.Value.Enabled = true;
            this.ChineseUIKey.Value.Activated += ToggleLang;
            MemService.OnLoaded += delegate { module.MemService.SetZhUI(ChineseUI.Value); };

            this.Cht = settings.DefineSetting(nameof(this.Cht), true, () => "Simplified Chinese to Traditional Chinese", () => "Work when Chinese UI enable.");
            this.Cht.SettingChanged += (sender, args) => { module.MemService.SetConvert(Cht.Value); };
            this.ChtJson = settings.DefineSetting(nameof(this.ChtJson), "", () => "Source", () => "Additional conversion source json path; ENG PATH ONLY; \r\njson format: \r\n[{ \"i\" : \"ZHS\",\r\n  \"o\" : \"ZHT\" }]");
            this.ChtJson.SettingChanged += (sender, args) => { ReloadJson(); };
            this.ChtJson.SetValidation(ValidateJson);
            this.ChtKey = settings.DefineSetting(nameof(this.ChtKey), new KeyBinding(Keys.OemSemicolon), () => "Toggle Traditional Chinese", () => "");
            this.ChtKey.Value.Enabled = true;
            this.ChtKey.Value.Activated += ToggleChinese;
            MemService.OnLoaded += delegate { module.MemService.SetConvert(Cht.Value); };

            this.AutoUpdate = settings.DefineSetting(nameof(this.AutoUpdate), false, () => "Auto Update", () => "");
            this.RestoreMem = settings.DefineSetting(nameof(this.RestoreMem), true, () => "Restore Language Setting, When Blish-HUD Closed.", () => "If unchecked then close Blish-HUD, You'll need restart your game to allow Blish-HUD to recontrolled the language setting");
        }
        public void ReloadJson()
        {
            Lang5SettingsView.SetMsg(module.MemService.ReloadConverter() == 0 ? "" : "Now loading, retry later please.");
        }
        private void ToggleChinese(object sender, EventArgs args)
        {
            Cht.Value = !Cht.Value;
            Utils.Notify.Show(Cht.Value ? "Enable Simplified Chinese To Traditional Chinese." : "Disable Simplified Chinese To Traditional Chinese.");
        }
        private void ToggleLang(object sender, EventArgs args)
        {
            ChineseUI.Value = !ChineseUI.Value;
            Utils.Notify.Show(ChineseUI.Value ? "Enable Chinese UI." : "Disable Chinese UI.");
        }
        public void Unload()
        {
            this.ChtKey.Value.Activated -= ToggleChinese;
            this.ChineseUIKey.Value.Activated -= ToggleLang;
        }
        private SettingValidationResult ValidateJson(string path)
        {
            Lang5SettingsView.SetMsg("");
            if (path == "") return new(true);
            try
            {
                Utils.GetJson<TextJson.TextJsonItem[]>(path);
                return new(true);
            }
            catch (Exception e)
            {
                Lang5SettingsView.SetMsg(e.Message);
                return new(false, e.Message);
            }
        }
    }
    public class Lang5SettingsView(SettingCollection settings) : View
    {
        static Padding messagePadding;
        static UpdateButton updateButton;
        FlowPanel rootflowPanel;
        readonly SettingCollection settings = settings;
        protected override void Build(Container buildPanel)
        {
            rootflowPanel = new FlowPanel()
            {
                Size = buildPanel.Size,
                FlowDirection = ControlFlowDirection.SingleTopToBottom,
                ControlPadding = new Vector2(5, 2),
                OuterControlPadding = new Vector2(10, 15),
                WidthSizingMode = SizingMode.Fill,
                HeightSizingMode = SizingMode.Standard,
                AutoSizePadding = new Point(0, 15),
                Parent = buildPanel
            };
            messagePadding = new Padding() { Parent = rootflowPanel };
            updateButton = new UpdateButton(rootflowPanel);

            foreach (var setting in settings.Where(s => s.SessionDefined))
            {
                IView settingView;

                if ((settingView = SettingView.FromType(setting, rootflowPanel.Width)) != null)
                {
                    ViewContainer container = new()
                    {
                        WidthSizingMode = SizingMode.Fill,
                        HeightSizingMode = SizingMode.AutoSize,
                        Parent = rootflowPanel
                    };
                    if (!(settingView is SettingsView)) container.Show(settingView);
                    switch (setting.EntryKey)
                    {
                        case "ChtKey":
                        case "ChineseUIKey":
                            new Padding() { Parent = rootflowPanel };
                            break;
                        case "ChtJson":
                            container.WidthSizingMode = SizingMode.AutoSize;
                            container.Parent = new FlowPanel()
                            {
                                Parent = rootflowPanel,
                                FlowDirection = ControlFlowDirection.LeftToRight,
                                WidthSizingMode = SizingMode.Fill,
                                HeightSizingMode = SizingMode.AutoSize,
                                ControlPadding = new Vector2(10, 0),
                            };
                            new StandardButton()
                            {
                                Parent = container.Parent,
                                Text = "Reload",
                                Width = 70,
                            }.Click += delegate { Lang5Module.Instance.Settings.ReloadJson(); };
                            break;
                    }
                }
            }

            rootflowPanel.ShowBorder = true;
            rootflowPanel.CanCollapse = true;
        }
        public static void SetMsg(string text)
        {
            if (Lang5SettingsView.messagePadding == null) return;
            if (text == "" && Lang5Module.UpdateAvailable)
            {
                Lang5SettingsView.messagePadding.Hide();
                Lang5SettingsView.updateButton.Show();
            }
            else if (text != "" && !Lang5SettingsView.messagePadding.Visible)
            {
                Lang5SettingsView.messagePadding.Show();
                Lang5SettingsView.updateButton.Hide();
            }
            Lang5SettingsView.messagePadding.message = text;
        }
        private class Padding : Control
        {
            public string message = "";
            public Padding(int height = 16)
            {
                Size = new Point(0, height);
            }
            protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
            {
                Width = Parent.Width;
                if (message == "") return;
                spriteBatch.DrawStringOnCtrl(this, message, GameService.Content.DefaultFont14, new Rectangle(0, 0, Width, Height), Color.Red, false, false, 1, HorizontalAlignment.Center, VerticalAlignment.Middle);
            }
        }
        private class UpdateButton : Control
        {
            readonly private ModuleManager moduleManager = GameService.Module.Modules.FirstOrDefault(m => m.ModuleInstance == Lang5Module.Instance);
            private StandardButton button = new()
            {
                Text = "Update",
                Width = 70
            };
            private FlowPanel container = new()
            {
                ShowBorder = false,
                FlowDirection = ControlFlowDirection.LeftToRight,
                WidthSizingMode = SizingMode.Fill,
                HeightSizingMode = SizingMode.AutoSize,
            };
            public UpdateButton(Container parent)
            {
                Hide();
                if (Lang5Module.UpdateAvailable)
                {
                    Show();
                    messagePadding.Hide();
                }
                container.Parent = parent;
                Parent = container;
                button.Parent = container;
                button.Click += delegate { _ = Lang5Module.Instance.UpdateSelf(); };

            }
            protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
            {
                string msg = "New Version Availible !";
                Width = 14 * msg.Length / 2;
                Height = button.Height;
                spriteBatch.DrawStringOnCtrl(this, msg, GameService.Content.DefaultFont14, new Rectangle(0, 0, 0, Height), Color.Red, false, false, 1, HorizontalAlignment.Left, VerticalAlignment.Middle);
            }
            new public void Hide()
            {
                container.Hide();
            }
            new public void Show()
            {
                container.Show();
            }
        }
    }
}
