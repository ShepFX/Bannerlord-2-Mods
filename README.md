# Bannerlord 2 Mods

Collection of Mount & Blade II: Bannerlord mods.

Each folder is a separate Bannerlord module:

- `AlternateStart` - choose how your story begins inside character creation: fourteen starts, from destitute wanderer, escaped captive, merchant, mercenary captain and outlaw chief to vassal lord, rebel king or ruler of an existing realm, each with its own outfit, troops, land and politics in the realm you pick. Every value tunable in `settings.txt`; separate build for v1.3.15. No Harmony required.
- `CompanionDefense` - lets you send companion-led clan parties to defend owned villages, castles, and towns under attack.
- `CompanionHotswap` - companion swapping and selection UI support.
- `DuelCompanions` - adds roaming duel rumors, elite duelists, gauntlets, rare weapon rewards, and recruitable duel companions.
- `FieldFortifications` - buy barricades, a ballista, a catapult, arrow barrels and a raised archer platform before a field battle and place them yourself during deployment; the AI paths around the barricades, horses impale themselves on them, your archers crew the engines and climb the platform. Requires Bannerlord Harmony.
- `LoadoutPresets` - player loadout preset support.
- `ManorLord` - buy a manor near a village and build it up: palisade, storehouse, guard quarters, mill, orchard, workshop and staff; walk a dressed estate scene that shows every upgrade you own, with guards on parade and an in-scene estate menu; defend it against raiders. Fully localized (EN, 简体中文, DE, FR, RU, ES, TR).
- `MovingDismount` - lets the player attempt to dismount from a moving horse, with speed-scaled stumble and hard-fall damage.
- `ReserveSliderLimit` - raises the settlement reserve slider from 10,000 to 100,000. Requires Bannerlord Harmony.
- `RoyalHeirStart` - lets a new Sandbox character claim a kingdom as a lost royal heir with faction-specific starting bonuses.
- `StrategicCampaignAI` - gives kingdom AI direction on the campaign map: frontline-aware objectives, war goals and exhaustion, army roles, siege judgement, raid response, garrison support, interception, and save-persistent strategic memory. Built on Bannerlord's own campaign AI models; no Harmony required.
- `TroopHealthBars` - adds compact battle HUD bars showing surviving infantry, archers, and mounted troops as percentages, with optional MCM settings for position, colours, opacity, percentages, and category toggles.

## Layout

Each module folder keeps the Bannerlord runtime layout:

- `SubModule.xml`
- `bin/Win64_Shipping_Client/*.dll`
- `GUI/` and `ModuleData/` where used
- `src/` source project

Intermediate build folders such as `src/obj` are intentionally excluded.

## Build Notes

Projects target `.NET Framework 4.7.2` and reference Bannerlord assemblies from a local Steam install. If your Bannerlord path differs, update the `BannerlordPath` property in the relevant `.csproj`.
