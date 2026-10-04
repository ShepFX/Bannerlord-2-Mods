using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterCreationContent;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace AlternateStart;

/// <summary>
/// Hooks the creation pages into character creation, then applies the chosen start once the campaign map is up:
/// settlements, kingdoms and parties are only safe to change after creation has finished and the map has opened.
/// </summary>
public sealed class AlternateStartBehavior : CampaignBehaviorBase
{
    private const float MapSettleSeconds = 0.5f;

    internal static AlternateStartBehavior? Instance { get; private set; }

    private bool _pending;
    private string? _pendingStart;
    private string? _pendingRealm;
    private float _mapSeconds;
    private string _appliedStart = "";

    public AlternateStartBehavior()
    {
        Instance = this;
    }

    public override void RegisterEvents()
    {
        CampaignEvents.OnCharacterCreationInitializedEvent.AddNonSerializedListener(this, OnCharacterCreationInitialized);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData("alternate_start_applied", ref _appliedStart);
    }

    private void OnCharacterCreationInitialized(CharacterCreationManager manager)
    {
        StartSettings.Reload();
        Starts.Build(StartSettings.Current);
        CreationChoice.Reset();

        string gameType = Game.Current?.GameType?.GetType().FullName ?? "";
        if (gameType.StartsWith("StoryMode", StringComparison.Ordinal) && !StartSettings.Current.AllowStoryMode)
        {
            ErrorLog.Debug("Story mode campaign; start pages not added (allow_story_mode=0).");
            return;
        }

        // Handlers sit in a SortedList keyed by priority, so a key another mod already took would throw.
        // Vanilla content uses 800; anything above runs after it, once its pages exist.
        for (int priority = 900; priority < 1000; priority++)
        {
            try
            {
                manager.RegisterCharacterCreationContentHandler(new CreationPages(this), priority);
                return;
            }
            catch (ArgumentException)
            {
            }
        }
        ErrorLog.Write("Could not register the start pages: every handler priority from 900 to 999 is taken.");
    }

    internal void QueueStart(string? startId, string? realmId)
    {
        _pending = true;
        _pendingStart = startId;
        _pendingRealm = realmId;
        _mapSeconds = 0f;
        ErrorLog.Debug($"Queued start '{startId ?? "(none)"}' in realm '{realmId ?? "(homeland)"}'.");
    }

    /// <summary>Called every frame from the SubModule; does nothing unless a start is waiting for the map.</summary>
    internal void Tick(float dt)
    {
        if (!_pending) return;
        if (Campaign.Current == null || MobileParty.MainParty == null || Hero.MainHero == null) return;
        if (GameStateManager.Current?.ActiveState is not MapState) return;

        _mapSeconds += dt;
        if (_mapSeconds < MapSettleSeconds) return;

        _pending = false;
        StartDef? start = Starts.Find(_pendingStart);
        if (start == null) return;

        try
        {
            StartApplier.Apply(start, _pendingRealm);
            _appliedStart = start.Id;
        }
        catch (Exception ex)
        {
            ErrorLog.Write($"Applying start '{start.Id}' failed: {ex}");
            InformationManager.DisplayMessage(new InformationMessage(
                "Alternate Start: the start could not be fully applied. See Modules/AlternateStart/errors.txt."));
        }
    }
}
