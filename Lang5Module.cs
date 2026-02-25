using Blish_HUD;
using Blish_HUD.Graphics.UI;
using Blish_HUD.Modules;
using Blish_HUD.Modules.Managers;
using Blish_HUD.Settings;
using System.ComponentModel.Composition;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace BhModule.Lang5
{
    [Export(typeof(Blish_HUD.Modules.Module))]
    public class Lang5Module : Blish_HUD.Modules.Module
    {
        public static readonly Logger Logger = Logger.GetLogger<Lang5Module>();
        #region Service Managers
        internal SettingsManager SettingsManager => this.ModuleParameters.SettingsManager;
        internal ContentsManager ContentsManager => this.ModuleParameters.ContentsManager;
        internal DirectoriesManager DirectoriesManager => this.ModuleParameters.DirectoriesManager;
        internal Gw2ApiManager Gw2ApiManager => this.ModuleParameters.Gw2ApiManager;
        #endregion
        public MemService MemService { get; private set; }
        public ModuleSettings Settings { get; private set; }
        public static Lang5Module Instance;
        public static ModuleManager InstanceManager;
        public static UpdateModule.UpdateManifest LatestManifest;
        public static bool UpdateAvailable { get => LatestManifest.Version > InstanceManager.Manifest.Version; }

        [ImportingConstructor]
        public Lang5Module([Import("ModuleParameters")] ModuleParameters moduleParameters) : base(moduleParameters)
        {
            Instance = this;
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
            InstanceManager = GameService.Module.Modules.FirstOrDefault(m => m.ModuleInstance == this);
            CheckUpdate().ContinueWith(_ =>
            {
                if (Settings.AutoUpdate.Value && UpdateAvailable) UpdateSelf();
                else MemService = new MemService(this);
            });
        }
        protected override void Unload()
        {
            Settings.Unload();
            MemService?.Unload();
        }
        async public Task<bool> CheckUpdate()
        {
            if (LatestManifest != null) return UpdateAvailable;
            LatestManifest = new(InstanceManager.Manifest);
            HttpClient client = new();
            client.DefaultRequestHeaders.Add("User-Agent", "BlishHUD/Lang5");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");

            try
            {
                string latestReleaseResp = await client.GetStringAsync("https://api.github.com/repos/cy-sp-howard/lang5/releases/latest");
                var latestRelease = JsonSerializer.Deserialize<UpdateModule.Release>(latestReleaseResp);
                SemVer.Version latestVersion = new(latestRelease.Verison);
                LatestManifest.SetValue("Version", latestVersion);
                if (UpdateAvailable)
                {
                    var file = latestRelease.Files.Find(f => f.Name.Contains(".bhm"));
                    if (file != null)
                    {
                        LatestManifest.Hash = file.Hash.Substring(7);
                        LatestManifest.Location = file.Url;
                    }
                }
                ;
            }
            catch { }
            return UpdateAvailable;
        }
        public void UpdateSelf()
        {
            if (!UpdateAvailable) return;
            if (MemService is not null)
            {
                MemService.ForceRestoreMem = true;
            }
            GameService.Module.ModulePkgRepoHandler.ReplacePackage(LatestManifest, InstanceManager).ContinueWith(_ =>
            {
                GameService.Overlay.Restart();
            });
        }

    }

}
