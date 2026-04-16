// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using ManagedBass;
using ManagedBass.Mix;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Mixing;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Configuration;
using osu.Framework.Development;
using osu.Framework.Extensions;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Textures;
using osu.Framework.Graphics.Video;
using osu.Framework.Graphics.Visualisation;
using osu.Framework.Input;
using osu.Framework.Input.Handlers;
using osu.Framework.Input.Handlers.Joystick;
using osu.Framework.Input.Handlers.Midi;
using osu.Framework.Input.Handlers.Mouse;
using osu.Framework.Input.Handlers.Tablet;
using osu.Framework.Input.Handlers.Touch;
using osu.Framework.IO.Stores;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Framework.Timing;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Beatmaps.Formats;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Extensions;
using osu.Game.Graphics;
using osu.Game.Graphics.Cursor;
using osu.Game.Graphics.UserInterface;
using osu.Game.Input;
using osu.Game.Input.Bindings;
using osu.Game.IO;
using osu.Game.Localisation;
using osu.Game.Online;
using osu.Game.Online.API;
using osu.Game.Online.Chat;
using osu.Game.Online.Leaderboards;
using osu.Game.Online.Metadata;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Spectator;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings;
using osu.Game.Overlays.Settings.Sections;
using osu.Game.Overlays.Settings.Sections.Input;
using osu.Game.Resources;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;
using osu.Game.Screens;
using osu.Game.Screens.Play;
using osu.Game.Skinning;
using osu.Game.Utils;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using RuntimeInfo = osu.Framework.RuntimeInfo;

namespace osu.Game
{
    /// <summary>
    /// The most basic <see cref="Game"/> that can be used to host osu! components and systems.
    /// Unlike <see cref="OsuGame"/>, this class will not load any kind of UI, allowing it to be used
    /// for provide dependencies to test cases without interfering with them.
    /// </summary>
    [Cached(typeof(OsuGameBase))]
    public partial class OsuGameBase : Framework.Game, ICanAcceptFiles, IBeatSyncProvider
    {
#if DEBUG
        public const string GAME_NAME = "osu! (development)";
#else
        public const string GAME_NAME = "osu!";
#endif

        public const string OSU_PROTOCOL = "osu://";

        /// <summary>
        /// The filename of the main client database.
        /// </summary>
        public const string CLIENT_DATABASE_FILENAME = @"client.realm";

        public const int SAMPLE_CONCURRENCY = 6;

        public const double SFX_STEREO_STRENGTH = 0.6;

        /// <summary>
        /// Length of debounce (in milliseconds) for commonly occuring sample playbacks that could stack.
        /// </summary>
        public const int SAMPLE_DEBOUNCE_TIME = 20;

        /// <summary>
        /// The maximum volume at which audio tracks should play back at. This can be set lower than 1 to create some head-room for sound effects.
        /// </summary>
        private const double global_track_volume_adjust = 0.8;

        public virtual bool UseDevelopmentServer => DebugUtils.IsDebugBuild;

        public virtual EndpointConfiguration CreateEndpoints() =>
            UseDevelopmentServer ? new DevelopmentEndpointConfiguration() : new ProductionEndpointConfiguration();

        protected override OnlineStore CreateOnlineStore() => new TrustedDomainOnlineStore();

        public virtual Version AssemblyVersion => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version();

        /// <summary>
        /// MD5 representation of the game executable.
        /// </summary>
        public string VersionHash { get; private set; }

        public bool IsDeployedBuild => AssemblyVersion.Major > 0;

        public virtual string Version
        {
            get
            {
                if (!IsDeployedBuild)
                    return @"local " + (DebugUtils.IsDebugBuild ? @"debug" : @"release");

                string informationalVersion = Assembly.GetEntryAssembly()?
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion;

                // Example: [assembly: AssemblyInformationalVersion("2025.613.0-tachyon+d934e574b2539e8787956c3c9ecce9dadebb10ee")]
                if (!string.IsNullOrEmpty(informationalVersion))
                    return informationalVersion.Split('+').First();

                Version version = AssemblyVersion;
                return $@"{version.Major}.{version.Minor}.{version.Build}-lazer";
            }
        }

        /// <summary>
        /// The <see cref="Edges"/> that the game should be drawn over at a top level.
        /// Defaults to <see cref="Edges.None"/>.
        /// </summary>
        protected virtual Edges SafeAreaOverrideEdges => Edges.None;

        protected OsuConfigManager LocalConfig { get; private set; }

        protected SessionStatics SessionStatics { get; private set; }

        protected OsuColour Colours { get; private set; }

        protected BeatmapManager BeatmapManager { get; private set; }

        protected BeatmapModelDownloader BeatmapDownloader { get; private set; }

        protected ScoreManager ScoreManager { get; private set; }

        protected ScoreModelDownloader ScoreDownloader { get; private set; }

        protected SkinManager SkinManager { get; private set; }

        protected RealmRulesetStore RulesetStore { get; private set; }

        protected RealmKeyBindingStore KeyBindingStore { get; private set; }

        protected GlobalCursorDisplay GlobalCursorDisplay { get; private set; }

        protected MusicController MusicController { get; private set; }

        protected IAPIProvider API { get; set; }

        protected Storage Storage { get; set; }

        /// <summary>
        /// The language in which the game is currently displayed in.
        /// </summary>
        public Bindable<Language> CurrentLanguage { get; } = new Bindable<Language>();

        protected Bindable<WorkingBeatmap> Beatmap { get; private set; } // cached via load() method

        /// <summary>
        /// The current ruleset selection for the local user.
        /// </summary>
        [Cached]
        [Cached(typeof(IBindable<RulesetInfo>))]
        protected internal readonly Bindable<RulesetInfo> Ruleset = new Bindable<RulesetInfo>();

        /// <summary>
        /// The current mod selection for the local user.
        /// </summary>
        /// <remarks>
        /// If a mod select overlay is present, mod instances set to this value are not guaranteed to remain as the provided instance and will be overwritten by a copy.
        /// In such a case, changes to settings of a mod will *not* propagate after a mod is added to this collection.
        /// As such, all settings should be finalised before adding a mod to this collection.
        /// </remarks>
        [Cached]
        [Cached(typeof(IBindable<IReadOnlyList<Mod>>))]
        protected readonly Bindable<IReadOnlyList<Mod>> SelectedMods = new Bindable<IReadOnlyList<Mod>>(Array.Empty<Mod>());

        /// <summary>
        /// Mods available for the current <see cref="Ruleset"/>.
        /// </summary>
        public readonly Bindable<Dictionary<ModType, IReadOnlyList<Mod>>> AvailableMods = new Bindable<Dictionary<ModType, IReadOnlyList<Mod>>>(new Dictionary<ModType, IReadOnlyList<Mod>>());

        private BeatmapDifficultyCache difficultyCache;
        private IBeatmapUpdater beatmapUpdater;

        private UserLookupCache userCache;
        private BeatmapLookupCache beatmapCache;
        protected LeaderboardManager LeaderboardManager { get; private set; }

        private RulesetConfigCache rulesetConfigCache;

        private SessionAverageHitErrorTracker hitErrorTracker;

        protected SpectatorClient SpectatorClient { get; private set; }

        protected MultiplayerClient MultiplayerClient { get; private set; }

        private MetadataClient metadataClient;

        private RealmAccess realm;

        protected SafeAreaContainer SafeAreaContainer { get; private set; }

        /// <summary>
        /// For now, this is used as a source specifically for beat synced components.
        /// Going forward, it could potentially be used as the single source-of-truth for beatmap timing.
        /// </summary>
        private readonly FramedBeatmapClock beatmapClock = new FramedBeatmapClock(applyOffsets: true, requireDecoupling: false);

        protected override Container<Drawable> Content => content;

        private Container content;

        private DependencyContainer dependencies;

        private readonly BindableNumber<double> globalTrackVolumeAdjust = new BindableNumber<double>(global_track_volume_adjust);

        private Bindable<string> frameworkLocale = null!;

        private IBindable<LocalisationParameters> localisationParameters = null!;

        /// <summary>
        /// Number of unhandled exceptions to allow before aborting execution.
        /// </summary>
        /// <remarks>
        /// When an unhandled exception is encountered, an internal count will be decremented.
        /// If the count hits zero, the game will crash.
        /// Each second, the count is incremented until reaching the value specified.
        /// </remarks>
        protected virtual int UnhandledExceptionsBeforeCrash => DebugUtils.IsDebugBuild ? 0 : 1;

        public OsuGameBase()
        {
            Name = GAME_NAME;

            allowableExceptions = UnhandledExceptionsBeforeCrash;
        }

        [BackgroundDependencyLoader]
        private void load(ReadableKeyCombinationProvider keyCombinationProvider, FrameworkConfigManager frameworkConfig)
        {
            try
            {
                using (var str = File.OpenRead(typeof(OsuGameBase).Assembly.Location))
                    VersionHash = str.ComputeMD5Hash();
            }
            catch
            {
                // special case for android builds, which can't read DLLs from a packed apk.
                // should eventually be handled in a better way.
                VersionHash = $"{Version}-{RuntimeInfo.OS}".ComputeMD5Hash();
            }

            Resources.AddStore(new DllResourceStore(OsuResources.ResourceAssembly));

            dependencies.Cache(realm = new RealmAccess(Storage, CLIENT_DATABASE_FILENAME, Host.UpdateThread));

            dependencies.CacheAs<RulesetStore>(RulesetStore = new RealmRulesetStore(realm, Storage));
            dependencies.CacheAs<IRulesetStore>(RulesetStore);

            Decoder.RegisterDependencies(RulesetStore);

            dependencies.CacheAs(Storage);

            var largeStore = new LargeTextureStore(Host.Renderer, Host.CreateTextureLoaderStore(new NamespacedResourceStore<byte[]>(Resources, @"Textures")));
            largeStore.AddTextureSource(Host.CreateTextureLoaderStore(CreateOnlineStore()));
            dependencies.Cache(largeStore);

            dependencies.CacheAs(LocalConfig);
            dependencies.CacheAs<IGameplaySettings>(LocalConfig);

            InitialiseFonts();

            addFilesWarning();

            Audio.Samples.PlaybackConcurrency = SAMPLE_CONCURRENCY;

            dependencies.Cache(SkinManager = new SkinManager(Storage, realm, Host, Resources, Audio, Scheduler));
            dependencies.CacheAs<ISkinSource>(SkinManager);

            EndpointConfiguration endpoints = CreateEndpoints();

            MessageFormatter.WebsiteRootUrl = endpoints.WebsiteUrl;

            // Initialise localisation
            frameworkLocale = frameworkConfig.GetBindable<string>(FrameworkSetting.Locale);
            frameworkLocale.BindValueChanged(_ => updateLanguage());

            localisationParameters = Localisation.CurrentParameters.GetBoundCopy();
            localisationParameters.BindValueChanged(_ => updateLanguage(), true);

            CurrentLanguage.BindValueChanged(val => frameworkLocale.Value = val.NewValue.ToCultureCode());

            dependencies.CacheAs(API ??= new APIAccess(this, LocalConfig, endpoints, VersionHash));

            var defaultBeatmap = new DummyWorkingBeatmap(Audio, Textures);

            dependencies.Cache(difficultyCache = new BeatmapDifficultyCache());

            // ordering is important here to ensure foreign keys rules are not broken in ModelStore.Cleanup()
            dependencies.Cache(ScoreManager = new ScoreManager(RulesetStore, () => BeatmapManager, Storage, realm, API, LocalConfig));

            dependencies.Cache(BeatmapManager = new BeatmapManager(Storage, realm, API, Audio, Resources, Host, defaultBeatmap, difficultyCache, performOnlineLookups: true));
            dependencies.CacheAs<IWorkingBeatmapCache>(BeatmapManager);

            dependencies.Cache(BeatmapDownloader = new BeatmapModelDownloader(BeatmapManager, API));
            dependencies.Cache(ScoreDownloader = new ScoreModelDownloader(ScoreManager, API));

            // Add after all the above cache operations as it depends on them.
            base.Content.Add(difficultyCache);

            // TODO: OsuGame or OsuGameBase?
            dependencies.CacheAs(beatmapUpdater = CreateBeatmapUpdater());
            dependencies.CacheAs(SpectatorClient = new OnlineSpectatorClient(endpoints));
            dependencies.CacheAs(MultiplayerClient = new OnlineMultiplayerClient(endpoints));
            dependencies.CacheAs(metadataClient = new OnlineMetadataClient(endpoints));

            base.Content.Add(new BeatmapOnlineChangeIngest(beatmapUpdater, realm, metadataClient));

            BeatmapManager.ProcessBeatmap = (beatmapSet, scope) => beatmapUpdater.Process(beatmapSet, scope);

            dependencies.Cache(userCache = new UserLookupCache());
            base.Content.Add(userCache);

            dependencies.Cache(beatmapCache = new BeatmapLookupCache());
            base.Content.Add(beatmapCache);

            dependencies.CacheAs<IRulesetConfigCache>(rulesetConfigCache = new RulesetConfigCache(realm, RulesetStore));

            var powerStatus = CreateBatteryInfo();
            if (powerStatus != null)
                dependencies.CacheAs(powerStatus);

            dependencies.Cache(SessionStatics = new SessionStatics());
            dependencies.Cache(hitErrorTracker = new SessionAverageHitErrorTracker());
            dependencies.Cache(Colours = new OsuColour());

            RegisterImportHandler(BeatmapManager);
            RegisterImportHandler(ScoreManager);
            RegisterImportHandler(SkinManager);

            // drop track volume game-wide to leave some head-room for UI effects / samples.
            // this means that for the time being, gameplay sample playback is louder relative to the audio track, compared to stable.
            // we may want to revisit this if users notice or complain about the difference (consider this a bit of a trial).
            Audio.Tracks.AddAdjustment(AdjustableProperty.Volume, globalTrackVolumeAdjust);

            Beatmap = new NonNullableBindable<WorkingBeatmap>(defaultBeatmap);

            dependencies.CacheAs<IBindable<WorkingBeatmap>>(Beatmap);
            dependencies.CacheAs(Beatmap);

            dependencies.Cache(LeaderboardManager = new LeaderboardManager());
            base.Content.Add(LeaderboardManager);

            // add api components to hierarchy.
            if (API is APIAccess apiAccess)
                base.Content.Add(apiAccess);

            base.Content.Add(SpectatorClient);
            base.Content.Add(MultiplayerClient);
            base.Content.Add(metadataClient);

            base.Content.Add(rulesetConfigCache);

            PreviewTrackManager previewTrackManager;
            dependencies.Cache(previewTrackManager = new PreviewTrackManager(BeatmapManager.BeatmapTrackStore));
            base.Content.Add(previewTrackManager);

            base.Content.Add(MusicController = new MusicController());
            dependencies.CacheAs(MusicController);

            MusicController.TrackChanged += onTrackChanged;
            base.Content.Add(beatmapClock);

            GlobalActionContainer globalBindings;

            OsuMenuSamples menuSamples;
            dependencies.Cache(menuSamples = new OsuMenuSamples());
            base.Content.Add(menuSamples);

            base.Content.Add(SafeAreaContainer = new SafeAreaContainer
            {
                SafeAreaOverrideEdges = SafeAreaOverrideEdges,
                RelativeSizeAxes = Axes.Both,
                Child = CreateScalingContainer().WithChild(globalBindings = new GlobalActionContainer(this)
                {
                    Children = new Drawable[]
                    {
                        (GlobalCursorDisplay = new GlobalCursorDisplay
                        {
                            RelativeSizeAxes = Axes.Both
                        }).WithChild(content = new OsuTooltipContainer(GlobalCursorDisplay.MenuCursor)
                        {
                            RelativeSizeAxes = Axes.Both
                        }),
                    }
                })
            });

            base.Content.Add(new TouchInputInterceptor());
            base.Content.Add(hitErrorTracker);

            KeyBindingStore = new RealmKeyBindingStore(realm, keyCombinationProvider);
            KeyBindingStore.Register(globalBindings, RulesetStore.AvailableRulesets);
            dependencies.Cache(KeyBindingStore);

            dependencies.Cache(globalBindings);

            Ruleset.BindValueChanged(onRulesetChanged);
            Beatmap.BindValueChanged(onBeatmapChanged);

            // make config aware of how to lookup skins for on-screen display purposes.
            // if this becomes a more common thing, tracked settings should be reconsidered to allow local DI.
            LocalConfig.LookupSkinName = id => SkinManager.Query(s => s.ID == id)?.ToString() ?? "Unknown";
            LocalConfig.LookupKeyBindings = l => KeyBindingStore.GetBindingsStringFor(l);
        }

        private void updateLanguage() => CurrentLanguage.Value = LanguageExtensions.GetLanguageFor(frameworkLocale.Value, localisationParameters.Value);

        private void addFilesWarning()
        {
            const string filename = "IMPORTANT READ ME.txt";

            if (!Storage.Exists(filename))
            {
                using (var stream = Storage.CreateFileSafely(filename))
                using (var textWriter = new StreamWriter(stream))
                {
                    textWriter.WriteLine(@"This folder contains all your user files and configuration.");
                    textWriter.WriteLine(@"Please DO NOT make manual changes to this folder.");
                    textWriter.WriteLine();
                    textWriter.WriteLine(@"- If you want to back up your game files, please back up THE ENTIRETY OF THIS DIRECTORY.");
                    textWriter.WriteLine(@"- If you want to delete all of your game files, please delete THE ENTIRETY OF THIS DIRECTORY.");
                    textWriter.WriteLine();
                    textWriter.WriteLine(@"To be very clear, the ""files/"" directory inside this directory stores all the raw pieces of your beatmaps, skins, and replays.");
                    textWriter.WriteLine(@"Importantly, it is NOT the only directory you need a backup of to avoid losing data. If you copy only the ""files/"" directory, YOU WILL LOSE DATA.");
                    textWriter.WriteLine();
                    textWriter.WriteLine(@"For more information on how these files are organised,");
                    textWriter.WriteLine(@"see https://github.com/ppy/osu/wiki/User-file-storage");
                }
            }
        }

        private void onTrackChanged(WorkingBeatmap beatmap, TrackChangeDirection direction) => beatmapClock.ChangeSource(beatmap.Track);

        protected virtual void InitialiseFonts()
        {
            AddFont(Resources, @"Fonts/Torus/Torus-Regular");
            AddFont(Resources, @"Fonts/Torus/Torus-Light");
            AddFont(Resources, @"Fonts/Torus/Torus-SemiBold");
            AddFont(Resources, @"Fonts/Torus/Torus-Bold");

            AddFont(Resources, @"Fonts/Torus-Alternate/Torus-Alternate-Regular");
            AddFont(Resources, @"Fonts/Torus-Alternate/Torus-Alternate-Light");
            AddFont(Resources, @"Fonts/Torus-Alternate/Torus-Alternate-SemiBold");
            AddFont(Resources, @"Fonts/Torus-Alternate/Torus-Alternate-Bold");

            AddFont(Resources, @"Fonts/Inter/Inter-Regular");
            AddFont(Resources, @"Fonts/Inter/Inter-RegularItalic");
            AddFont(Resources, @"Fonts/Inter/Inter-Light");
            AddFont(Resources, @"Fonts/Inter/Inter-LightItalic");
            AddFont(Resources, @"Fonts/Inter/Inter-SemiBold");
            AddFont(Resources, @"Fonts/Inter/Inter-SemiBoldItalic");
            AddFont(Resources, @"Fonts/Inter/Inter-Bold");
            AddFont(Resources, @"Fonts/Inter/Inter-BoldItalic");

            AddFont(Resources, @"Fonts/Noto/Noto-Basic");
            AddFont(Resources, @"Fonts/Noto/Noto-Bopomofo");
            AddFont(Resources, @"Fonts/Noto/Noto-CJK-Basic");
            AddFont(Resources, @"Fonts/Noto/Noto-CJK-Compatibility");
            AddFont(Resources, @"Fonts/Noto/Noto-Hangul");
            AddFont(Resources, @"Fonts/Noto/Noto-Thai");

            AddFont(Resources, @"Fonts/Venera/Venera-Light");
            AddFont(Resources, @"Fonts/Venera/Venera-Bold");
            AddFont(Resources, @"Fonts/Venera/Venera-Black");

            Fonts.AddStore(new OsuIcon.OsuIconStore(Textures));
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            var localeMappings = Enum.GetValues<Language>().Select(language =>
            {
#if DEBUG
                if (language == Language.debug)
                    return new LocaleMapping("debug", new DebugLocalisationStore());
#endif

                string cultureCode = language.ToCultureCode();

                try
                {
                    return new LocaleMapping(new ResourceManagerLocalisationStore(cultureCode));
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Could not load localisations for language \"{cultureCode}\"");
                    return null;
                }
            }).Where(m => m != null);

            Localisation.AddLocaleMappings(localeMappings);
        }

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent) =>
            dependencies = new DependencyContainer(base.CreateChildDependencies(parent));

        public override void SetHost(GameHost host)
        {
            base.SetHost(host);

            // may be non-null for certain tests
            Storage ??= host.Storage;

            LocalConfig ??= UseDevelopmentServer
                ? new DevelopmentOsuConfigManager(Storage)
                : new OsuConfigManager(Storage);

            host.ExceptionThrown += onExceptionThrown;
        }

        #region Exit handling

        /// <summary>
        /// Use to programatically exit the game as if the user was triggering via alt-f4.
        /// By default, will keep persisting until an exit occurs (exit may be blocked multiple times).
        /// May be interrupted (see <see cref="OsuGame"/>'s override).
        /// </summary>
        public virtual void AttemptExit()
        {
            if (!OnExiting())
                Exit();
            else
                Scheduler.AddDelayed(AttemptExit, 2000);
        }

        /// <summary>
        /// An action that restarts the application after it has exited.
        /// </summary>
        [CanBeNull]
        public Action RestartOnExitAction { private get; set; }

        /// <summary>
        /// Signals that the application should not be restarted after it is exited.
        /// </summary>
        public void CancelRestartOnExit()
        {
            RestartOnExitAction = null;
        }

        /// <summary>
        /// If supported by the platform, the game will automatically restart after the next exit.
        /// </summary>
        /// <returns>Whether a restart operation was queued.</returns>
        public virtual bool RestartAppWhenExited() => false;

        #endregion

        /// <summary>
        /// Perform migration of user data to a specified path.
        /// </summary>
        /// <param name="path">The path to migrate to.</param>
        /// <returns>Whether migration succeeded to completion. If <c>false</c>, some files were left behind.</returns>
        /// <exception cref="TimeoutException"></exception>
        public bool Migrate(string path)
        {
            Logger.Log($@"Migrating osu! data from ""{Storage.GetFullPath(string.Empty)}"" to ""{path}""...");

            IDisposable realmBlocker = null;

            try
            {
                ManualResetEventSlim readyToRun = new ManualResetEventSlim();

                bool success = false;

                Scheduler.Add(() =>
                {
                    try
                    {
                        realmBlocker = realm.BlockAllOperations("migration");
                        success = true;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Attempting to block all operations failed: {ex}", LoggingTarget.Database);
                    }

                    readyToRun.Set();
                }, false);

                if (!readyToRun.Wait(30000) || !success)
                    throw new TimeoutException("Attempting to block for migration took too long.");

                bool? cleanupSucceeded = (Storage as OsuStorage)?.Migrate(Host.GetStorage(path));

                Logger.Log(@"Migration complete!");
                return cleanupSucceeded != false;
            }
            finally
            {
                realmBlocker?.Dispose();
            }
        }

        protected virtual IBeatmapUpdater CreateBeatmapUpdater() => new BeatmapUpdater(BeatmapManager, difficultyCache, API, Storage);

        protected override UserInputManager CreateUserInputManager() => new OsuUserInputManager();

        protected virtual BatteryInfo CreateBatteryInfo() => null;

        protected virtual Container CreateScalingContainer() => new DrawSizePreservingFillContainer();

        protected override Storage CreateStorage(GameHost host, Storage defaultStorage) => new OsuStorage(host, defaultStorage);

        /// <summary>
        /// Creates an input settings subsection for an <see cref="InputHandler"/>.
        /// </summary>
        /// <remarks>Should be overriden per-platform to provide settings for platform-specific handlers.</remarks>
        public virtual SettingsSubsection CreateSettingsSubsectionFor(InputHandler handler)
        {
            // One would think that this could be moved to the `OsuGameDesktop` class, but doing so means that
            // OsuGameTestScenes will not show any input options (as they are based on OsuGame not OsuGameDesktop).
            //
            // This in turn makes it hard for ruleset creators to adjust input settings while testing their ruleset
            // within the test browser interface.
            if (RuntimeInfo.IsDesktop)
            {
                switch (handler)
                {
                    case ITabletHandler th:
                        return new TabletSettings(th);
                }
            }

            switch (handler)
            {
                case MouseHandler mh:
                    return new MouseSettings(mh);

                case JoystickHandler jh:
                    return new JoystickSettings(jh);

                case TouchHandler th:
                    return new TouchSettings(th);

                case MidiHandler:
                    return new InputSubsection(handler);

                // return null for handlers that shouldn't have settings.
                default:
                    return null;
            }
        }

        private void onBeatmapChanged(ValueChangedEvent<WorkingBeatmap> beatmap)
        {
            if (IsLoaded && !ThreadSafety.IsUpdateThread)
                throw new InvalidOperationException("Global beatmap bindable must be changed from update thread.");

            Logger.Log($"Game-wide working beatmap updated to {beatmap.NewValue}");
        }

        private void onRulesetChanged(ValueChangedEvent<RulesetInfo> r)
        {
            if (IsLoaded && !ThreadSafety.IsUpdateThread)
                throw new InvalidOperationException("Global ruleset bindable must be changed from update thread.");

            Ruleset instance = null;

            if (r.NewValue?.Available == true)
            {
                try
                {
                    instance = r.NewValue.CreateInstance();
                }
                catch (Exception e)
                {
                    Rulesets.RulesetStore.LogRulesetFailure(r.NewValue, e);
                }
            }

            if (instance == null)
            {
                // reject the change if the ruleset is not available.
                revertRulesetChange();
                return;
            }

            var dict = new Dictionary<ModType, IReadOnlyList<Mod>>();

            try
            {
                foreach (ModType type in Enum.GetValues<ModType>())
                {
                    dict[type] = instance.GetModsFor(type)
                                         // Rulesets should never return null mods, but let's be defensive just in case.
                                         // ReSharper disable once ConditionIsAlwaysTrueOrFalse
                                         .Where(mod => mod != null)
                                         .ToList();
                }
            }
            catch (Exception e)
            {
                Rulesets.RulesetStore.LogRulesetFailure(r.NewValue, e);
                revertRulesetChange();
                return;
            }

            AvailableMods.Value = dict;

            if (SelectedMods.Disabled)
                return;

            var convertedMods = SelectedMods.Value.Select(mod =>
            {
                var newMod = instance.CreateModFromAcronym(mod.Acronym);
                newMod?.CopyCommonSettingsFrom(mod);
                return newMod;
            }).Where(newMod => newMod != null).ToList();

            if (!ModUtils.CheckValidForGameplay(convertedMods, out var invalid))
                invalid.ForEach(newMod => convertedMods.Remove(newMod));

            SelectedMods.Value = convertedMods;

            void revertRulesetChange() => Ruleset.Value = r.OldValue?.Available == true ? r.OldValue : RulesetStore.AvailableRulesets.First();
        }

        private int allowableExceptions;

        /// <summary>
        /// Allows a maximum of one unhandled exception, per second of execution.
        /// </summary>
        /// <returns>Whether to ignore the exception and continue running.</returns>
        private bool onExceptionThrown(Exception ex)
        {
            if (Interlocked.Decrement(ref allowableExceptions) < 0)
            {
                Logger.Log("Too many unhandled exceptions, crashing out.");
                RulesetStore?.TryDisableCustomRulesetsCausing(ex);
                return false;
            }

            Logger.Log($"Unhandled exception has been allowed with {allowableExceptions} more allowable exceptions.");
            // restore the stock of allowable exceptions after a short delay.
            Task.Delay(1000).ContinueWith(_ => Interlocked.Increment(ref allowableExceptions));

            return true;
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            RulesetStore?.Dispose();
            LocalConfig?.Dispose();

            beatmapUpdater?.Dispose();

            realm?.Dispose();

            if (Host != null)
                Host.ExceptionThrown -= onExceptionThrown;

            RestartOnExitAction?.Invoke();
        }

        ControlPointInfo IBeatSyncProvider.ControlPoints => Beatmap.Value.BeatmapLoaded ? Beatmap.Value.Beatmap.ControlPointInfo : null;
        IClock IBeatSyncProvider.Clock => beatmapClock;
        ChannelAmplitudes IHasAmplitudes.CurrentAmplitudes => Beatmap.Value.TrackLoaded ? Beatmap.Value.Track.CurrentAmplitudes : ChannelAmplitudes.Empty;

        public class MyClock : FramedClock, IAdjustableClock
        {
            double IAdjustableClock.Rate { get => Rate; set => throw new NotImplementedException(); }

            public MyClock(IClock source)
                : base(source, false)
            {
            }

            public void Reset()
            {
            }

            public void ResetSpeedAdjustments()
            {
            }

            public bool Seek(double position)
            {
                return true;
            }

            public void Start()
            {
            }

            public void Stop()
            {
            }
        }

        private double replayTime = 0;
        private bool replayTimeStarted = false;
        private ReplayPlayer player = null;

        protected ManualClock ScreenStackTimeSource = new()
        {
            CurrentTime = 0,
            IsRunning = true,
            Rate = 1,
        };
        public IFrameBasedClock ScreenStackClock = null;
        private FFmpegCliProcess ffmpeg;
        public bool Recording = false;
        private const int fps = 60;
        private const double frame_time_ms = 1000.0 / fps;
        protected CapturableScreenStack CaptureScreenStack;
        private IFrameBasedClock originalStackClock;
        private double simulatedTimeToInvokeAction;
        private Action actionWhenSimulatedTimeReached;

        // Do something after `timeFromNowMs` simulated time ms.
        // This is kept track by ScreenStackClock.CurrentTime in Update().
        public void CaptureInvokeActionIn(Action action, double timeFromNowMs)
        {
            simulatedTimeToInvokeAction = ScreenStackTimeSource.CurrentTime + timeFromNowMs;
            actionWhenSimulatedTimeReached = action;
        }

        // Audio recording stuff
        private const int samplerate = 44100;
        private const int channels = 2;
        private Resolution resolution;
        private int myMixerHandle;
        private byte[] audioBuf = null;

        public class StatisticTimer
        {
            private readonly Stopwatch stopwatch = new();
            private uint count = 0;
            private double sum = 0;
            public double Average => sum / count;
            public void Begin() => stopwatch.Restart();
            public void End()
            {
                sum += stopwatch.Elapsed.TotalMilliseconds;
                ++count;
            }
        }

        // Lock that waits for the Image before advancing replay time
        private bool currentlyCapturing = false;
        private StatisticTimer extractTime = new(), captureTime = new(), audioTime = new();

        // We just count with a double, because Player.GameplayClockContainer actually controls
        // the beatmap's Track... so all we need to do is Seek() to our simulated time!
        protected void StartReplayTime(ReplayPlayer player)
        {
            replayTimeStarted = true;
            this.player = player;
        }

        protected void StartRecording()
        {
            // This allows for a 10-fold speed increase over image.CreateReadOnlyPixelSpan()!
            // See FFmpegCliProcess.WriteFrame().
            SixLabors.ImageSharp.Configuration.Default.PreferContiguousImageBuffers = true;

            replayTimeStarted = false;
            replayTime = 0;
            ScreenStackTimeSource.CurrentTime = 0;
            Recording = true;

            ffmpeg?.Dispose();
            ffmpeg = null;
            currentlyCapturing = false;

            // this.CaptureScreenStack = stack;
            originalStackClock = CaptureScreenStack.Clock;
            CaptureScreenStack.Clock = ScreenStackClock = new MyClock(ScreenStackTimeSource);

            { // Finish transforms of the ENTIRE scene graph
                Drawable current = CaptureScreenStack;
                while (current != null)
                {
                    current.FinishTransforms(propagateChildren: true);
                    current = current.Parent;
                }
            }

            // Create our own mixer to combine TrackMixer and SampleMixer
            myMixerHandle = BassMix.CreateMixerStream(samplerate, channels, BassFlags.MixerNonStop | BassFlags.Decode);
            if (myMixerHandle == 0)
                throw new InvalidOperationException($"CreateMixerStream: ${Bass.LastError}");

            // Just get the resolution since we're letting BASS choose it
            if (Bass.ChannelGetInfo(myMixerHandle, out ChannelInfo info))
                resolution = info.Resolution;
            else
                throw new InvalidOperationException($"BASS error: {Bass.LastError}");

            // Remove mixers from global mixer
            if (!BassMix.MixerRemoveChannel(GetMixerHandle(Audio.TrackMixer)))
                throw new InvalidOperationException($"MixerRemoveChannel: ${Bass.LastError}");
            if (!BassMix.MixerRemoveChannel(GetMixerHandle(Audio.SampleMixer)))
                throw new InvalidOperationException($"MixerRemoveChannel: ${Bass.LastError}");

            // Add mixers to my mixer
            if (!BassMix.MixerAddChannel(myMixerHandle, GetMixerHandle(Audio.TrackMixer), 0))
                throw new InvalidOperationException($"MixerAddChannel: ${Bass.LastError}");
            if (!BassMix.MixerAddChannel(myMixerHandle, GetMixerHandle(Audio.SampleMixer), 0))
                throw new InvalidOperationException($"MixerAddChannel: ${Bass.LastError}");

            Task.Run(() =>
            {
                while (Recording)
                {
                    Thread.Sleep(1_000);
                    Logger.Log($"Average times: Extract = {extractTime.Average}ms; Capture = {captureTime.Average}ms; Audio = {audioTime.Average}ms");
                }
            });

            CaptureScreenStack.OnImageReceived = OnImageReceived;
            Logger.Log("Started rendering replay.", level: LogLevel.Important);
        }

        public void StopRecording()
        {
            if (!Recording)
                return;
            Recording = false;
            player = null;
            replayTimeStarted = false;
            CaptureScreenStack.Clock = originalStackClock;
            ScreenStackClock = null;
            ffmpeg?.Dispose();
            ffmpeg = null;

            // Remove mixers from my mixer
            if (!BassMix.MixerRemoveChannel(GetMixerHandle(Audio.TrackMixer)))
                throw new InvalidOperationException($"MixerRemoveChannel: ${Bass.LastError}");
            if (!BassMix.MixerRemoveChannel(GetMixerHandle(Audio.SampleMixer)))
                throw new InvalidOperationException($"MixerRemoveChannel: ${Bass.LastError}");

            // Add mixers back to global mixer
            if (!BassMix.MixerAddChannel((int)Audio.GlobalMixerHandle.Value, GetMixerHandle(Audio.TrackMixer), 0))
                throw new InvalidOperationException($"MixerAddChannel: ${Bass.LastError}");
            if (!BassMix.MixerAddChannel((int)Audio.GlobalMixerHandle.Value, GetMixerHandle(Audio.SampleMixer), 0))
                throw new InvalidOperationException($"MixerAddChannel: ${Bass.LastError}");

            Logger.Log("Stopped rendering replay.", level: LogLevel.Important);
        }

        protected override void Update()
        {
            base.Update();

            if (!Recording || currentlyCapturing)
                return;

            // TODO: ffmpeg can be inited here using ScheduleAfterChildren()

            if (ScreenStackClock != null)
            {
                if (ScreenStackTimeSource.CurrentTime >= simulatedTimeToInvokeAction)
                    actionWhenSimulatedTimeReached?.Invoke();
                ScreenStackTimeSource.CurrentTime += frame_time_ms;
            }

            // The player was started at the end of OnImageReceived(),
            // giving BASS a lot of time to render audio, so we can record
            // once children have updated with the new clock state.
            ScheduleAfterChildren(() =>
            {
                recordAudio();

                if (replayTimeStarted && !player.HasCompleted)
                {
                    // Stop BEFORE seeking so it STAYS stopped as the image is taken.
                    player.GameplayClockContainer.Stop();
                    player.Seek(replayTime);
                    // We keep the clock stopped as to not take unpredictable images of the screen.

                    // Increment now before we forget.
                    replayTime += frame_time_ms;
                }
            });

            CaptureScreenStack.RequestCapture();
            currentlyCapturing = true;
            captureTime.Begin();
        }

        private void recordAudio()
        {
            if (ffmpeg == null)
                return;

            if (audioBuf == null)
            {
                int afpvf = samplerate / fps;
                int samples = afpvf * channels;
                int sampleSize = ResolutionToByteSize(resolution);
                audioBuf = new byte[samples * sampleSize];
            }

            audioTime.Begin();
            int bytesRead = Bass.ChannelGetData(myMixerHandle, audioBuf, audioBuf.Length);
            if (bytesRead == -1)
                throw new InvalidOperationException($"BASS error: {Bass.LastError}");

            if (!ffmpeg.WriteAudio(audioBuf.AsSpan().Slice(0, bytesRead)))
                Logger.Log("Dropped audio packet, is ffmpeg too slow?", level: LogLevel.Error);
            audioTime.End();
        }

        protected void OnImageReceived(Image<Rgba32> image)
        {
            if (!currentlyCapturing)
                return;
            currentlyCapturing = false;
            captureTime.End();

            if (!Recording || image == null)
                return;

            // We wait for the first image because by this point all
            // transforms should have been finished by StartRecording().
            if (ffmpeg == null)
            {
                ffmpeg = new FFmpegCliProcess
                {
                    OutputFilePath = "out.mp4",
                    VideoSize = new() { X = image.Width, Y = image.Height },
                    Framerate = fps,
                    Samplerate = samplerate,
                    SampleFormat = ResolutionToFfmpegSmpFmt(resolution),
                    Channels = channels
                };
                ffmpeg.Start();
            }

            // Don't use `using` as the FFmpegCliProcess class now queues images for a separate thread to handle.
            // This was needed to deal with Windows' very small anonymous pipe buffers causing deadlocks.
            if (!ffmpeg.WriteFrame(image))
                Logger.Log("Dropped video frame, is ffmpeg too slow?", level: LogLevel.Error);

            if (replayTimeStarted)
            {
                // Start playing so that on the next update we will have audio to record.
                player.GameplayClockContainer.Start();
            }
        }

        public static int GetMixerHandle(AudioMixer mixer)
        {
            // Get the BassAudioMixer type (internal class)  
            Type bassMixerType = typeof(AudioMixer).Assembly
                .GetType("osu.Framework.Audio.Mixing.Bass.BassAudioMixer") ?? throw new InvalidOperationException("BassAudioMixer type not found");

            // Get the Handle property  
            PropertyInfo handleProperty = bassMixerType.GetProperty("Handle") ?? throw new InvalidOperationException("Handle property not found");

            // Get the handle value  
            return (int)handleProperty.GetValue(mixer);
        }

        public static string ResolutionToFfmpegSmpFmt(Resolution r)
        {
            switch (r)
            {
                case Resolution.Float: return "f32le";
                case Resolution.Short: return "s16le";
                case Resolution.Byte: return "u8";
            }
            throw new ArgumentException($"Resolution {r} invalid");
        }

        public static int ResolutionToByteSize(Resolution r)
        {
            switch (r)
            {
                case Resolution.Float: return 4;
                case Resolution.Short: return 2;
                case Resolution.Byte: return 1;
            }
            throw new ArgumentException($"Resolution {r} invalid");
        }
    }
}
