// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Graphics;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.Sprites;
using osuTK;
using osu.Framework.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Online.API;
using osu.Game.Overlays.Notifications;
using osu.Framework.Allocation;
using osu.Framework.Platform;
using System.IO;
using osu.Framework.IO.Network;

namespace osu.Game.Overlays
{
    public partial class RulesetManagerOverlay : FullscreenOverlay<RulesetManagerOverlayHeader>
    {
        [Resolved]
        private INotificationOverlay notifications { get; set; } = null!;

        [Resolved]
        private Storage storage { get; set; } = null!;

        private readonly FillFlowContainer rulesetButtonsContainer;
        private readonly LoadingLayer loadingLayer;
        private bool hasFetchedRulesets;

        public RulesetManagerOverlay()
            : base(OverlayColourScheme.Pink) // Choose a color scheme for the overlay.
        {
            Content.Child = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Top = 50 }, // Add some padding below the header.
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(10),
                Children =
                [
                    new OsuSpriteText
                    {
                        Text = @"Ruleset Manager",
                        Font = OsuFont.GetFont(size: 30, weight: FontWeight.Bold),
                        Colour = Colour4.White,
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                    },
                    rulesetButtonsContainer = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Full,
                        Spacing = new Vector2(5), // Add spacing between buttons
                        Padding = new MarginPadding { Top = 10 },
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Child = loadingLayer = new LoadingLayer(dimBackground: true, withBox: true, blockInput: true)
                    }
                ]
            };
        }

        protected override void PopIn()
        {
            base.PopIn();

            if (!hasFetchedRulesets)
            {
                fetchAndDisplayRulesets();
                hasFetchedRulesets = true;
            }
        }

        private async void fetchAndDisplayRulesets()
        {
            loadingLayer.Show();

            const string rulesets_api_url = @"https://rulesets.info/api/rulesets";

            try
            {
                var request = new OsuJsonWebRequest<List<Ruleset>>(rulesets_api_url);
                await request.PerformAsync().ConfigureAwait(true);

                var rulesets = request.ResponseObject;

                if (rulesets == null)
                {
                    Logger.Log(@"No rulesets found.");
                    notifications.Post(new SimpleErrorNotification
                    {
                        Text = @$"No rulesets retrieved from https://rulesets.info!"
                    });
                    return;
                }

                Logger.Log(@$"Retrieved {rulesets.Count} rulesets: {string.Join(", ", rulesets.Select(r => r.name))}");

                // Add a button for each ruleset
                foreach (var ruleset in rulesets)
                {
                    if (!ruleset.can_download) continue;
                    rulesetButtonsContainer.Add(new RulesetButton
                    {
                        Text = ruleset.name,
                        Width = 200,
                        Height = 50,
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Action = () => downloadRuleset(ruleset),
                    });
                }

                Logger.Log(@"Rulesets loaded successfully.");
            }
            catch (Exception ex)
            {
                Logger.Log(@$"Failed to fetch rulesets: {ex.Message}");

                notifications.Post(new SimpleErrorNotification
                {
                    Text = @$"Failed to fetch rulesets: {ex.Message}"
                });
            }
            finally
            {
                loadingLayer.Hide();
            }
        }

        private void downloadRuleset(Ruleset ruleset)
        {
            string rulesetsFolder = storage.GetFullPath(@"rulesets");
            string rulesetFile = Path.GetFileName(ruleset.direct_download_link);
            string downloadPath = Path.Join(rulesetsFolder, rulesetFile);

            Logger.Log(@$"Downloading ruleset {ruleset.name} from {ruleset.direct_download_link}...");

            var notification = new ProgressNotification
            {
                Text = @$"Downloading {ruleset.name}...",
                State = ProgressNotificationState.Queued,
                CompletionText = @$"{ruleset.name} downloaded successfully!"
            };

            notifications.Post(notification);

            var request = new FileWebRequest(downloadPath, ruleset.direct_download_link);
            request.Started += () => notification.State = ProgressNotificationState.Active;
            request.DownloadProgress += (current, total) => notification.Progress = (float)current / total;

            request.Failed += ex =>
            {
                Logger.Error(ex, @$"Failed to download ruleset {ruleset.name}");
                notification.State = ProgressNotificationState.Cancelled;
                notification.Text = @$"Failed to download {ruleset.name}: {ex.Message}";
            };

            request.Finished += () =>
            {
                Logger.Log(@$"Ruleset {ruleset.name} downloaded successfully to {rulesetFile}");
                notification.State = ProgressNotificationState.Completed;
            };

            notification.CancelRequested = () =>
            {
                request.Abort();
                notification.Text = @$"Download of {ruleset.name} cancelled.";
                Logger.Log(@$"Download of {ruleset.name} cancelled");
                return true;
            };

            request.PerformAsync();
        }

        protected override RulesetManagerOverlayHeader CreateHeader() => [];

        private partial class RulesetButton : OsuButton
        {
        }

#pragma warning disable IDE1006
#pragma warning disable CS8618
        private class Ruleset
        {
            public int id { get; set; }
            public string name { get; set; }
            public string slug { get; set; }
            public string description { get; set; }
            public string icon { get; set; }
            public string light_icon { get; set; }
            public OwnerDetail owner_detail { get; set; }
            public bool verified { get; set; }
            public bool archive { get; set; }
            public string direct_download_link { get; set; }
            public bool can_download { get; set; }
            public Status status { get; set; }
        }

        private class OwnerDetail
        {
            public int id { get; set; }
            public User user { get; set; }
            public string image { get; set; }
        }

        private class User
        {
            public string username { get; set; }
            public string email { get; set; }
        }

        private class Status
        {
            public string latest_version { get; set; }
            public string latest_update { get; set; }
            public bool pre_release { get; set; }
            public string changelog { get; set; }
            public int file_size { get; set; }
            public string playable { get; set; }
        }
#pragma warning restore IDE1006
#pragma warning restore CS8618
    }
}