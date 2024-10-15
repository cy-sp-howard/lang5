using Blish_HUD.Input;
using Blish_HUD.Settings;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BhModule.Lang5
{
    public class ModuleSettings
    {
        private readonly Lang5Module module;
        public SettingEntry<KeyBinding> ChineseUIKey { get; private set; }
        public SettingEntry<bool> ChineseUI { get; private set; }
        public ModuleSettings(Lang5Module module, SettingCollection settings)
        {
            this.module = module;
            InitUISetting(settings);
        }
        private void InitUISetting(SettingCollection settings)
        {
            this.ChineseUI = settings.DefineSetting(nameof(this.ChineseUI), false, () => "Use Chinese UI", () => "");
            this.ChineseUI.SettingChanged += (sender, args) => { module.memService.SetUILang(); };
            this.ChineseUIKey = settings.DefineSetting(nameof(this.ChineseUIKey), new KeyBinding(Keys.O), () => "Toggle Chinese UI", () => "");
            this.ChineseUIKey.Value.Enabled = true;
            this.ChineseUIKey.Value.Activated += (sender, args) =>
            {
                ChineseUI.Value = !ChineseUI.Value;
                Utils.Notify.Show(ChineseUI.Value ? "Enable Chinese UI." : "Disable Chinese UI.");
            };
        }
    }
}
