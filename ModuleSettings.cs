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
        readonly Lang5Module _module;
        public SettingEntry<bool> AutoUpdate { get; private set; }
        public SettingEntry<KeyBinding> ChineseUIKey { get; private set; }
        public SettingEntry<bool> ChineseUI { get; private set; }
        public SettingEntry<KeyBinding> ChtKey { get; private set; }
        public SettingEntry<bool> Cht { get; private set; }
        public SettingEntry<string> ChtJson { get; private set; }
        public SettingEntry<bool> RestoreMem { get; private set; }
        public ModuleSettings(Lang5Module module, SettingCollection settings)
        {
            _module = module;
            InitUISetting(settings);
        }
        void InitUISetting(SettingCollection settings)
        {
            ChineseUI = settings.DefineSetting(nameof(ChineseUI), true, () => "Use Chinese UI", () => "");
            ChineseUI.SettingChanged += ApplyLang;
            ChineseUIKey = settings.DefineSetting(nameof(ChineseUIKey), new KeyBinding(Keys.P), () => "Toggle Chinese UI", () => "");
            ChineseUIKey.Value.Enabled = true;
            ChineseUIKey.Value.Activated += ToggleLang;
            MemService.OnLoaded += delegate { _module.MemService?.SetZhUI(ChineseUI.Value); };

            Cht = settings.DefineSetting(nameof(Cht), true, () => "Simplified Chinese to Traditional Chinese", () => "Work when Chinese UI enable.");
            Cht.SettingChanged += ApplyChtCoverter;
            ChtJson = settings.DefineSetting(nameof(ChtJson), "", () => "Additional Source", () => "Additional conversion source json path; ENG PATH ONLY; \r\njson format: \r\n[{ \"i\" : \"ZHS\",\r\n  \"o\" : \"ZHT\" }]");
            ChtJson.SettingChanged += ReloadJson;
            ChtJson.SetValidation(ValidateJson);
            ChtKey = settings.DefineSetting(nameof(ChtKey), new KeyBinding(Keys.OemSemicolon), () => "Toggle Traditional Chinese", () => "");
            ChtKey.Value.Enabled = true;
            ChtKey.Value.Activated += ToggleChinese;
            MemService.OnLoaded += delegate { _module.MemService?.SetConvert(Cht.Value); };

            AutoUpdate = settings.DefineSetting(nameof(AutoUpdate), false, () => "Auto Update", () => "");
            RestoreMem = settings.DefineSetting(nameof(RestoreMem), false, () => "Restore Language Setting, When Blish-HUD Closed.", () => "If unchecked then close Blish-HUD, You'll need restart your game to allow Blish-HUD to recontrolled the language setting");
        }
        public void ReloadJson(object sender, EventArgs args)
        {
            Lang5SettingsView.SetMsg(_module.MemService?.ReloadConverter() == 0 ? "" : "Now loading, retry later please.");
        }
        void ApplyLang(object sender, EventArgs args)
        {
            _module.MemService?.SetZhUI(ChineseUI.Value);
        }
        void ApplyChtCoverter(object sender, EventArgs args)
        {
            _module.MemService?.SetConvert(Cht.Value);
        }
        void ToggleChinese(object sender, EventArgs args)
        {
            Cht.Value = !Cht.Value;
            Utils.Notify.Show(Cht.Value ? "Enable Simplified Chinese To Traditional Chinese." : "Disable Simplified Chinese To Traditional Chinese.");
        }
        void ToggleLang(object sender, EventArgs args)
        {
            ChineseUI.Value = !ChineseUI.Value;
            Utils.Notify.Show(ChineseUI.Value ? "Enable Chinese UI." : "Disable Chinese UI.");
        }
        public void Unload()
        {
            ChtKey.Value.Activated -= ToggleChinese;
            ChineseUIKey.Value.Activated -= ToggleLang;
            ChineseUI.SettingChanged -= ApplyLang;
            Cht.SettingChanged -= ApplyChtCoverter;
            ChtJson.SettingChanged -= ReloadJson;
            Lang5SettingsView.DisposeRootflowPanel?.Invoke();
        }
        SettingValidationResult ValidateJson(string path)
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
        static Padding _messagePadding;
        static UpdateButton _updateButton;
        static public Action DisposeRootflowPanel;
        FlowPanel _rootflowPanel;
        readonly SettingCollection _settings = settings;
        protected override void Build(Container buildPanel)
        {
            DisposeRootflowPanel?.Invoke();
            _rootflowPanel = new FlowPanel()
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
            DisposeRootflowPanel = () =>
            {
                DisposeRootflowPanel = null;
                _rootflowPanel.Dispose();
            };
            _messagePadding = new Padding() { Parent = _rootflowPanel };
            _updateButton = new UpdateButton(_rootflowPanel);

            foreach (var setting in _settings.Where(s => s.SessionDefined))
            {
                IView settingView;

                if ((settingView = SettingView.FromType(setting, _rootflowPanel.Width)) != null)
                {
                    ViewContainer container = new()
                    {
                        WidthSizingMode = SizingMode.Fill,
                        HeightSizingMode = SizingMode.AutoSize,
                        Parent = _rootflowPanel
                    };
                    if (settingView is not SettingsView) container.Show(settingView);
                    switch (setting.EntryKey)
                    {
                        case "ChtKey":
                        case "ChineseUIKey":
                            new Padding() { Parent = _rootflowPanel };
                            break;
                        case "ChtJson":
                            container.WidthSizingMode = SizingMode.AutoSize;
                            container.Parent = new FlowPanel()
                            {
                                Parent = _rootflowPanel,
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
                            }.Click += delegate { Lang5Module.Instance.Settings.ReloadJson(this, EventArgs.Empty); };
                            break;
                    }
                }
            }
        }
        public static void SetMsg(string text)
        {
            if (_messagePadding == null) return;
            if (text == "" && Lang5Module.UpdateAvailable)
            {
                _messagePadding.Hide();
                _updateButton.Show();
            }
            else if (text != "" && !_messagePadding.Visible)
            {
                _messagePadding.Show();
                _updateButton.Hide();
            }
            _messagePadding._message = text;
        }
        class Padding : Control
        {
            public string _message = "";
            public Padding(int height = 16)
            {
                Size = new Point(0, height);
            }
            protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
            {
                Width = Parent.Width;
                if (_message == "") return;
                spriteBatch.DrawStringOnCtrl(this, _message, GameService.Content.DefaultFont14, new Rectangle(0, 0, Width, Height), Color.Red, false, false, 1, HorizontalAlignment.Center, VerticalAlignment.Middle);
            }
        }
        class UpdateButton : Control
        {
            readonly StandardButton _button = new()
            {
                BasicTooltipText = "Blish-HUD will restart immediately",
                Text = "Update",
                Width = 70
            };
            readonly FlowPanel _container = new()
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
                    _messagePadding.Hide();
                }
                _container.Parent = parent;
                Parent = _container;
                _button.Parent = _container;
                _button.Click += delegate { Lang5Module.Instance.UpdateSelf(); };
            }
            protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
            {
                string msg = "New Version Availible !";
                Width = 14 * msg.Length / 2;
                Height = _button.Height;
                spriteBatch.DrawStringOnCtrl(this, msg, GameService.Content.DefaultFont14, new Rectangle(0, 0, 0, Height), Color.Red, false, false, 1, HorizontalAlignment.Left, VerticalAlignment.Middle);
            }
            new public void Hide()
            {
                _container.Hide();
            }
            new public void Show()
            {
                _container.Show();
            }
        }
    }
}
