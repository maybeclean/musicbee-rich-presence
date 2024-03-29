using System;
using System.Drawing;
using System.Windows.Forms;
using System.Net.Http;
using DiscordInterface;
using Utils;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.IO;

namespace MusicBeePlugin
{
    public partial class Plugin
    {
        private readonly PluginInfo _about = new PluginInfo();

        private static DiscordRPC.RichPresence _rpcPresence = new DiscordRPC.RichPresence();

        public static MusicBeeApiInterface MbApiInterface;

        public static IniParser IniParser = new IniParser("C:\\MusicBee-RichPresence\\configuration.ini");

        public static readonly Logger Logging = new Logger();

        public static readonly HttpClient HttpClient = new HttpClient();

        public static string DiscordId = "";

        public static int DiscordType = 0;

        public TextBox DiscordIdTextBox;
        public ComboBox DiscordTypeSelection;

        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            Utility.ReadConfig();
            string token = "";

            if (DiscordType != 0)
                token = DiscordToken.GetAuthToken();

            MbApiInterface = new MusicBeeApiInterface();
            MbApiInterface.Initialise(apiInterfacePtr);
            _about.PluginInfoVersion = PluginInfoVersion;
            _about.Name = "Discord Rich Presence";
            _about.Description = "A Richer Rich Presence for Musicbee.";
            _about.Author = "@maybeclean";
            _about.Type = PluginType.General;
            _about.VersionMajor = 1;
            _about.VersionMinor = 2;
            _about.Revision = 09;
            _about.ReceiveNotifications = (ReceiveNotificationFlags.PlayerEvents | ReceiveNotificationFlags.TagEvents);
            _about.ConfigurationPanelHeight = 65;

            if (!string.IsNullOrEmpty(token))
            {
                if (token == "FAIL")
                    MessageBox.Show("Failed to grab auth token");

                HttpClient.DefaultRequestHeaders.Clear();
                HttpClient.DefaultRequestHeaders.Add("Authorization", token);

                if (!string.IsNullOrEmpty(DiscordId))
                    InitialiseDiscord();
            }

            return _about;
        }

        public bool Configure(IntPtr panelHandle)
        {
            if (panelHandle != IntPtr.Zero)
            {
                Panel configPanel = (Panel) Control.FromHandle(panelHandle);
                Label prompt = new Label();
                prompt.AutoSize = true;
                prompt.Location = new Point(0, 0);
                prompt.Text = "Developer App Id";
                DiscordIdTextBox = new TextBox();
                DiscordIdTextBox.Bounds = new Rectangle(135, 0, 100, DiscordIdTextBox.Height);
                DiscordIdTextBox.Text = DiscordId;

                // ------ Discord Type ----- \\
                Label labelType = new Label();
                labelType.AutoSize = true;
                labelType.Location = new Point(0, 30);
                labelType.Text = "Discord Type";
                DiscordTypeSelection = new ComboBox();
                DiscordTypeSelection.Bounds = new Rectangle(135, 30, 100, DiscordTypeSelection.Height);
                DiscordTypeSelection.DropDownStyle = ComboBoxStyle.DropDownList;
                DiscordTypeSelection.DataSource = new ComboItem[]
                {
                    new ComboItem {Id = 1, Text = "Stable"},
                    new ComboItem {Id = 2, Text = "PTB"},
                    new ComboItem {Id = 3, Text = "Canary"}
                };
                DiscordTypeSelection.DisplayMember = "Text";
                DiscordTypeSelection.ValueMember = "ID";
                DiscordTypeSelection.SelectedValue = DiscordType;

                configPanel.Controls.AddRange(new Control[]
                    {prompt, DiscordIdTextBox, labelType, DiscordTypeSelection});
            }

            return false;
        }

        public void SaveSettings()
        {
            try
            {
                IniParser.Write("AppID", DiscordIdTextBox.Text, "Discord");
                IniParser.Write("DiscordType", DiscordTypeSelection.SelectedValue.ToString(), "Discord");
                MessageBox.Show("Musicbee will now restart to apply your changes", "Restart Required");
                Application.Restart();
                Environment.Exit(0);
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Exception Occured");
            }
        }

        private void InitialiseDiscord()
        {
            var handlers = new DiscordRPC.DiscordEventHandlers
            {
                readyCallback = HandleReadyCallback,
                errorCallback = HandleErrorCallback,
                disconnectedCallback = HandleDisconnectedCallback
            };
            DiscordRPC.Initialize(DiscordId, ref handlers, true, null);
        }

        private void HandleReadyCallback()
        {
        }

        private void HandleErrorCallback(int errorCode, string message)
        {
        }

        private void HandleDisconnectedCallback(int errorCode, string message)
        {
        }

        private async void ProcessArtwork(string album, string hash = "")
        {
            var assetList = await Discord.GetAssetList().ReadAsStringAsync();

            var artworkData = MbApiInterface.NowPlaying_GetArtwork();

            var dataUri = "data:image/png;base64," + artworkData;

            var albumAssured = Utility.AssureByteSize(album, 32); //Asset keys larger than 32 bytes will cause an exception.

            if (assetList.Contains(albumAssured))
            {
                _rpcPresence.largeImageKey = albumAssured;
                return;
            }
            else
            {
                _rpcPresence.largeImageKey = "temp_uploading";
                await Discord.UploadAsset(albumAssured, dataUri);
                _rpcPresence.largeImageKey = albumAssured;
            }
        }

        private void UpdatePresence(string artist, string trackArtist, string track, string album, string duration,
            bool playing, int position, int volume, bool handleArtworks = false)
        {
            track = Utility.Utf16ToUtf8(track + " ");
            artist = Utility.Utf16ToUtf8("by " + artist);
            trackArtist = Utility.Utf16ToUtf8("by " + trackArtist);

            _rpcPresence.state = Utility.AssureByteSize(trackArtist.Substring(0, trackArtist.Length - 1), 128);
            _rpcPresence.details = Utility.AssureByteSize(track.Substring(0, track.Length - 1), 128);
            
            string largeText = album + " " + artist;

            string genre = MbApiInterface.NowPlaying_GetFileTag(MetaDataType.Genre);

            _rpcPresence.largeImageText = largeText.Substring(0, largeText.Length - 1);

            if (!string.IsNullOrEmpty(genre))
                _rpcPresence.largeImageText += $" ({genre})";

            string cleanedAlbum = Utility.SanitizeAlbumName(Utility.ValidateEncoding(album));

            if (handleArtworks)
                ProcessArtwork(cleanedAlbum);

            long now = (long) (DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;

            if (playing)
            {
                _rpcPresence.startTimestamp = now - position;

                string[] durations = duration.Split(':');
                long end = Convert.ToInt64(durations[0]) * 60 + Convert.ToInt64(durations[1]);

                _rpcPresence.endTimestamp = _rpcPresence.startTimestamp + end;
                _rpcPresence.smallImageKey = "playing";
                _rpcPresence.smallImageText = "Playing at " + volume + "%";
            }
            else
            {
                _rpcPresence.endTimestamp = 0;
                _rpcPresence.startTimestamp = 0;
                _rpcPresence.smallImageKey = "paused";
                _rpcPresence.smallImageText = "Paused";
            }

            DiscordRPC.UpdatePresence(ref _rpcPresence);
        }

        public void Close(PluginCloseReason reason)
        {
            DiscordRPC.Shutdown();
        }

        public void ReceiveNotification(string sourceFileUrl, NotificationType type)
        {
            string artist = MbApiInterface.NowPlaying_GetFileTag(MetaDataType.AlbumArtist);
            string trackArtist = MbApiInterface.NowPlaying_GetFileTag(MetaDataType.Artist);
            string trackTitle = MbApiInterface.NowPlaying_GetFileTag(MetaDataType.TrackTitle);
            string album = MbApiInterface.NowPlaying_GetFileTag(MetaDataType.Album);
            string duration = MbApiInterface.NowPlaying_GetFileProperty(FilePropertyType.Duration);
            int volume = Convert.ToInt32(MbApiInterface.Player_GetVolume() * 100.0f);
            int position = MbApiInterface.Player_GetPosition() / 1000;

            if (string.IsNullOrEmpty(artist))
                artist = "[artist empty]";

            switch (type)
            {
                case NotificationType.PlayStateChanged:
                    switch (MbApiInterface.Player_GetPlayState())
                    {
                        case PlayState.Playing:
                            UpdatePresence(artist, trackArtist, trackTitle, album, duration, true, position, volume);
                            break;
                        case PlayState.Paused:
                            UpdatePresence(artist, trackArtist, trackTitle, album, duration, false, 0, volume);
                            break;
                    }

                    break;

                case NotificationType.TrackChanged:
                    UpdatePresence(artist, trackArtist, trackTitle, album, duration, true, 0, volume, true);
                    break;

                case NotificationType.VolumeLevelChanged:
                    if (MbApiInterface.Player_GetPlayState() == PlayState.Playing)
                        UpdatePresence(artist, trackArtist, trackTitle, album, duration, true, position, volume);
                    break;
            }
        }
    }
}