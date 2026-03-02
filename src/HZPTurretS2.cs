
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Plugins;
using Microsoft.Extensions.Logging;



namespace HZPTurretS2;

[PluginMetadata(
    Id = "HZPTurretS2",
    Version = "1.0.1",
    Name = "H-AN ZP炮塔/H-AN ZPTurret",
    Author = "H-AN",
    Description = "CS2 ZP炮塔/CS2 ZPTurret")]

public partial class HanTurretS2(ISwiftlyCore core) : BasePlugin(core)
{
    private ServiceProvider? ServiceProvider { get; set; }
    private HanTurretCommands _Commands = null!;
    private HanTurretEvents _Events = null!;
    private IOptionsMonitor<HanTurretS2Config> _turretCFGMonitor = null!;
    private IOptionsMonitor<HanTurretS2MainConfig> _turretMainCFGMonitor = null!;
    public override void Load(bool hotReload)
    {
        Core.Configuration.InitializeJsonWithModel<HanTurretS2MainConfig>("HZPTurretS2MainConfig.jsonc", "HZPTurretS2MainCFG").Configure(builder =>
        {
            builder.AddJsonFile("HZPTurretS2MainConfig.jsonc", false, true);
        });

        Core.Configuration.InitializeJsonWithModel<HanTurretS2Config>("HZPTurretS2.jsonc", "HZPTurretS2CFG").Configure(builder =>
        {
            builder.AddJsonFile("HZPTurretS2.jsonc", false, true);
        });

        var collection = new ServiceCollection();
        collection.AddSwiftly(Core);

        collection
            .AddOptionsWithValidateOnStart<HanTurretS2MainConfig>()
            .BindConfiguration("HZPTurretS2MainCFG");

        collection
            .AddOptionsWithValidateOnStart<HanTurretS2Config>()
            .BindConfiguration("HZPTurretS2CFG");

        collection.AddSingleton<HanTurretGlobals>();
        collection.AddSingleton<HanTurretS2Service>();
        collection.AddSingleton<HanTurretAIService>();
        collection.AddSingleton<HanTurretCombatService>();
        collection.AddSingleton<HanTurretEffectService>();
        collection.AddSingleton<HanTurretCommands>();
        collection.AddSingleton<HanTurretHelpers>();
        collection.AddSingleton<HanTurretEvents>();
        collection.AddSingleton<HanTurretMenuHelper>();
        collection.AddSingleton<HanTurretMenu>();

        ServiceProvider = collection.BuildServiceProvider();

        _Commands = ServiceProvider.GetRequiredService<HanTurretCommands>();
        _Events = ServiceProvider.GetRequiredService<HanTurretEvents>();

        var Globals = ServiceProvider.GetRequiredService<HanTurretGlobals>();
        var Helpers = ServiceProvider.GetRequiredService<HanTurretHelpers>();
        var Menus = ServiceProvider.GetRequiredService<HanTurretMenu>();
        var MenusHelper = ServiceProvider.GetRequiredService<HanTurretMenuHelper>();
        var Service = ServiceProvider.GetRequiredService<HanTurretS2Service>();
        var TurretAIService = ServiceProvider.GetRequiredService<HanTurretAIService>();
        var TurretCombatService = ServiceProvider.GetRequiredService<HanTurretCombatService>();
        var TurretEffectService = ServiceProvider.GetRequiredService<HanTurretEffectService>();

        _turretMainCFGMonitor = ServiceProvider.GetRequiredService<IOptionsMonitor<HanTurretS2MainConfig>>();
        _turretCFGMonitor = ServiceProvider.GetRequiredService<IOptionsMonitor<HanTurretS2Config>>();
        
        _turretMainCFGMonitor.OnChange(newConfig =>
        {
            Core.Logger.LogInformation($"{Core.Localizer["ServerCfgChange"]}");
        });

        _turretCFGMonitor.OnChange(newConfig =>
        {
            Core.Logger.LogInformation($"{Core.Localizer["ServerCfgChange"]}");
        });

        _Commands.Commands();
        _Events.HookEvents();
    }

    public override void Unload()
    {
        ServiceProvider!.Dispose();
    }

}