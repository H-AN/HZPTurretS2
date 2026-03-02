

using System.Numerics;
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
using static System.Net.Mime.MediaTypeNames;

namespace HZPTurretS2;

public class HanTurretEvents
{
    private readonly ILogger<HanTurretEvents> _logger;
    private readonly ISwiftlyCore _core;
    private readonly IOptionsMonitor<HanTurretS2MainConfig> _mainconfig;
    private readonly IOptionsMonitor<HanTurretS2Config> _config;
    private readonly HanTurretGlobals _globals;
    private readonly HanTurretAIService _aiservice;
    private readonly HanTurretHelpers _helpers;
    private readonly HanTurretEffectService _effect;


    public HanTurretEvents(ISwiftlyCore core, ILogger<HanTurretEvents> logger,
        IOptionsMonitor<HanTurretS2Config> config, HanTurretGlobals globals,
        IOptionsMonitor<HanTurretS2MainConfig> mainconfig, HanTurretAIService aiservice,
        HanTurretHelpers helpers, HanTurretEffectService Effect)
    {
        _core = core;
        _logger = logger;
        _config = config;
        _globals = globals;
        _mainconfig = mainconfig;
        _aiservice = aiservice;
        _helpers = helpers;
        _effect = Effect;
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
        if (@event.Info.DamageType != DamageTypes_t.DMG_SLASH)
            return;

        var victim = @event.Entity;
        if (victim == null || !victim.IsValid)
            return;

        var vEntity = victim.Entity;
        if (vEntity == null || !vEntity.IsValid)
            return;

        if (string.IsNullOrEmpty(vEntity.Name) || !vEntity.Name.StartsWith("华仔炮塔_"))
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

        var _zpAPI = HanTurretS2._zpApi;
        if (_zpAPI == null)
            return;


        var phy = victim.As<CPhysicsPropOverride>();
        if (phy == null || !phy.IsValid || !phy.IsValidEntity)
            return;

        uint hitRaw = _core.EntitySystem.GetRefEHandle(phy).Raw;
        uint finalPhyRaw = hitRaw; 


        if (_globals.TurretHeadToPhysics.TryGetValue(hitRaw, out uint mainFromHead))
            finalPhyRaw = mainFromHead;
        else if (_globals.TurretBaseToPhysics.TryGetValue(hitRaw, out uint mainFromBase))
            finalPhyRaw = mainFromBase;


        var phyHandle = new CHandle<CPhysicsPropOverride>(finalPhyRaw);
        var phyEntity = phyHandle.Value;

        if (phyEntity == null || !phyEntity.IsValid) return;


        // _logger.LogInformation($"1 命中部位: {vEntity.Name}, 实际指向主体: {phyEntity.Entity.Name}");
        

        if (_globals.TurretPartsMap.TryGetValue(finalPhyRaw, out var parts))
        {

            bool attackerIsZombie = _zpAPI.HZP_IsZombie(AttackerPlayer.PlayerID);
            int amount = (int)@event.Info.Damage; 
            if (attackerIsZombie)
            {
                if (_globals.TurretData.TryGetValue(finalPhyRaw, out var turretData) && turretData.Canbreakage)
                {
                    phyEntity.Health -= (int)@event.Info.Damage;
                    phyEntity.HealthUpdated();

                    _helpers.EmitSoundFromPhyEntity(phyHandle, "Breakable.MatMetal");
                    //_logger.LogInformation($"2 攻击者 {AttackerPlayer.Name}, 主体剩余血量 {phyEntity.Health}");

                    if (phyEntity.Health <= 0)
                    {
                        _aiservice.UnlinkTurret(phyHandle.Raw);
                        _aiservice.KillTurret(phyHandle);
                        
                    }
 
                }
            }
            else
            {
                if (_globals.TurretData.TryGetValue(finalPhyRaw, out var turretData) && turretData.CanFixes)
                {
                    if (phyEntity.Health < phyEntity.MaxHealth)
                    {
                        phyEntity.Health += amount;

                        if (phyEntity.Health > phyEntity.MaxHealth)
                            phyEntity.Health = phyEntity.MaxHealth;

                        phyEntity.HealthUpdated();

                        _helpers.EmitSoundFromPhyEntity(phyHandle, "SolidMetal.BulletImpact");
                    }
                }
                
            }
            ShowTurretInfo(AttackerPlayer, phyHandle);
            _effect.CreateParticleAtPos(phyHandle, "particles/explosions_fx/explosion_c4_interior_sparktrails.vpcf");

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
        @event.AddItem("soundevents/game_sounds_physics.vsndevts");
        @event.AddItem("soundevents/game_sounds_weapons.vsndevts");
        @event.AddItem("particles/explosions_fx/explosion_c4_short.vpcf");
        @event.AddItem("particles/explosions_fx/explosion_c4_interior_sparktrails.vpcf");

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
        _globals.TurretToPlayer.Clear();
        _globals.TurretOwner.Clear();         
        _globals.TurretPartsMap.Clear();       
        _globals.TurretHeadToPhysics.Clear();  
        _globals.TurretBaseToPhysics.Clear();  

    }

    private HookResult OnRoundStart(EventRoundStart @event)
    {
        _globals.TurretCanFire = true;
        _globals.sentryParticles.Clear();
        _globals.TurretData.Clear();
        _globals.PlayerTurretCounts.Clear();
        _globals.TurretToPlayer.Clear();
        _globals.TurretOwner.Clear();
        _globals.TurretPartsMap.Clear();
        _globals.TurretHeadToPhysics.Clear();
        _globals.TurretBaseToPhysics.Clear();

        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd @event)
    {
        _globals.TurretCanFire = false;
        _globals.sentryParticles.Clear();
        _globals.TurretData.Clear();
        _globals.PlayerTurretCounts.Clear();
        _globals.TurretToPlayer.Clear();
        _globals.TurretOwner.Clear();
        _globals.TurretPartsMap.Clear();
        _globals.TurretHeadToPhysics.Clear();
        _globals.TurretBaseToPhysics.Clear();

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

    public void ShowTurretInfo(IPlayer player, CHandle<CPhysicsPropOverride> sentryHandle) 
    {
        if(!_mainconfig.CurrentValue.ShowTurretInfo)
            return;

        if (!sentryHandle.IsValid)
            return;

        if (player == null || !player.IsValid || player.IsFakeClient || !player.IsAlive)
            return;

        var Sentry = sentryHandle.Value;
        if (Sentry == null || !Sentry.IsValid || !Sentry.IsValidEntity)
            return;

        var SentryOwner = _globals.TurretToPlayer.TryGetValue(sentryHandle.Raw, out var ownerId);
        var owner = _core.PlayerManager.GetPlayer(ownerId);
        if (owner == null || !owner.IsValid || owner.IsFakeClient)
            return;

        _globals.TurretData.TryGetValue(sentryHandle.Raw, out var turretData);
        if(turretData == null)
            return;

        bool canbreakage = turretData.Canbreakage;
        bool canfix = turretData.CanFixes;

        string breakagemessage = canbreakage ? $"{_core.Translation.GetPlayerLocalizer(player)["TurretHudCanBreakage"]}" : $"{_core.Translation.GetPlayerLocalizer(player)["TurretHudCantBreakage"]}";
        string canfixmessage = canfix ? $"{_core.Translation.GetPlayerLocalizer(player)["TurretHudCanFix"]}" : $"{_core.Translation.GetPlayerLocalizer(player)["TurretHudCantFix"]}";
        string message = $"<span><font color='red'> {_core.Translation.GetPlayerLocalizer(player)["TurretHudOwnerPlayer", owner.Name, turretData.Name]}</font></span><br>" +
         $"<span><font color='orange'>{_core.Translation.GetPlayerLocalizer(player)["TurretHudLeftHealth"]} </font><font color='red'>{Sentry.Health}</font></span><br>" +
         $"<span><font color='orange'>{_core.Translation.GetPlayerLocalizer(player)["TurretHudMaxHealth"]} </font><font color='red'>{Sentry.MaxHealth}</font></span><br>" +
         $"<span><font color='green'>{breakagemessage} </font></span><br>" +
         $"<span><font color='green'>{canfixmessage} </font></span><br>";

        player.SendCenterHTMLAsync(message);

    }
}