using Blish_HUD;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BhModule.Lang5
{
    public class MemService
    {
        private readonly Lang5Module module;
        public MemService(Lang5Module module)
        {
            this.module = module;
            if (GameService.GameIntegration.Gw2Instance.Gw2IsRunning) SetLangPtr();
            GameService.GameIntegration.Gw2Instance.Gw2Started += delegate { SetLangPtr(); };
        }
        public void SetUILang()
        {

        }
        private void SetLangPtr() {
            var result = Utils.FindReadonlyStringRef("ValidateLanguage(language)");
        }
    }
}
