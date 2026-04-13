// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Logging;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Screens;

namespace osu.Game.Scoring
{
    public static class ScoreVideoRenderer
    {
        public static void RequestRender(ScoreInfo score, OsuGame? game, INotificationOverlay? notifications = null)
        {
            if (game == null)
            {
                notifications?.Post(new SimpleNotification
                {
                    Text = "Cannot render video from this screen."
                });
                return;
            }

            game.PresentScore(score, ScorePresentType.Render);
        }
    }
}