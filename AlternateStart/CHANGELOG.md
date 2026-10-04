# Changelog

## v1.0.0

First release, for Bannerlord v1.4.7 and later, with a separate build for v1.3.15.

Adds two pages to the end of sandbox character creation, after Starting Age:

- **Your Start** — choose how your life stands on day one. The preview character changes into each start's
  outfit, and the gear you see is the gear you start with.
- **Where Your Story Begins** — choose a realm. You start in its lands; starts that serve, hold land in or
  seize a crown use it as that crown.

### Starts

| Start | What you get |
|---|---|
| Adventurer | The vanilla start, set out from the realm you choose |
| Destitute Wanderer | 30 denars, rags and a battered sword, no horse |
| Escaped Captive | No money, no weapons, badly wounded, 4 fellow escapees |
| Militia Captain | 15 village recruits, the headmen's favour, 1,500 denars |
| Discharged Veteran | 8 veteran soldiers, a sellsword's kit, 4,000 denars |
| Merchant | 2 workshops, a caravan, mules and trade goods, 10,000 denars |
| Mercenary Captain | 25 sellswords, a companion, a mercenary contract with the realm |
| Outlaw Chief | 20 brigands near a hideout, a price on your head |
| Landless Noble | Noble house, 12 household troops, lordly gear, 10,000 denars |
| Independent Lord | A castle sworn to no one, 30 troops, 2 companions |
| Sworn Vassal | A castle in the realm, 35 troops, 150 influence |
| Great Lord | A town and a castle in the realm, 50 troops, 300 influence |
| Rebel King | A seized town and castle, your own kingdom, at war with the realm |
| Ruler of a Realm | The realm's crown and royal seat; the old ruler dies |

Every number, and whether each start appears at all, is set in `settings.txt`.

### Notes

- Sandbox only by default. Story mode can be enabled in `settings.txt`, but its main quest expects the vanilla start.
- Do not run it alongside other mods that hand out a crown or a start on a new campaign.
- Problems are written to `errors.txt` in the mod folder. Nothing is written when all is well.
