# Changelog

## v1.5.0

**Station troops at the manor.** Once the guard quarters are built, *Manage the household guard →
Station or collect troops* opens the party screen with the manor's quarters on one side and your
party on the other. Leave up to 20 soldiers there (30 on a landed estate, 40 on a grand one) and
take them back whenever you like. Stationed troops gain experience every day on the training field
(faster with a captain or on drill orders), their wounded recover (twice as fast with a physician),
and they stand in the yard when you visit. They draw their normal wages and eat supplies, fight
in the defense of the estate, and take casualties if it is raided. If they go unpaid they desert.
Selling the manor sends them back to your party.

**Real goods.** The estate's output is now made of real produce: grain, cheese and butter from
mixed farming; olives, dates, grapes, wool or flax from cash crops, depending on the region; meat,
hides and wool from livestock; grain from the mill; fruit from the orchard; tools from the
workshop. In the stewardship menu you choose whether the steward sells it for income, as before,
or stores it in the storehouse as goods worth a quarter more, for you to sell, eat or trade. Stored
goods can be looted in a raid. The orchard now adds to the estate's income as well as its supplies.

**Ties to the village.** A thriving estate grows the village a little every day, and the bigger
the estate the more it grows. Each week, the village's notables think a little better of you, and
defending the estate from raiders earns their gratitude. In return, the lord who holds the village
sends a reeve every four weeks for the customary dues, a tenth of the estate's income. Paying
earns the lord's favour; refusing costs it. No dues are owed if your own clan holds the village.
The stewardship menu shows the village's growth, the notables' regard and the next dues.

## v1.4.1

**Translations now actually load.** Each language folder was missing the `language_data.xml`
the game requires before it will read a translation, so the mod showed English regardless
of the game language. Reported, with the fix, by a Russian player — thank you.

## v1.4.0

**The estate is now a place.** Walking the grounds shows what you have actually built:
every completed improvement places its buildings and props in the manor scene — palisade
stakes ring the yard, the mill and orchard stand in the fields, the guard quarters, training
butts, storehouse and workshop appear as you add them, and a landed or grand estate gains a
barn and a proper house. After a raid, wreckage litters the yard until repairs are done.

Your household guard stands in ranks in the yard, with the captain, steward and physician
about their business, whenever you visit. The same dressing applies during a manor defense.

The manor house itself stands at the head of the yard and grows with the estate's rank. Walk
up to its door and press **F** to review the ledger, open the storehouse, or move funds
without leaving the grounds.

The walls and the house are solid: the mod lays blocker navmesh along them, so neither you nor
anyone else can walk through. The estate menus now use the village backdrop instead of the
placeholder texture, and the trees that stood where the yard is have been cleared.

The layout lives in `ModuleData/manor_lord_props.xml` and can be edited without rebuilding.

## v1.3.0

**Localization support.** Manor Lord can now be translated. All 208 player-visible
strings — menus, options, tooltips, messages and dialogs — moved out of the code and
into `ModuleData/Languages/`.

Included translations:

- 简体中文 (Simplified Chinese)
- Deutsch
- Español (LA)
- Français
- Русский
- Türkçe

English remains the fallback, so any string a translation omits still displays correctly.

Adding a language needs no code changes — see `TRANSLATING.md`. Contributions welcome;
the community translations are a first pass and native-speaker corrections are wanted.

No gameplay changes. Existing saves are unaffected.
