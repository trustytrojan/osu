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
        private static readonly HashSet<Guid> pending_score_renders = new HashSet<Guid>();

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

            lock (pending_score_renders)
            {
                pending_score_renders.Add(score.ID);
            }

            Logger.Log($"Video render requested for score {score.ID}.", LoggingTarget.Runtime, LogLevel.Important);

            game.PresentScore(score, ScorePresentType.Gameplay);

            notifications?.Post(new SimpleNotification
            {
                Text = "Started replay frame rendering task."
            });
        }

        public static bool ConsumePendingRequest(ScoreInfo score)
        {
            lock (pending_score_renders)
            {
                return pending_score_renders.Remove(score.ID);
            }
        }
    }
}