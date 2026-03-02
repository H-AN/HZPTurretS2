

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mono.Cecil.Cil;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Sounds;

namespace HZPTurretS2;

public class HanTurretEvents
{
    private readonly ILogger<HanTurretEvents> _logger;
    private readonly ISwiftlyCore _core;
    private readonly IOptionsMonitor<HanTurretS2MainConfig> _mainconfig;
    private readonly IOptionsMonitor<HanTurretS2Config> _config;
    private readonly HanTurretGlobals _globals;
    private readonly HanTurretAIService _aiservice;

    public HanTurretEvents(ISwiftlyCore core, ILogger<HanTurretEvents> logger,
        IOptionsMonitor<HanTurretS2Config> config, HanTurretGlobals globals,
        IOptionsMonitor<HanTurretS2MainConfig> mainconfig, HanTurretAIService aiservice)
    {
        _core = core;
        _logger = logger;
        _config = config;
        _globals = globals;
        _mainconfig = mainconfig;
        _aiservice = aiservice;
    }

    public void HookEvents()
    {
        _core.Event.OnPrecacheResource += Event_OnPrecacheResource;

        _core.GameEvent.HookPre<EventRoundStart>(OnRoundStart);
        _core.GameEvent.HookPre<EventRoundEnd>(OnRoundEnd);

        _core.GameEvent.HookPre<EventPlayerDeath>(OnPlayerDeath);

        _core.Event.OnMapUnload += Event_OnMapUnload;
        _core.Event.OnClientConnected += Event_OnClientConnected;
        _core.Event.OnClientDisconnected += Event_OnClientDisconnected;

        _core.Event.OnEntityTakeDamage += Event_OnEntityTakeDamage;
    }

    private void Event_OnEntityTakeDamage(SwiftlyS2.Shared.Events.IOnEntityTakeDamageEvent @event)
    {
        var victim = @event.Entity;
        if (victim == null || !victim.IsValid)
            return;

        var attacker = @event.Info.Attacker.Value;
        if (attacker == null || !attacker.IsValid)
            return;

        var AttackerPawn = attacker.As<CCSPlayerPawn>();
        if (AttackerPawn == null || !AttackerPawn.IsValid)
            return;

        var AttackerPlayer = _core.PlayerManager.GetPlayerFromPawn(AttackerPawn);
        if (AttackerPlayer == null || !AttackerPlayer.IsValid)
            return;

        var vEntity = victim.Entity;
        if (vEntity == null || !vEntity.IsValid)
            return;

        if (string.IsNullOrEmpty(vEntity.Name) || !vEntity.Name.StartsWith("华仔炮塔_"))
            return;

        var phy = victim.As<CPhysicsPropOverride>();
        if (phy == null || !phy.IsValid || !phy.IsValidEntity)
            return;

        _logger.LogInformation($"攻击者 {AttackerPlayer.Name}, 被攻击者 {victim.DesignerName} {victim.Entity.Name} 伤害 {@event.Info.Damage}");
        _logger.LogInformation($"最大血量  {phy.MaxHealth} 血量 {phy.Health}");
        uint phyRaw = _core.EntitySystem.GetRefEHandle(phy).Raw;


        if (_globals.TurretPartsMap.TryGetValue(phyRaw, out var parts))
        {
            var headHandle = new CHandle<CBaseModelEntity>(parts.head);
            var baseHandle = new CHandle<CBaseModelEntity>(parts.baseEnt);

            var phyHandle = new CHandle<CPhysicsPropOverride>(phyRaw);
            _aiservice.KillTurret(phyHandle);

            _globals.TurretPartsMap.Remove(phyRaw);
        }

    }

    private void Event_OnClientDisconnected(SwiftlyS2.Shared.Events.IOnClientDisconnectedEvent @event)
    {
        var playerId = @event.PlayerId;
        if (_globals.PlayerSteamCache.TryGetValue(playerId, out var steamID))
        {
            _aiservice.RemoveAllPlayerTurrets(playerId, steamID);
            _globals.PlayerSteamCache.Remove(playerId);
        }
        else
        {
            _aiservice.RemoveAllPlayerTurrets(playerId, 0);
        }

    }

    private void Event_OnClientConnected(SwiftlyS2.Shared.Events.IOnClientConnectedEvent @event)
    {
        var playerId = @event.PlayerId;
        var player = _core.PlayerManager.GetPlayer(playerId);
        if (player == null || !player.IsValid)
            return;

        _globals.PlayerSteamCache[player.PlayerID] = player.SteamID;
    }

    private void Event_OnPrecacheResource(SwiftlyS2.Shared.Events.IOnPrecacheResourceEvent @event)
    {
        @event.AddItem("models/stk_sentry_guns/sentry/sentry_physbox.vmdl");
        @event.AddItem("models/stk_sentry_guns/sentry/base.vmdl");

        var maincfg = _mainconfig.CurrentValue;
        if (!string.IsNullOrEmpty(maincfg.TurretBaseModel))
        {
            @event.AddItem(maincfg.TurretBaseModel);
        }
        if (!string.IsNullOrEmpty(maincfg.TurretPhysboxModel))
        {
            @event.AddItem(maincfg.TurretPhysboxModel);
        }

        var turretList = _config.CurrentValue.TurretList;
        if (turretList != null && turretList.Count > 0)
        {
            foreach (var bturretox in turretList)
            {
                if (!string.IsNullOrEmpty(bturretox.Model))
                {
                    @event.AddItem(bturretox.Model);
                }
                if (!string.IsNullOrEmpty(bturretox.PrecacheSoundEvent))
                {
                    @event.AddItem(bturretox.PrecacheSoundEvent);
                }
                if (!string.IsNullOrEmpty(bturretox.MuzzleParticle))
                {
                    @event.AddItem(bturretox.MuzzleParticle);
                }
            }
        }

    }

    private void Event_OnMapUnload(SwiftlyS2.Shared.Events.IOnMapUnloadEvent @event)
    {
        _globals.TurretCanFire = false;
        _globals.sentryParticles.Clear();
        _globals.TurretData.Clear();
        _globals.PlayerTurretCounts.Clear();
    }

    private HookResult OnRoundStart(EventRoundStart @event)
    {
        _globals.TurretCanFire = true;
        _globals.sentryParticles.Clear();
        _globals.TurretData.Clear();
        _globals.PlayerTurretCounts.Clear();

        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd @event)
    {
        _globals.TurretCanFire = false;
        _globals.sentryParticles.Clear();
        _globals.TurretData.Clear();
        _globals.PlayerTurretCounts.Clear();

        return HookResult.Continue;
    }

    private HookResult OnPlayerDeath(EventPlayerDeath @event)
    {
        var player = @event.UserIdPlayer;
        if (player == null || !player.IsValid)
            return HookResult.Continue;

        _aiservice.RemoveAllPlayerTurrets(player.PlayerID, player.SteamID);

        return HookResult.Continue;
    }

}