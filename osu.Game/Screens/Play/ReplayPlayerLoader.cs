// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Screens;
using osu.Game.Scoring;

namespace osu.Game.Screens.Play
{
    public partial class ReplayPlayerLoader : PlayerLoader
    {
        public readonly ScoreInfo Score;
        public override bool DisallowExternalBeatmapRulesetChanges => false;

        private OsuGameBase game = null!;

        public ReplayPlayerLoader(Score score)
            : base(() => new ReplayPlayer(score))
        {
            if (score.Replay == null)
                throw new ArgumentException($"{nameof(score)} must have a non-null {nameof(score.Replay)}.", nameof(score));

            Score = score.ScoreInfo;
        }

        [BackgroundDependencyLoader]
        private void load(OsuGameBase game)
        {
            this.game = game;
        }

        public override void OnEntering(ScreenTransitionEvent e)
        {
            // these will be reverted thanks to PlayerLoader's lease.
            Mods.Value = Score.Mods;
            Ruleset.Value = Score.Ruleset;

            base.OnEntering(e);
        }

        public override void OnSuspending(ScreenTransitionEvent e)
        {
            if (e.Next is not ReplayPlayer)
                game.StopRecording();
            base.OnSuspending(e);
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            if (e.Destination is not ReplayPlayer)
                game.StopRecording();
            return base.OnExiting(e);
        }
    }
}
