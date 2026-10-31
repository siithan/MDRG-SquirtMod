# Squirt Mod

A MelonLoader mod for **My Dystopian Robot Girlfriend** that adds a pulsed squirt effect when Jun climaxes, followed by lingering drips.

## Features

- Context-sensitive: squirt only triggers when vaginal insertion is detected during orgasm
- Works in missionary, cowgirl and doggy, with appropriate spawning and layering
- Lingering drips that stretch down and fade out after the squirt
- Can use your own PNG art, reloaded live when you save over it

## Requirements

| | Version |
|---|---|
| My Dystopian Robot Girlfriend | 0.97.12 (PC) |
| MelonLoader | 0.7.3 |

Other game versions may work, but game updates can break mods. PC only: MelonLoader mods don't run on the browser or Android versions.

## Installation

1. Install [MelonLoader](https://github.com/LavaGang/MelonLoader) on the game and launch it once.
2. Download `SquirtMod.zip` from the [Releases](../../releases) page.
3. Extract it into the game's `Mods` folder
4. Launch the game. The MelonLoader console should list **Squirt Mod 0.1.1**.

## Custom art

Replace the PNGs in `Mods\SquirtMod\` with your own:

- `drop_*.png` (any number): droplets, drawn **falling straight down**. One is picked at random per drop.
- `drop_stretch.png`: the lingering drip. Only its middle stretches; see `DripEffect.cs` for the pixel measurements to update if you change its proportions.

Save as **transparent PNG**. Changes are picked up the next time the effect plays.

## Tuning

Settings are public static fields, editable live with [MDRGUnityExplorer](https://github.com/IkariDevGIT/MDRGUnityExplorer) (search the class `SquirtEffect`, `DripEffect` or `InsertionDetector`).

## Known limitations

- Shower pose is not integrated yet.
- Tested with game version 0.97.12 only.

## Roadmap

Planned for future versions:

- [ ] Shower pose support
- [ ] Contact with the MC's hand and penis: splashes, drops that stick and drip down
- [ ] Wet decals that linger after the squirt
- [ ] Pulses synced with Jun's quivers
- [ ] Exaggerating Jun's quivers
- [ ] ModSettingsMenu support

## Credits

- Built with [MelonLoader](https://github.com/LavaGang/MelonLoader)
- Thanks to IkariDev for MoanMod (a great reference) and the MDRG compatible UnityExplorer fork

## License

MIT. See [LICENSE](LICENSE).
