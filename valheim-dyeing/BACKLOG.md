# Backlog

Wanted and thought through, not built.

## More armor than bronze

The mod is deliberately limited to the bronze cuirass, leggings and helmet for its first version.
Nothing in the code is specific to bronze: `Dyeing.BaseItems` is a list of prefab names, and the
tint helper asks each material which of `_ArmorHue`, `_Hue` and `_Color` it understands rather than
assuming. Widening it is a matter of deciding how far to go and what it costs in prefabs.

The cost is worth stating before anyone widens it casually. Every dyeable piece is cloned once per
colour, so three pieces in seven colours is twenty one prefabs. Every chest, leg, helmet and cape
in the game is roughly a hundred pieces, which at seven colours is seven hundred prefabs minted at
load, registered in `ObjectDB` and in `ZNetScene`, and hashed into both. That is a large multiplier
on a list the game already fills with about three thousand entries, and it wants measuring rather
than guessing.

Capes are the obvious next step and the cheapest: there are about a dozen, they are cloth, and the
shoulder slot is the one slot vanilla already carries a variant through, which may open a cheaper
route than cloning.

## A tub that does not look like a cauldron

The tub is the cooking cauldron's model with a new name and a new cost, and it carries the
cauldron's build menu icon too. Standing next to both, there is nothing to tell them apart. Tinting
the cauldron's water material would go a long way on its own, and the piece already has a water
surface to tint. A rack of dyed cloth hung over the side would go further.

## Washing the colour back out

There is no way to get a plain piece back short of forging a new one. A wash out recipe was
designed and dropped: producing the plain item needs a recipe whose result is the plain prefab, and
any such recipe also offers itself in the forge's upgrade tab, where its requirements would let a
dyed piece be spent upgrading a plain one. Vanilla's `m_requireOnlyOneIngredient` would express
"any dyed piece of this kind" in one recipe rather than one per colour, but `Recipe.GetAmount` calls
`Player.GetFirstRequiredItem` and dereferences the result without a null check, so selecting the
recipe while holding nothing that satisfies it would throw.

Doing it properly means either a recipe the upgrade tab refuses to show, which is the mirror of
`m_noCraftOnlyUpgrade` and does not exist, or taking the wash out off the recipe system entirely and
hanging it on the tub as an interaction.

## The inventory icon is not dyed

A dyed piece is tinted wherever it is drawn in the world, on the player, on an armor stand and on
the ground, because all of those go through renderers whose materials the mod has replaced. The
inventory icon does not: it is a `Sprite` baked at build time and stored in `m_shared.m_icons`, so
every colour shows the plain bronze icon and the colour is only readable from the item's name.
Rendering an icon per colour at load is possible and is what a larger mod would do.

## Black cannot darken the metal past half

Every colour is applied twice over: a hue, saturation and value shift for the cloth and leather, and
a multiply colour for the metal. The multiply is softened before use, lifting its brightness to sit
between 0.5 and 1, which is what stops a coloured dye turning bronze into mud and is part of why red,
blue, yellow and orange came out right.

Black is the colour that wants the opposite. A multiply floored at 0.5 can only ever halve the metal,
so black armor keeps bright metalwork no matter what the config says, and the first attempt to force
it darker instead drove the value shift so low that the cloth collapsed to a flat void while the
metal stayed untouched. The user described it exactly: "the metal parts looks normal but the black is
still pure black".

The fix is to let a dye carry its own metal multiply rather than deriving one from the cloth colour,
as an optional fifth field on the config string so nothing already tuned has to change. Black would
then set a genuinely dark metal tint and a gentle value shift, which is the combination it needs and
cannot currently express.

## The debug test file replays on every restart

The file the test tool reads is treated as new whenever its timestamp differs from the last one seen,
and that memory is held in a field reset on `ZNet.Awake`. A server restart therefore re-reads a file
it has already run and drops the whole kit again, which during this session meant seven hundred
unwanted items at the player's feet after an unrelated restart. Writing the marker somewhere that
survives a restart, or deleting the file once it has been run, would settle it. It only affects
builds made with `DebugTools`, so nothing ships with it, but it will happen again in the next test
session.
