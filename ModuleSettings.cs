using Blish_HUD.Input;
using Blish_HUD.Settings;
using Microsoft.Xna.Framework.Input;

namespace BhModule.Lang5
{
    public class ModuleSettings
    {
        private readonly Lang5Module module;
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
            this.ChineseUIKey.Value.Activated += (sender, args) =>
            {
                ChineseUI.Value = !ChineseUI.Value;
                Utils.Notify.Show(ChineseUI.Value ? "Enable Chinese UI." : "Disable Chinese UI.");
            };
            MemService.OnLoaded += delegate { module.MemService.SetZhUI(ChineseUI.Value); };
            settings.DefineSetting(" ", false, () => "", () => "").SetDisabled();

            this.Cht = settings.DefineSetting(nameof(this.Cht), true, () => "Simplified to Traditional", () => "Work when Chinese UI enable.");
            this.Cht.SettingChanged += (sender, args) => { module.MemService.SetConvert(Cht.Value); };
            this.ChtJson = settings.DefineSetting(nameof(this.ChtJson), "", () => "Source", () => "Additional conversion json file path; only support English path; json format [{\"i\":\"your word\",\"o\":\"same size\"}]");
            this.ChtJson.SettingChanged += (sender, args) => { module.MemService.ReloadConverter(); };
            this.ChtKey = settings.DefineSetting(nameof(this.ChtKey), new KeyBinding(Keys.OemSemicolon), () => "Toggle Traditional Chinese", () => "");
            this.ChtKey.Value.Enabled = true;
            this.ChtKey.Value.Activated += (sender, args) =>
            {
                Cht.Value = !Cht.Value;
                Utils.Notify.Show(Cht.Value ? "Enable Simplified Chinese To Traditional Chinese." : "Disable Simplified Chinese To Traditional Chinese.");
            };
            MemService.OnLoaded += delegate { module.MemService.SetConvert(Cht.Value); };
            settings.DefineSetting("  ", false, () => "", () => "").SetDisabled();

            this.RestoreMem = settings.DefineSetting(nameof(this.RestoreMem), true, () => "Restore changed memory when module unload.", () => "When close Blish, will return back original language setting");
        }
    }
}
