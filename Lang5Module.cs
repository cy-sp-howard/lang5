using Blish_HUD;
using Blish_HUD.Modules;
using Blish_HUD.Modules.Managers;
using Blish_HUD.Settings;
using Microsoft.Xna.Framework;
using System.ComponentModel.Composition;
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
        public MemService memService { get; private set; }
        public ModuleSettings Settings { get; private set; }
        public ModuleParameters Parameters { get; private set; }

        [ImportingConstructor]
        public Lang5Module([Import("ModuleParameters")] ModuleParameters moduleParameters) : base(moduleParameters)
        {
            Parameters = moduleParameters;
        }

        protected override void DefineSettings(SettingCollection settings)
        {
            this.Settings = new ModuleSettings(this, settings);
        }

        // game attached run; use --module "**.bhm"   will call when Blish BeginRun()

        protected override void Initialize()
        {


        }

        //game attached run; use --module "**.bhm"   will call when Blish BeginRun()
        protected override async Task LoadAsync()
        {
            this.memService = new MemService(this);
        }

        protected override void Update(GameTime gameTime)
        {
            this.memService.Upadate();
            var a = GameService.GameIntegration.Gw2Instance.Gw2Process;
        }

        /// <inheritdoc />
        protected override void Unload()
        {

            this.memService.Unload();
        }

    }

}
