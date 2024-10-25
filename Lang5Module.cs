using Blish_HUD;
using Blish_HUD.Graphics.UI;
using Blish_HUD.Modules;
using Blish_HUD.Modules.Managers;
using Blish_HUD.Settings;
using Microsoft.Xna.Framework;
using System.ComponentModel.Composition;
using System.Runtime;
using System.Threading.Tasks;

namespace BhModule.Lang5
{
    [Export(typeof(Blish_HUD.Modules.Module))]
    public class Lang5Module : Blish_HUD.Modules.Module
    {

        private static readonly Logger Logger = Logger.GetLogger<Lang5Module>();

        #region Service Managers
        internal SettingsManager SettingsManager => this.ModuleParameters.SettingsManager;
        internal ContentsManager ContentsManager => this.ModuleParameters.ContentsManager;
        internal DirectoriesManager DirectoriesManager => this.ModuleParameters.DirectoriesManager;
        internal Gw2ApiManager Gw2ApiManager => this.ModuleParameters.Gw2ApiManager;
        #endregion
        public MemService MemService { get; private set; }
        public ModuleSettings Settings { get; private set; }
        public static Lang5Module Instance;

        [ImportingConstructor]
        public Lang5Module([Import("ModuleParameters")] ModuleParameters moduleParameters) : base(moduleParameters)
        {
            Lang5Module.Instance = this;
        }

        protected override void DefineSettings(SettingCollection settings)
        {
            this.Settings = new ModuleSettings(this, settings);
        }
        public override IView GetSettingsView()
        {
            return new Lang5SettingsView(SettingsManager.ModuleSettings);
        }

        protected override void Initialize()
        {
            this.MemService = new MemService(this);
        }

        protected override async Task LoadAsync()
        {
            this.MemService.Load();
        }

        protected override void Update(GameTime gameTime)
        {
            this.MemService.Upadate();
        }

        protected override void Unload()
        {
            this.MemService.Unload();
        }

    }

}
