// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;

namespace osu.Game.Overlays
{
    public partial class RulesetManagerOverlayHeader : OverlayHeader
    {
        protected override OverlayTitle CreateTitle() => new RulesetManagerOverlayTitle();

        protected override Drawable CreateBackground() => new Box
        {
            RelativeSizeAxes = Axes.Both,
            Colour = Colour4.DarkSlateGray // Background color for the header.
        };

        private partial class RulesetManagerOverlayTitle : OverlayTitle
        {
            public RulesetManagerOverlayTitle()
            {
                Title = @"ruleset manager";
                Description = @"download and manage custom rulesets";
                Icon = FontAwesome.Solid.PlusCircle; // Example icon.
            }
        }
    }
}