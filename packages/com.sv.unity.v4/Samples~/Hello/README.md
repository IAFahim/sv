# Hello sv (Unity)

A hero and a knight are hit by a goblin and a dragon. The knight has a shield and a fire ward; the hero has neither. Press Play and the Console prints:

```
frame 1: hero health 25, knight health 85 shield 0, hero low health True, digest D0391ABCE50C62DB
frame 2: hero health 75, knight health 85 shield 0, hero low health False, digest 9078B3C66F1DD3FE
```

These are the same numbers, and the same two digests, as the .NET program `samples/Sv.Hello` prints. The sample's test (`Tests/HelloSvTests.cs`) checks that.

## Run it

1. Package Manager > **sv Unity Adapter v4** > **Samples** > **Hello sv** > **Import**.
2. Create an empty GameObject in any scene, add the **Hello Sv** component, press **Play**.

## Change it without code (for designers)

Everything a designer touches is in `Pack/`:

| File | What you change there |
|---|---|
| `values.sv.json` | the values every character has (Health, MaxHealth, Shield), the damage types (Physical, Fire, Healing), who attacks (Goblin, Dragon) |
| `templates.sv.json` | starting values per character: try the knight's `"Shield":15` |
| `cards.sv.json` | things a character can hold: the fire ward |
| `rules.sv.json` | what happens: the shield soaks damage, the fire ward blocks fire, health stays between 0 and MaxHealth, "low health" starts under 30 % and ends above 40 % |

Try it:

1. Open **Window > sv > Pack Authoring**. Set **Pack folder** to this sample's `Pack` folder and press **Load**.
2. In `templates.sv.json`, change the knight's `"Shield":15` to `"Shield":30`. **Validate**, then **Save**.
3. Set the namespace to `Hello.Sv` and the output folder to this sample's `Generated` folder, press **Regenerate**.
4. Press **Play**. The knight now ends frame 1 with health 100 and shield 0: the shield soaked all 30. The digest changes too, which is how you know two machines disagree.

You never edit C#. The generated code in `Generated/` comes from the four files.

## Words

| Designer word | sv word |
|---|---|
| stat (MaxHealth) | `stat`: changed by cards and modifiers |
| current value (Health, Shield) | `intrinsic`: changed by rules each frame |
| damage type, healing | `channel` |
| who hit you | `kind` |
| a thing a character holds | `card` |
| a character type | `template` |
| "low health" | `event` (with enter and stay limits) |

The rules language is described in the repository README, chapter 3.
