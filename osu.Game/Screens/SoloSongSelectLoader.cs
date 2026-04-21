// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using osu.Game.Screens.Select;

namespace osu.Game.Screens
{
    /// <summary>
    /// Startup loader variant that bypasses intro/menu and starts directly at song select.
    /// </summary>
    public partial class SoloSongSelectLoader : Loader
    {
        protected override OsuScreen CreateLoadableScreen() => new SoloSongSelect();
    }
}
