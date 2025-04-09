// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Testing;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;

using static osu.Game.Overlays.RulesetManagerOverlay;

namespace osu.Game.Tests.Visual.Online
{
    public partial class TestSceneRulesetManagerOverlay : OsuTestScene
    {
        private partial class TestRulesetManagerOverlay : RulesetManagerOverlay
        {
            protected override bool FetchedRulesets => true;
            public IEnumerable<RulesetButton> RulesetButtons => RulesetButtonsContainer.ChildrenOfType<RulesetButton>();
        }

        [Cached(typeof(INotificationOverlay))]
        private NotificationOverlay notificationOverlay = [];

        private TestRulesetManagerOverlay overlay = null!;

        [SetUp]
        public void SetUp()
        {
            Clear(false);
            Add(overlay = []);
            Add(notificationOverlay);
        }

        [Test]
        public void CorrectButtonCount()
        {
            AddStep("show overlay", () => overlay.Show());
            AddStep("display rulesets", () => overlay.DisplayRulesets(dummyRulesets));
            AddAssert("corrent number of ruleset buttons shown", () =>
                overlay.RulesetButtons.Count() == dummyRulesets.Where(r => r.CanDownload).Count());
        }

        [Test]
        public void ErrorNotificationShown()
        {
            ProgressNotification notification = null!;
            AddStep("show overlay", () => overlay.Show());
            AddStep("display rulesets", () => overlay.DisplayRulesets(dummyRulesets));
            AddStep("click first ruleset button", () => overlay.RulesetButtons.First().TriggerClick());
            AddUntilStep("wait for progress notification", () =>
                notificationOverlay.AllNotifications.OfType<ProgressNotification>().Any());
            AddStep("get progress notification", () =>
                notification = notificationOverlay.AllNotifications.OfType<ProgressNotification>().First());
            AddUntilStep("wait for failure", () => notification.State == ProgressNotificationState.Cancelled);
            AddAssert("check notification text", () =>
                notification.Text.ToString().StartsWith("Failed to download", StringComparison.Ordinal));
        }

        private readonly Ruleset[] dummyRulesets =
        [
            new Ruleset
            {
                Id = 1,
                Name = "ExampleRuleset1",
                Slug = "example1",
                Description = "This is a dummy description for ExampleRuleset1.",
                Icon = "/media/rulesets_icon/example1.png",
                LightIcon = "/media/rulesets_icon_light/example1_light.png",
                OwnerDetail = new UserDetail
                {
                    Id = 101,
                    User = new User
                    {
                        Username = "DummyUser1",
                        Email = "dummy1@example.com"
                    },
                    Image = "/media/profile_pics/dummy1.png"
                },
                Verified = true,
                Archive = false,
                DirectDownloadLink = "https://example.com/ruleset1.dll",
                CanDownload = true,
                Status = new Status
                {
                    LatestVersion = "1.0.0",
                    LatestUpdate = DateTime.Parse("2025-01-01T00:00:00Z"),
                    PreRelease = false,
                    Changelog = "Initial release.",
                    FileSize = 123456,
                    Playable = "yes"
                }
            },
            new Ruleset
            {
                Id = 2,
                Name = "ExampleRuleset2",
                Slug = "example2",
                Description = "This is a dummy description for ExampleRuleset2.",
                Icon = "/media/rulesets_icon/example2.png",
                LightIcon = "/media/rulesets_icon_light/example2_light.png",
                OwnerDetail = new UserDetail
                {
                    Id = 102,
                    User = new User
                    {
                        Username = "DummyUser2",
                        Email = "dummy2@example.com"
                    },
                    Image = "/media/profile_pics/dummy2.png"
                },
                Verified = false,
                Archive = false,
                DirectDownloadLink = "https://example.com/ruleset2.dll",
                CanDownload = true,
                Status = new Status
                {
                    LatestVersion = "2.0.0",
                    LatestUpdate = DateTime.Parse("2025-02-01T00:00:00Z"),
                    PreRelease = true,
                    Changelog = "Beta release.",
                    FileSize = 234567,
                    Playable = "no"
                }
            },
            new Ruleset
            {
                Id = 3,
                Name = "ExampleRuleset3",
                Slug = "example3",
                Description = "This is a dummy description for ExampleRuleset3.",
                Icon = "/media/rulesets_icon/example3.png",
                LightIcon = "/media/rulesets_icon_light/example3_light.png",
                OwnerDetail = new UserDetail
                {
                    Id = 103,
                    User = new User
                    {
                        Username = "DummyUser3",
                        Email = "dummy3@example.com"
                    },
                    Image = "/media/profile_pics/dummy3.png"
                },
                Verified = true,
                Archive = true,
                DirectDownloadLink = "https://example.com/ruleset3.dll",
                CanDownload = false,
                Status = new Status
                {
                    LatestVersion = "3.0.0",
                    LatestUpdate = DateTime.Parse("2025-03-01T00:00:00Z"),
                    PreRelease = false,
                    Changelog = "Final release.",
                    FileSize = 345678,
                    Playable = "yes"
                }
            }
        ];
    }
}