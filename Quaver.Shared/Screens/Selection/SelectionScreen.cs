using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Quaver.API.Enums;
using Quaver.API.Helpers;
using Quaver.Server.Common.Enums;
using Quaver.Server.Common.Objects;
using Quaver.Shared.Audio;
using Quaver.Shared.Config;
using Quaver.Shared.Database.Maps;
using Quaver.Shared.Database.Playlists;
using Quaver.Shared.Database.Scores;
using Quaver.Shared.Database.Settings;
using Quaver.Shared.Discord;
using Quaver.Shared.Graphics.Notifications;
using Quaver.Shared.Graphics.Transitions;
using Quaver.Shared.Modifiers;
using Quaver.Shared.Online;
using Quaver.Shared.Scheduling;
using Quaver.Shared.Screens.Download;
using Quaver.Shared.Screens.Editor;
using Quaver.Shared.Screens.Gameplay;
using Quaver.Shared.Screens.Importing;
using Quaver.Shared.Screens.Loading;
using Quaver.Shared.Screens.Main;
using Quaver.Shared.Screens.Menu;
using Quaver.Shared.Screens.Multiplayer;
using Quaver.Shared.Screens.Select.UI.Leaderboard;
using Quaver.Shared.Screens.Selection.UI;
using Quaver.Shared.Screens.Selection.UI.FilterPanel.Search;
using Quaver.Shared.Screens.Selection.UI.Maps;
using Quaver.Shared.Screens.Selection.UI.Mapsets;
using Wobble.Bindables;
using Wobble.Graphics;
using Wobble.Graphics.UI.Dialogs;
using Wobble.Input;
using Wobble.Logging;

namespace Quaver.Shared.Screens.Selection
{
    public sealed class SelectionScreen : QuaverScreen
    {
        /// <inheritdoc />
        /// <summary>
        /// </summary>
        public override QuaverScreenType Type { get; } = QuaverScreenType.Select;

        /// <summary>
        ///     If the user is in multiplayer, this is the current screen
        /// </summary>
        public MultiplayerScreen MultiplayerScreen { get; }

        /// <summary>
        ///     Stores the currently available mapsets to play in the screen
        /// </summary>
        public Bindable<List<Mapset>> AvailableMapsets { get; private set; }

        /// <summary>
        ///    The user's search term/query
        /// </summary>
        public Bindable<string> CurrentSearchQuery { get; private set; }

        /// <summary>
        ///     The currently active panel on the left side of the screen
        /// </summary>
        public Bindable<SelectContainerPanel> ActiveLeftPanel { get; private set; }

        /// <summary>
        ///     The currently active scroll container on the right-side of the screen
        /// </summary>
        public Bindable<SelectScrollContainerType> ActiveScrollContainer { get; private set; }

        /// <summary>
        /// </summary>
        private Random Rng { get; } = new Random();

        /// <summary>
        ///     Invoked when a random mapset has been selected
        /// </summary>
        public static event EventHandler<RandomMapsetSelectedEventArgs> RandomMapsetSelected;

        /// <summary>
        ///     If the user is currently exporting a mapset
        /// </summary>
        private bool IsExportingMapset { get; set; }

        /// <summary>
        /// </summary>
        public SelectionScreen(MultiplayerScreen multiplayerScreen = null)
        {
            MultiplayerScreen = multiplayerScreen;

            if (MultiplayerScreen != null)
                OnlineManager.Client?.SetGameCurrentlySelectingMap(true);
            else
                SetRichPresence();

            InitializeSearchQueryBindable();
            InitializeAvailableMapsetsBindable();
            InitializeActiveLeftPanelBindable();
            InitializeActiveScrollContainerBindable();
            InitializeSelectedPlaylist();

            // Do initial filtering of mapsets for the screen
            AvailableMapsets.Value = MapsetHelper.FilterMapsets(CurrentSearchQuery);

            MapManager.MapsetDeleted += OnMapsetDeleted;
            MapManager.MapDeleted += OnMapDeleted;
            MapManager.MapUpdated += OnMapUpdated;

            ConfigManager.AutoLoadOsuBeatmaps.ValueChanged += OnAutoLoadOsuBeatmapsChanged;

            View = new SelectionScreenView(this);
        }

        /// <inheritdoc />
        /// <summary>
        /// </summary>
        public override void OnFirstUpdate()
        {
            FadeAudioTrackIn();
            base.OnFirstUpdate();
        }

        /// <inheritdoc />
        /// <summary>
        /// </summary>
        /// <param name="gameTime"></param>
        public override void Update(GameTime gameTime)
        {
            ImportMaps();
            HandleInput(gameTime);
            base.Update(gameTime);
        }

        /// <inheritdoc />
        /// <summary>
        /// </summary>
        public override void Destroy()
        {
            CurrentSearchQuery?.Dispose();
            AvailableMapsets?.Dispose();
            ActiveLeftPanel?.Dispose();
            ActiveScrollContainer?.Dispose();
            RandomMapsetSelected = null;
            MapManager.MapsetDeleted -= OnMapsetDeleted;
            MapManager.MapUpdated -= OnMapUpdated;

            // ReSharper disable once DelegateSubtraction
            ConfigManager.AutoLoadOsuBeatmaps.ValueChanged -= OnAutoLoadOsuBeatmapsChanged;

            base.Destroy();
        }

        /// <summary>
        ///     Initializes the bindable which stores the user's search query <see cref="CurrentSearchQuery"/>
        /// </summary>
        private void InitializeSearchQueryBindable()
            => CurrentSearchQuery = new Bindable<string>(null) { Value = FilterPanelSearchBox.PreviousSearchTerm };

        /// <summary>
        ///     Initializes the bindable which stores the available mapsets for the screen <see cref="AvailableMapsets"/>
        /// </summary>
        private void InitializeAvailableMapsetsBindable()
            => AvailableMapsets = new Bindable<List<Mapset>>(null) { Value = new List<Mapset>()};

        /// <summary>
        ///     Initializes the bindable which keeps track of which panel on the left side of the screen is active
        /// </summary>
        private void InitializeActiveLeftPanelBindable()
        {
            ActiveLeftPanel = new Bindable<SelectContainerPanel>(SelectContainerPanel.Leaderboard)
            {
                Value = SelectContainerPanel.Leaderboard
            };
        }

        /// <summary>
        ///     Initializes the bindable which keeps track of which scroll container is active
        /// </summary>
        private void InitializeActiveScrollContainerBindable()
        {
            ActiveScrollContainer = new Bindable<SelectScrollContainerType>(SelectScrollContainerType.Mapsets)
            {
                Value = SelectScrollContainerType.Mapsets
            };

            if (ConfigManager.SelectGroupMapsetsBy.Value == GroupMapsetsBy.Playlists)
                ActiveScrollContainer.Value = SelectScrollContainerType.Playlists;

            // If the user is playing maps from a playlist, then automatically use the mapset container
            if (PlaylistManager.Selected.Value != null && ConfigManager.SelectGroupMapsetsBy.Value == GroupMapsetsBy.Playlists)
                ActiveScrollContainer.Value = SelectScrollContainerType.Mapsets;
        }

        /// <summary>
        ///     If the initial playlist is null this will set it apporpriately
        /// </summary>
        private void InitializeSelectedPlaylist()
        {
            if (PlaylistManager.Selected.Value == null && PlaylistManager.Playlists.Count != 0)
                PlaylistManager.Selected.Value = PlaylistManager.Playlists.First();
        }

        /// <summary>
        ///     Handles all input for the screen
        /// </summary>
        /// <param name="gameTime"></param>
        private void HandleInput(GameTime gameTime)
        {
            if (DialogManager.Dialogs.Count != 0)
                return;

            HandleKeyPressEscape();
            HandleKeyPressF1();
            HandleKeyPressF2();
            HandleKeyPressF3();
            HandleKeyPressEnter();
            HandleKeyPressControlInput();
            HandleThumb1MouseButtonClick();
            HandleKeyPressTab();
        }

        /// <summary>
        ///     Handles when the user presses escape
        /// </summary>
        private void HandleKeyPressEscape()
        {
            if (!KeyboardManager.IsUniqueKeyPress(Keys.Escape))
                return;

            switch (ActiveLeftPanel.Value)
            {
                case SelectContainerPanel.Leaderboard:
                    if (ActiveScrollContainer.Value == SelectScrollContainerType.Maps)
                    {
                        ActiveScrollContainer.Value = SelectScrollContainerType.Mapsets;
                        return;
                    }

                    if (ActiveScrollContainer.Value == SelectScrollContainerType.Mapsets &&
                        ConfigManager.SelectGroupMapsetsBy.Value == GroupMapsetsBy.Playlists)
                    {
                        ActiveScrollContainer.Value = SelectScrollContainerType.Playlists;
                        return;
                    }

                    ExitToMenu();
                    break;
                case SelectContainerPanel.Modifiers:
                    ActiveLeftPanel.Value = SelectContainerPanel.Leaderboard;
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        ///     Handles when the user presses F1
        /// </summary>
        private void HandleKeyPressF1()
        {
            if (KeyboardManager.IsUniqueKeyPress(Keys.F1) && ActiveLeftPanel.Value != SelectContainerPanel.Modifiers)
                ActiveLeftPanel.Value = SelectContainerPanel.Modifiers;
        }

        /// <summary>
        ///     Handles random map selection through key press
        /// </summary>
        private void HandleKeyPressF2()
        {
            if (!KeyboardManager.IsUniqueKeyPress(Keys.F2))
                return;

            SelectRandomMap();
        }

        /// <summary>
        ///    Handles exporting mapsets through F3 key press
        /// </summary>
        private void HandleKeyPressF3()
        {
            if (KeyboardManager.CurrentState.IsKeyDown(Keys.LeftControl) || KeyboardManager.CurrentState.IsKeyDown(Keys.RightControl))
                return;

            if (!KeyboardManager.IsUniqueKeyPress(Keys.F3))
                return;

            ExportSelectedMapset();
        }

        /// <summary>
        ///     Handles when the user presses the enter key
        /// </summary>
        private void HandleKeyPressEnter()
        {
            if (!KeyboardManager.IsUniqueKeyPress(Keys.Enter))
                return;

            switch (ActiveScrollContainer.Value)
            {
                case SelectScrollContainerType.Mapsets:
                    if (MapsetHelper.IsSingleDifficultySorted())
                        ExitToGameplay();
                    else
                        ActiveScrollContainer.Value = SelectScrollContainerType.Maps;
                    break;
                case SelectScrollContainerType.Maps:
                    ExitToGameplay();
                    break;
                case SelectScrollContainerType.Playlists:
                    ActiveScrollContainer.Value = SelectScrollContainerType.Mapsets;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        /// <summary>
        ///     Handles when the user presses the tab key
        /// </summary>
        private void HandleKeyPressTab()
        {
            if (!KeyboardManager.IsUniqueKeyPress(Keys.Tab))
                return;

            var index = (int) ConfigManager.LeaderboardSection.Value;

            if (index + 1 < Enum.GetNames(typeof(LeaderboardType)).Length)
                ConfigManager.LeaderboardSection.Value = (LeaderboardType) index + 1;
            else
                ConfigManager.LeaderboardSection.Value = LeaderboardType.Local;
        }

        /// <summary>
        ///     Handles when the user holds control down and performs input actions
        /// </summary>
        private void HandleKeyPressControlInput()
        {
            if (!KeyboardManager.CurrentState.IsKeyDown(Keys.LeftControl) &&
                !KeyboardManager.CurrentState.IsKeyDown(Keys.RightControl))
                return;

            if (OnlineManager.CurrentGame != null)
                return;

            // Increase rate.
            if (KeyboardManager.IsUniqueKeyPress(Keys.OemPlus) || KeyboardManager.IsUniqueKeyPress(Keys.Add))
                ModManager.AddSpeedMods(GetNextRate(true));

            // Decrease Rate
            if (KeyboardManager.IsUniqueKeyPress(Keys.OemMinus) || KeyboardManager.IsUniqueKeyPress(Keys.Subtract))
                ModManager.AddSpeedMods(GetNextRate(false));

            // Change from pitched to non-pitched
            if (KeyboardManager.IsUniqueKeyPress(Keys.D0))
                ConfigManager.Pitched.Value = !ConfigManager.Pitched.Value;

            ChangeScrollSpeed();
        }

        /// <summary>
        ///     Handles when the user clicks the thumb1 mouse button.
        ///     If the user has the maps container open, this'll bring back the mapset container
        /// </summary>
        private void HandleThumb1MouseButtonClick()
        {
            if (!MouseManager.IsUniqueClick(MouseButton.Thumb1))
                return;

            var view = (SelectionScreenView) View;

            switch (ActiveScrollContainer.Value)
            {
                case SelectScrollContainerType.Mapsets:
                    if (ConfigManager.SelectGroupMapsetsBy.Value != GroupMapsetsBy.Playlists)
                        return;

                    if (!view.MapsetContainer.IsHovered())
                        return;

                    ActiveScrollContainer.Value = SelectScrollContainerType.Playlists;
                    break;
                case SelectScrollContainerType.Maps:
                    if (!view.MapContainer.IsHovered())
                        return;

                    ActiveScrollContainer.Value = SelectScrollContainerType.Mapsets;
                    break;
                case SelectScrollContainerType.Playlists:
                    return;
            }
        }

        /// <summary>
        ///     Gets the adjacent rate value.
        ///
        ///     For example, if the current rate is 1.0x, the adjacent value would be either 0.95x or 1.1x,
        ///     depending on the argument.
        /// </summary>
        /// <param name="faster">If true, returns the higher rate, otherwise the lower rate.</param>
        /// <returns></returns>
        private static float GetNextRate(bool faster)
        {
            var current = ModHelper.GetRateFromMods(ModManager.Mods);
            var adjustment = 0.1f;

            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (current < 1.0f || (current == 1.0f && !faster))
                adjustment = 0.05f;

            var next = current + adjustment * (faster ? 1f : -1f);
            return (float) Math.Round(next, 2);
        }

        /// <summary>
        ///     Fades the track back to the config setting
        /// </summary>
        private void FadeAudioTrackIn()
        {
            if (ConfigManager.VolumeMusic == null)
                return;

            if (AudioEngine.Track != null && AudioEngine.Track.IsPlaying)
                AudioEngine.Track?.Fade(100, 300);
        }

        /// <summary>
        ///     Changes the user's scroll speed for the selected game mode
        ///     CTRL+F3/CTRL+F4
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private void ChangeScrollSpeed()
        {
            BindableInt scrollSpeed;

            switch (MapManager.Selected.Value.Mode)
            {
                case GameMode.Keys4:
                    scrollSpeed = ConfigManager.ScrollSpeed4K;
                    break;
                case GameMode.Keys7:
                    scrollSpeed = ConfigManager.ScrollSpeed7K;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            var changed = false;

            // Change scroll speed down
            if (KeyboardManager.IsUniqueKeyPress(Keys.F3))
            {
                scrollSpeed.Value--;
                changed = true;
            }
            else if (KeyboardManager.IsUniqueKeyPress(Keys.F4))
            {
                scrollSpeed.Value++;
                changed = true;
            }

            if (changed)
            {
                NotificationManager.Show(NotificationLevel.Info, $"Your {ModeHelper.ToShortHand(MapManager.Selected.Value.Mode)} " +
                                                                 $"scroll speed has been changed to: {scrollSpeed.Value}");
            }
        }

        /// <summary>
        ///     Selects a random map
        /// </summary>
        public void SelectRandomMap()
        {
            if (AvailableMapsets.Value.Count == 0)
                return;

            ActiveScrollContainer.Value = SelectScrollContainerType.Mapsets;

            var index = Rng.Next(AvailableMapsets.Value.Count);
            var mapIndex = Rng.Next(AvailableMapsets.Value[index].Maps.Count);

            MapManager.Selected.Value = AvailableMapsets.Value[index].Maps[mapIndex];
            RandomMapsetSelected?.Invoke(this, new RandomMapsetSelectedEventArgs(AvailableMapsets.Value[index], index));
        }

        /// <summary>
        ///     Exits the screen to gameplay
        /// </summary>
        public void ExitToGameplay()
        {
            if (MapManager.Selected.Value == null)
                return;

            if (OnlineManager.CurrentGame != null)
            {
                SelectMultiplayerMap();
                return;
            }

            if (OnlineManager.IsSpectatingSomeone)
                OnlineManager.Client?.StopSpectating();

            Exit(() => new MapLoadingScreen(new List<Score>()));
        }

        /// <summary>
        ///     Exits the current screen back to menu
        /// </summary>
        public void ExitToMenu()
        {
            Exit(() =>
            {
                if (MultiplayerScreen != null)
                {
                    MultiplayerScreen.Exiting = false;
                    OnlineManager.Client?.SetGameCurrentlySelectingMap(false);
                    return MultiplayerScreen;
                }

                return new MainMenuScreen();
            });
        }

        /// <summary>
        ///     Exits the current screen and goes to the editor
        /// </summary>
        public void ExitToEditor()
        {
            if (MapManager.Selected.Value == null)
                return;

            if (AudioEngine.Track != null && AudioEngine.Track.IsPlaying)
                AudioEngine.Track.Stop();

            if (OnlineManager.CurrentGame != null)
            {
                NotificationManager.Show(NotificationLevel.Error, "You cannot use the editor while playing multiplayer.");
                return;
            }

            if (OnlineManager.IsSpectatingSomeone)
                OnlineManager.Client?.StopSpectating();

            Exit(() => new EditorScreen(MapManager.Selected.Value.LoadQua()));
        }

        /// <summary>
        ///     Handles the selection of multiplayer maps
        /// </summary>
        private void SelectMultiplayerMap()
        {
            var map = MapManager.Selected.Value;

            if (!CheckMultiplayerDifficultyRange())
                return;

            if (!CheckMultiplayerSongLength())
                return;

            if (!CheckMultiplayerGameMode())
                return;

            if (!CheckMultiplayerLongNotePercentage())
                return;

            // Start the fade out early to make it look like the screen is loading
            Transitioner.FadeIn();

            ThreadScheduler.Run(() =>
            {
                OnlineManager.Client.ChangeMultiplayerGameMap(map.Md5Checksum, map.MapId,
                    map.MapSetId, map.ToString(), (byte) map.Mode,map.DifficultyFromMods(ModManager.Mods),
                    map.GetDifficultyRatings(), map.GetJudgementCount(), MapManager.Selected.Value.GetAlternativeMd5());

                OnlineManager.Client.SetGameCurrentlySelectingMap(false);

                Exit(() =>
                {
                    MultiplayerScreen.Exiting = false;
                    return MultiplayerScreen;
                });
            });
        }

        /// <summary>
        /// </summary>
        /// <returns></returns>
        private bool CheckMultiplayerDifficultyRange()
        {
            var diff = MapManager.Selected.Value.DifficultyFromMods(ModManager.Mods);

            // Prevent host from picking a map not within difficulty range
            if (diff < OnlineManager.CurrentGame.MinimumDifficultyRating || diff > OnlineManager.CurrentGame.MaximumDifficultyRating)
            {
                NotificationManager.Show(NotificationLevel.Error,
                    $"Difficulty rating must be between {OnlineManager.CurrentGame.MinimumDifficultyRating} " +
                    $"and {OnlineManager.CurrentGame.MaximumDifficultyRating} for this multiplayer match!");

                return false;
            }

            return true;
        }

        /// <summary>
        ///     Checks if the host is selecting a map that fits the length criteria
        /// </summary>
        /// <returns></returns>
        private bool CheckMultiplayerSongLength()
        {
            var length = MapManager.Selected.Value.SongLength * ModHelper.GetRateFromMods(ModManager.Mods) / 1000;

            // Pevent host from picking a map not in max song length range
            if (length > OnlineManager.CurrentGame.MaximumSongLength)
            {
                NotificationManager.Show(NotificationLevel.Error,
                    $"The maximum length allowed for this multiplayer match is: {OnlineManager.CurrentGame.MaximumSongLength} seconds");

                return false;
            }

            return true;
        }

        /// <summary>
        ///     Checks if the host is selecting a map that fits the game mode criteria
        /// </summary>
        /// <returns></returns>
        private bool CheckMultiplayerGameMode()
        {
            // Prevent disallowed game modes from being selected
            if (!OnlineManager.CurrentGame.AllowedGameModes.Contains((byte) MapManager.Selected.Value.Mode))
            {
                NotificationManager.Show(NotificationLevel.Error, "You cannot pick maps of this game mode in this multiplayer match!");
                return false;
            }

            return true;
        }

        /// <summary>
        ///     Checks if the host is selecting a map within the LN% range
        /// </summary>
        /// <returns></returns>
        private bool CheckMultiplayerLongNotePercentage()
        {
            var map = MapManager.Selected.Value;

            // Prevent maps not in range of the minimum and maximum LN%
            if (map.LNPercentage < OnlineManager.CurrentGame.MinimumLongNotePercentage || map.LNPercentage > OnlineManager.CurrentGame.MaximumLongNotePercentage)
            {
                NotificationManager.Show(NotificationLevel.Error,
                    $"You cannot select this map. The long note percentage must be between " +
                    $"{OnlineManager.CurrentGame.MinimumLongNotePercentage}%-{OnlineManager.CurrentGame.MaximumLongNotePercentage}% " +
                                                                  $"for this multiplayer match.");

                return false;
            }

            return true;
        }

        /// <summary>
        ///     Exports the current mapset to a zip file and opens it in the file manager
        /// </summary>
        public void ExportSelectedMapset()
        {
            if (MapManager.Selected.Value == null)
                return;

            if (IsExportingMapset)
            {
                NotificationManager.Show(NotificationLevel.Warning, "Slow down! You must wait for your previous mapset to export");
                return;
            }

            IsExportingMapset = true;

            ThreadScheduler.Run(() =>
            {
                NotificationManager.Show(NotificationLevel.Info, "Exporting mapset to zip archive. Please wait!");

                MapManager.Selected.Value.Mapset.ExportToZip();
                IsExportingMapset = false;

                NotificationManager.Show(NotificationLevel.Success,
                    $"Successfully exported {MapManager.Selected.Value.Mapset.Artist} - {MapManager.Selected.Value.Mapset.Title}!");
            });
        }

        /// <summary>
        /// </summary>
        private void SetRichPresence()
        {
            DiscordHelper.Presence.Details = "Selecting a song";
            DiscordHelper.Presence.State = "In the menus";
            DiscordHelper.Presence.PartySize = 0;
            DiscordHelper.Presence.PartyMax = 0;
            DiscordHelper.Presence.EndTimestamp = 0;
            DiscordRpc.UpdatePresence(ref DiscordHelper.Presence);
        }

        /// <summary>
        ///     When a mapset has been deleted, refilter the mapsets
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnMapsetDeleted(object sender, MapsetDeletedEventArgs e)
        {
            ThreadScheduler.Run(() =>
            {
                var index = 0;

                if (e.Index == -1)
                    index = 0;

                if (e.Index - 1 >= 0)
                    index = e.Index - 1;
;
                lock (AvailableMapsets.Value)
                    AvailableMapsets.Value = MapsetHelper.FilterMapsets(CurrentSearchQuery);

                // Change the map
                if (index != -1)
                {
                    MapManager.Selected.Value = AvailableMapsets.Value[index].Maps.First();
                    return;
                }

                // Stop the current track if there are no more mapsets left
                lock (AudioEngine.Track)
                {
                    if (AudioEngine.Track.IsDisposed && !AudioEngine.Track.IsStopped)
                        AudioEngine.Track.Dispose();
                }
            });
        }

        /// <summary>
        ///     Called when a map has been deleted
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnMapDeleted(object sender, MapDeletedEventArgs e)
        {
            ThreadScheduler.Run(() =>
            {
                lock (AvailableMapsets.Value)
                    AvailableMapsets.Value = MapsetHelper.FilterMapsets(CurrentSearchQuery);

                var mapsetIndex = AvailableMapsets.Value.FindIndex(x => x.Maps.Contains(e.Map));

                if (mapsetIndex == -1 && AvailableMapsets.Value.Count != 0)
                {
                    MapManager.Selected.Value = AvailableMapsets.Value.First().Maps.First();
                    return;
                }

                // Stop the current track if there are no more mapsets left
                lock (AudioEngine.Track)
                {
                    if (AudioEngine.Track.IsDisposed && !AudioEngine.Track.IsStopped)
                        AudioEngine.Track.Dispose();
                }
            });
        }

        /// <summary>
        ///     Starts importing maps as soon as they're available to be imported
        /// </summary>
        private void ImportMaps()
        {
            // Go to the import screen if we've imported a map not on the select screen
            if (!Exiting && MapsetImporter.Queue.Count > 0 || QuaverSettingsDatabaseCache.OutdatedMaps.Count != 0
                                                           || MapDatabaseCache.MapsToUpdate.Count != 0)
            {
                Exit(() => new ImportingScreen(MultiplayerScreen, true));
                return;
            }
        }

        /// <summary>
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnMapUpdated(object sender, MapUpdatedEventArgs e) => AvailableMapsets.Value = MapsetHelper.FilterMapsets(CurrentSearchQuery);

        /// <summary>
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <exception cref="NotImplementedException"></exception>
        private void OnAutoLoadOsuBeatmapsChanged(object sender, BindableValueChangedEventArgs<bool> e)
            => Exit(() => new ImportingScreen(MultiplayerScreen, true));

        /// <inheritdoc />
        /// <summary>
        /// </summary>
        /// <returns></returns>
        public override UserClientStatus GetClientStatus() => new UserClientStatus(ClientStatus.Selecting, -1, "", 0, "", 0);
    }
}