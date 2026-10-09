using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace StrategicCampaignAI;

public sealed class SubModule : MBSubModuleBase
{
    protected override void OnSubModuleLoad()
    {
        base.OnSubModuleLoad();
        StrategicAiSettings.LoadOnce();
    }

    protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
    {
        base.OnGameStart(game, gameStarterObject);

        if (game.GameType is Campaign && gameStarterObject is CampaignGameStarter campaignStarter)
        {
            // The generic overload hands each model the one registered before it (vanilla's, or War Sails' naval
            // model) as BaseModel, so ours wrap it rather than replace it.
            campaignStarter.AddModel<TargetScoreCalculatingModel>(new StrategicTargetScoreModel());
            campaignStarter.AddModel<ArmyManagementCalculationModel>(new StrategicArmyManagementModel());
            campaignStarter.AddModel<SettlementGarrisonModel>(new StrategicGarrisonModel());
            campaignStarter.AddModel<MobilePartyAIModel>(new StrategicPartyAIModel());
            campaignStarter.AddBehavior(new StrategicCampaignAIBehavior());
        }
    }
}
