# MapleCombatText (Erenshor / Lunaris plugin)

Replaces Erenshor's damage popups (`DmgPop.LoadInfo`) with MapleStory-style combat text:

- Orange gradient digits for damage you deal, a bigger first digit, staggered heights, thick dark outline and drop shadow
- Critical hits: pink-red digits with a spiky orange "critical" burst behind the first digit
- Damage you take: violet digits
- `MISS` for 0-damage hits
- Optional black outline (**BlackOutline** setting) instead of the default tinted brown/red/purple outline
- Pop-in, short hold, then float up and fade
- Rapid hits on the same target stack upward like multi-line skills

## Install

1. Install Lunaris for Erenshor (the plugin loader).
2. Download `MapleCombatText-v1.0.0.zip` from the Releases page.
3. Unzip it into `<Erenshor>\plugins\` so you have `plugins\MapleCombatText.dll` and `plugins\MapleCombatText\skins\...`.
4. Start the game. Settings are in the Lunaris config UI under MapleCombatText.

## Building from source

Build: `dotnet build -c Release`. This copies the DLL into `<Erenshor>\plugins` automatically.
Settings show up in the Lunaris config UI (scale, others scale/opacity, stacking, timings, skin, on/off). It uses the game's own font, because Unity cannot load Windows system fonts at runtime here.

## Custom digit skins (for an exact look)

Put PNGs in `<Erenshor>\plugins\MapleCombatText\skins\<SkinName>\`, then set **Skin** = `<SkinName>` and restart.
Any subset works. Missing pieces fall back to the built-in look.

| File | Used for |
|---|---|
| `normal_0.png` to `normal_9.png` | damage you deal |
| `crit_0.png` to `crit_9.png` | critical hits |
| `taken_0.png` to `taken_9.png` | damage you take |
| `normal_miss.png`, `taken_miss.png` | MISS |
| `crit_effect.png` | critical burst behind the first digit |

Each digit set needs all ten files to be used.

## Built-in "Maple" digits

Toggle with the **MapleDigits** setting (on by default, switches instantly). Off = the font-based look, or your own **Skin** if one is set.

`tools/make_skin.py` generates MapleStory-style bubbly digits (Comic Sans Bold, fattened and rounded, glossy gradient, dark outline):

    python tools/make_skin.py "<Erenshor>\plugins\MapleCombatText\skins\Maple"
    python tools/make_skin.py "<Erenshor>\plugins\MapleCombatText\skins\MapleBlack" --black

A `<Skin>Black` folder next to the skin is used automatically while **BlackOutline** is on.
