using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace AlternateStart;

public sealed class SubModule : MBSubModuleBase
{
    protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
    {
        base.OnGameStart(game, gameStarterObject);

        if (game.GameType is Campaign && gameStarterObject is CampaignGameStarter campaignStarter)
        {
            campaignStarter.AddBehavior(new AlternateStartBehavior());
        }
    }

    protected override void OnApplicationTick(float dt)
    {
        base.OnApplicationTick(dt);
        AlternateStartBehavior.Instance?.Tick(dt);
    }

    public override void OnGameEnd(Game game)
    {
        base.OnGameEnd(game);
        if (game.GameType is Campaign) CreationChoice.Reset();
    }
}
