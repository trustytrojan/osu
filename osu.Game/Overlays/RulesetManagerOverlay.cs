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
using Newtonsoft.Json;
using System.Threading.Tasks;

namespace osu.Game.Overlays
{
    public partial class RulesetManagerOverlay : FullscreenOverlay<RulesetManagerOverlayHeader>
    {
        [Resolved]
        private INotificationOverlay notifications { get; set; } = null!;

        [Resolved]
        private Storage storage { get; set; } = null!;

        protected readonly FillFlowContainer RulesetButtonsContainer;
        private readonly LoadingLayer loadingLayer;
        protected virtual bool FetchedRulesets { get; set; }

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
                    RulesetButtonsContainer = new FillFlowContainer
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

        protected override async void PopIn()
        {
            base.PopIn();

            if (!FetchedRulesets)
            {
                loadingLayer.Show();
                DisplayRulesets(await fetchRulesets().ConfigureAwait(true));
                loadingLayer.Hide();
                FetchedRulesets = true;
            }
        }

        private async Task<IEnumerable<Ruleset>> fetchRulesets()
        {
            const string rulesets_api_url = @"https://rulesets.info/api/rulesets";

            var request = new OsuJsonWebRequest<Ruleset[]>(rulesets_api_url);
            request.Failed += ex =>
            {
                Logger.Error(ex, @$"Failed to fetch rulesets");
                notifications.Post(new SimpleErrorNotification()
                {
                    Text = @$"Failed to fetch rulesets: {ex.Message}"
                });
            };

            await request.PerformAsync().ConfigureAwait(true);
            var rulesets = request.ResponseObject;

            if (rulesets.Length == 0)
            {
                Logger.Log(@$"No rulesets retrieved from {rulesets_api_url}");
                notifications.Post(new SimpleErrorNotification()
                {
                    Text = @$"No rulesets retrieved from {rulesets_api_url}"
                });
            }

            Logger.Log(@$"Retrieved {rulesets.Length} rulesets: {string.Join(", ", rulesets.Select(r => r.Name))}");
            return rulesets;
        }

        public void DisplayRulesets(IEnumerable<Ruleset> rulesets)
        {
            if (!rulesets.Any())
                throw new ArgumentException(@"Rulesets is empty");

            foreach (var ruleset in rulesets)
            {
                if (!ruleset.CanDownload) continue;
                RulesetButtonsContainer.Add(new RulesetButton
                {
                    Text = ruleset.Name,
                    Width = 200,
                    Height = 50,
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Action = () => downloadRuleset(ruleset),
                });
            }
        }

        private void downloadRuleset(Ruleset ruleset)
        {
            string rulesetsFolder = storage.GetFullPath(@"rulesets");
            string rulesetFile = Path.GetFileName(ruleset.DirectDownloadLink);
            string downloadPath = Path.Join(rulesetsFolder, rulesetFile);

            Logger.Log(@$"Downloading ruleset {ruleset.Name} from {ruleset.DirectDownloadLink}...");

            var notification = new ProgressNotification
            {
                Text = @$"Downloading {ruleset.Name}...",
                State = ProgressNotificationState.Queued,
                CompletionText = @$"{ruleset.Name} downloaded successfully!"
            };

            notifications.Post(notification);

            var request = new FileWebRequest(downloadPath, ruleset.DirectDownloadLink);
            request.Started += () => notification.State = ProgressNotificationState.Active;
            request.DownloadProgress += (current, total) => notification.Progress = (float)current / total;

            request.Failed += ex =>
            {
                Logger.Error(ex, @$"Failed to download ruleset {ruleset.Name}");
                notification.State = ProgressNotificationState.Cancelled;
                notification.Text = @$"Failed to download {ruleset.Name}: {ex.Message}";
            };

            request.Finished += () =>
            {
                Logger.Log(@$"Ruleset {ruleset.Name} downloaded successfully to {rulesetFile}");
                notification.State = ProgressNotificationState.Completed;
            };

            notification.CancelRequested = () =>
            {
                request.Abort();
                notification.Text = @$"Download of {ruleset.Name} cancelled.";
                Logger.Log(@$"Download of {ruleset.Name} cancelled");
                return true;
            };

            request.PerformAsync();
        }

        protected override RulesetManagerOverlayHeader CreateHeader() => [];

        public partial class RulesetButton : OsuButton
        {
        }

        // https://docs.rulesets.info/#/api?id=listing
        public class Ruleset
        {
            [JsonProperty("id")]
            public int Id;

            [JsonProperty("name")]
            public required string Name;

            [JsonProperty("slug")]
            public required string Slug;

            [JsonProperty("description")]
            public required string Description;

            [JsonProperty("icon")]
            public required string Icon;

            [JsonProperty("light_icon")]
            public required string LightIcon;

            [JsonProperty("owner_detail")]
            public required UserDetail OwnerDetail;

            [JsonProperty("verified")]
            public bool Verified;

            [JsonProperty("archive")]
            public bool Archive;

            [JsonProperty("direct_download_link")]
            public required string DirectDownloadLink;

            [JsonProperty("can_download")]
            public bool CanDownload;

            [JsonProperty("status")]
            public required Status Status;
        }

        // https://docs.rulesets.info/#/api?id=user_detail
        public class UserDetail
        {
            [JsonProperty("id")]
            public int Id;

            [JsonProperty("user")]
            public required User User;

            [JsonProperty("image")]
            public required string Image;
        }

        // https://docs.rulesets.info/#/api?id=user_detail
        public class User
        {
            [JsonProperty("username")]
            public required string Username;

            [JsonProperty("email")]
            public required string Email;
        }

        // https://docs.rulesets.info/#/api?id=status
        public class Status
        {
            [JsonProperty("latest_version")]
            public required string LatestVersion;

            [JsonProperty("latest_update")]
            public DateTime? LatestUpdate;

            [JsonProperty("pre_release")]
            public bool PreRelease;

            [JsonProperty("changelog")]
            public required string Changelog;

            [JsonProperty("file_size")]
            public int FileSize;

            [JsonProperty("playable")]
            public required string Playable;
        }
    }
}