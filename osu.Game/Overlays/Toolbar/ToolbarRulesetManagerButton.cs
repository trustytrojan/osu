// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;

namespace osu.Game.Overlays.Toolbar
{
    public partial class ToolbarRulesetManagerButton : ToolbarOverlayToggleButton
    {
        protected override Anchor TooltipAnchor => Anchor.TopRight;

        protected override void LoadComplete()
        {
            base.LoadComplete();
            SetIcon(FontAwesome.Solid.Smile); // Example icon
        }

        [BackgroundDependencyLoader(true)]
        private void load(RulesetManagerOverlay overlay)
        {
            StateContainer = overlay; // Ensure this references the correct overlay
        }
    }
}