using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Biscord
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        Dictionary<string, Channel[]> guildIdChannelPairs = new Dictionary<string, Channel[]>();
        Dictionary<string, Message[]> channelIdMessagePairs = new Dictionary<string, Message[]>();
        Dictionary<string, Channel> guildIdSelectedChannelPairs = new Dictionary<string, Channel>();
        Guild? selectedGuild;
        Guild[] guilds;
        string? token = null;

        ClientWebSocket ws = new();
        ClientWebSocket voice_ws = new();

        string sessionId;
        string voiceGuildId;
        User Client;

        public MainWindow()
        {
            Loaded += MainWindow_Loaded;
            InitializeComponent();
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(this);
            window.KeyDown += Window_KeyDown;

        Launch:
            while (token == null)
            {
                var dialogue = new TokenInputDialogue();
                bool? result = dialogue.ShowDialog();

                // UNCOMMENT THIS WHEN ACTUALLY RELEASING
                if (result == true) token = TokenInputDialogue.token;
            }

            string url = "wss://gateway.discord.gg/?v=6&encoding=json";

            int interval = 0;

            await ws.ConnectAsync(new Uri(url), CancellationToken.None);

            var payload = new
            {
                op = 2,
                intents = 512,
                d = new
                {
                    token = token,
                    properties = new
                    {
                        os = "OSdoge",
                        browser = "Chrome",
                        device = "AmongOS"
                    }
                }
            };

            switch (ws.State)
            {
                case WebSocketState.Open:
                    await ws.SendAsync(new ArraySegment<byte>(Encoding.ASCII.GetBytes(JsonSerializer.Serialize(payload))), WebSocketMessageType.Text, true, CancellationToken.None);
                    break;
            }

            ArraySegment<byte> buffer = new(new byte[16777216]);

            // Sends and receives hello event
            _ = await ws.ReceiveAsync(buffer, CancellationToken.None);
            int i = buffer.Array.Length - 1;
            while (buffer.Array[i] == 0)
                --i;
            var buffer_ = new byte[i + 1];
            Array.Copy(buffer.Array, buffer_, i + 1);
            buffer = new(new byte[16777216]);

            Hello resp = JsonSerializer.Deserialize<Hello>(Encoding.UTF8.GetString(buffer_));
            int op = resp.op;

            switch (op)
            {
                case 10:
                    int heartbeatInterval = (int)resp.d.heartbeat_interval;

                    _ = Task.Run(() => Heartbeat(heartbeatInterval));

                    HttpClient client = new();
                    HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "https://discord.com/api/v9/users/@me/guilds");
                    request.Headers.Add("authorization", token);
                    var response = await client.SendAsync(request);
                    string status = response.StatusCode.ToString();

                    if (status == "Unauthorized")
                    {
                        MessageBox.Show("Error: Unauthorized; Ensure that you have entered the correct token");
                        token = null;

                        goto Launch;
                    }

                    guilds = JsonSerializer.Deserialize<Guild[]>(await response.Content.ReadAsStringAsync());
                    selectedGuild = guilds[0];

                    client = new();
                    request = new HttpRequestMessage(HttpMethod.Get, "https://discord.com/api/v9/users/@me");
                    request.Headers.Add("authorization", token);
                    response = await client.SendAsync(request);
                    Client = JsonSerializer.Deserialize<User>(await response.Content.ReadAsStringAsync());

                    foreach (Guild guild in guilds)
                    {
                        request = new HttpRequestMessage(HttpMethod.Get, $"https://discord.com/api/v9/guilds/{guild.id}/channels");
                        request.Headers.Add("authorization", token);

                        response = await client.SendAsync(request);
                        Channel[]? channels = JsonSerializer.Deserialize<Channel[]>(await response.Content.ReadAsStringAsync());
                        guildIdChannelPairs.Add(guild.id, channels);

                        Channel? mainChannel = Array.Find(channels, channel => channel.type is 0 or 5);
                        guildIdSelectedChannelPairs.Add(guild.id, mainChannel);

                        foreach (Channel channel in channels)
                        {
                            if (guild == selectedGuild)
                            {
                                ChannelsPanel.Children.Add(CreateChannelBlock(channel));
                            }
                        }

                        Button button = new();
                        button.Content = guild.name;

                        BitmapImage bitmap = new();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri($"https://cdn.discordapp.com/icons/{guild.id}/{guild.icon}.png");
                        bitmap.DecodePixelWidth = 2048;
                        bitmap.EndInit();

                        ImageBrush brush = new();
                        brush.ImageSource = bitmap;

                        Ellipse ellipse = new();
                        ellipse.Width = 50;
                        ellipse.Height = 50;
                        ellipse.Fill = brush;
                        ellipse.Stretch = Stretch.UniformToFill;
                        ellipse.Margin = new Thickness(5);
                        ellipse.Tag = guild.id;
                        ellipse.MouseDown += GuildIcon_MouseDown;

                        GuildPanel.Children.Add(ellipse);
                    }

                    // Get user icon information
                    client = new();
                    request = new HttpRequestMessage(HttpMethod.Get, "https://discord.com/api/v9/users/@me");
                    request.Headers.Add("authorization", token);
                    response = await client.SendAsync(request);
                    User? user = JsonSerializer.Deserialize<User>(await response.Content.ReadAsStringAsync());

                    BitmapImage iconBitmap = new();
                    iconBitmap.BeginInit();
                    iconBitmap.UriSource = new Uri($"https://cdn.discordapp.com/avatars/{user.id}/{user.avatar}.png");
                    iconBitmap.DecodePixelWidth = 2048;
                    iconBitmap.EndInit();

                    ImageBrush iconBrush = new();
                    iconBrush.ImageSource = iconBitmap;

                    UserIcon.Fill = iconBrush;

                    // Brings in channel messages from default channel
                    Channel selectedChannel = guildIdSelectedChannelPairs[selectedGuild.id];
                    ChannelName.Text = "# " + selectedChannel.name;
                    client = new();
                    request = new HttpRequestMessage(HttpMethod.Get, $"https://discord.com/api/v9/channels/{selectedChannel.id}/messages?limit=100");
                    request.Headers.Add("authorization", token);

                    response = await client.SendAsync(request);
                    Message[] messages = JsonSerializer.Deserialize<Message[]>(await response.Content.ReadAsStringAsync());

                    for (int j = messages.Length - 1; j >= 0; j--)
                    {
                        MessagePanel.Children.Add(CreateMessageBlock(messages[j]));
                        if (messages[j].attachments.Length > 0)
                        {
                            for (int k = 0; k < messages[j].attachments.Length; k++)
                            {
                                if (messages[j].attachments[k].height != null)
                                    MessagePanel.Children.Add(CreateImage(messages[j], k));
                            }
                        }
                    }
                    channelIdMessagePairs.Add(selectedChannel.id, messages);
                    MessageScrollViewer.ScrollToEnd();

                    // Loads in the members

                    break;
            }

            // Starts receiving events from discord
            while (true)
            {
                _ = await ws.ReceiveAsync(buffer, CancellationToken.None);

                i = buffer.Array.Length - 1;
                while (buffer.Array[i] == 0)
                    --i;
                buffer_ = new byte[i + 1];
                Array.Copy(buffer.Array, buffer_, i + 1);
                buffer = new(new byte[16777216]);

                Debug.WriteLine(Encoding.Default.GetString(buffer_));
                Event @event = JsonSerializer.Deserialize<Event>(Encoding.UTF8.GetString(buffer_));

                switch (@event.op)
                {
                    case 0:
                        switch (@event.t)
                        {
                            case "READY":
                                Ready ready = JsonSerializer.Deserialize<Ready>(Encoding.UTF8.GetString(buffer_));
                                sessionId = ready.d.session_id;
                                break;
                            case "MESSAGE_CREATE":
                                MessageEvent messageEvent = JsonSerializer.Deserialize<MessageEvent>(Encoding.UTF8.GetString(buffer_));
                                Message message = messageEvent.d;

                                if (message.channel_id == guildIdSelectedChannelPairs[selectedGuild.id].id)
                                {
                                    MessagePanel.Children.Add(CreateMessageBlock(message));
                                    List<Message> messageList = channelIdMessagePairs[message.channel_id].ToList();
                                    messageList.Insert(0, message);
                                    channelIdMessagePairs[message.channel_id] = messageList.ToArray();

                                    MessageScrollViewer.ScrollToEnd();
                                }

                                break;
                            case "SESSIONS_REPLACE":
                                SessionsReplace session = JsonSerializer.Deserialize<SessionsReplace>(Encoding.UTF8.GetString(buffer_));
                                sessionId = session.d[0].session_id;
                                Debug.WriteLine(sessionId);
                                break;
                            case "VOICE_STATE_UPDATE":

                                break;
                            case "VOICE_SERVER_UPDATE":
                                VoiceServerUpdates voiceServer = JsonSerializer.Deserialize<VoiceServerUpdates>(Encoding.UTF8.GetString(buffer_));
                                await voice_ws.ConnectAsync(new Uri("wss://" + voiceServer.d.endpoint), CancellationToken.None);
                                var payload_ = new
                                {
                                    op = 0,
                                    d = new
                                    {
                                        server_id = voiceServer.d.guild_id,
                                        user_id = Client.id,
                                        session_id = sessionId,
                                        token = voiceServer.d.token
                                    }
                                };

                                await voice_ws.SendAsync(new ArraySegment<byte>(Encoding.ASCII.GetBytes(JsonSerializer.Serialize(payload_))), WebSocketMessageType.Text, true, CancellationToken.None);

                                ArraySegment<byte> voiceBuffer = new(new byte[16777216]);
                                _ = await voice_ws.ReceiveAsync(voiceBuffer, CancellationToken.None);
                                int j = voiceBuffer.Array.Length - 1;
                                while (voiceBuffer.Array[j] == 0)
                                    --j;
                                byte[] voiceBuffer_ = new byte[j + 1];
                                Array.Copy(voiceBuffer.Array, voiceBuffer_, j + 1);

                                VoiceHello? hello = JsonSerializer.Deserialize<VoiceHello>(Encoding.UTF8.GetString(voiceBuffer_));
                                _ = Task.Run(() => VoiceHeartbeat(hello.d.heartbeat_interval));

                                while (true)
                                {
                                    voiceBuffer = new(new byte[16777216]);
                                    _ = await voice_ws.ReceiveAsync(voiceBuffer, CancellationToken.None);
                                    j = voiceBuffer.Array.Length - 1;
                                    while (voiceBuffer.Array[j] == 0)
                                        --j;
                                    voiceBuffer_ = new byte[j + 1];
                                    Array.Copy(voiceBuffer.Array, voiceBuffer_, j + 1);
                                    voiceBuffer = new(new byte[16777216]);

                                    Debug.WriteLine(Encoding.UTF8.GetString(voiceBuffer_));

                                    VoiceEvent? voiceEvent = JsonSerializer.Deserialize<VoiceEvent>(Encoding.UTF8.GetString(voiceBuffer_));
                                    switch (voiceEvent.op)
                                    {
                                        case 2:
                                            VoiceEventReady? voiceReady = JsonSerializer.Deserialize<VoiceEventReady>(Encoding.UTF8.GetString(voiceBuffer_));
                                            var protocolPayload = new
                                            {
                                                op = 1,
                                                d = new
                                                {
                                                    protocol = "udp",
                                                    data = new
                                                    {
                                                        address = voiceReady.d.ip,
                                                        port = voiceReady.d.port,
                                                        mode = "xsalsa20_poly1305_lite"
                                                    }
                                                }
                                            };

                                            await voice_ws.SendAsync(new ArraySegment<byte>(Encoding.ASCII.GetBytes(JsonSerializer.Serialize(protocolPayload))), WebSocketMessageType.Text, true, CancellationToken.None);

                                            break;
                                        case 4:
                                            SessionDescription? description = JsonSerializer.Deserialize<SessionDescription>(Encoding.UTF8.GetString(voiceBuffer_));
                                            int[] encryptionKey = description.d.secret_key;
                                            List<NAudio.Wave.WaveInCapabilities> sources = new List<NAudio.Wave.WaveInCapabilities>();
                                            for (int k = 0; k < NAudio.Wave.WaveIn.DeviceCount; k++)
                                            {
                                                sources.Add(NAudio.Wave.WaveIn.GetCapabilities(k));
                                            }



                                            break;
                                    }
                                }

                                async void VoiceHeartbeat(int ms)
                                {
                                    var timer = new PeriodicTimer(TimeSpan.FromSeconds(ms));
                                    while (await timer.WaitForNextTickAsync())
                                    {
                                        var heartbeatPayload = new
                                        {
                                            op = 3,
                                            d = (long)(new DateTime(2015, 1, 1) - new DateTime(1970, 1, 1)).TotalMilliseconds
                                        };

                                        await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(heartbeatPayload))), WebSocketMessageType.Text, true, CancellationToken.None);
                                        Debug.WriteLine("Voice Heartbeat sent!");
                                    }
                                }
                                break;
                        }
                        break;
                    case 11:
                        Debug.WriteLine("Heartbeat Acknowledged!");
                        break;
                }
            }

            async void Heartbeat(int ms)
            {
                var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(ms));
                while (await timer.WaitForNextTickAsync())
                {
                    var heartbeatPayload = new
                    {
                        op = 1,
                        d = (string)null
                    };

                    await ws.SendAsync(new ArraySegment<byte>(Encoding.ASCII.GetBytes(JsonSerializer.Serialize(heartbeatPayload))), WebSocketMessageType.Text, true, CancellationToken.None);
                    Debug.WriteLine("Heartbeat sent!");
                }
            }
        }

        private async void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && MessageInputBox.IsKeyboardFocused)
            {
                string input = MessageInputBox.Text;
                if (input != null)
                {
                    var payload = new
                    {
                        content = input
                    };

                    Channel messageChannel = guildIdSelectedChannelPairs[selectedGuild.id];
                    HttpClient client = new();
                    HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, $"https://discord.com/api/v9/channels/{messageChannel.id}/messages");
                    request.Headers.Add("Authorization", token);
                    request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                    var response = await client.SendAsync(request);

                    MessageInputBox.Clear();
                }
            }
        }

        private async void ChannelBlock_MouseDown(object sender, MouseButtonEventArgs e)
        {
            TextBlock? channelTextBlock = sender as TextBlock;
            Channel? channel = Array.Find(guildIdChannelPairs[selectedGuild.id], channel => channel.id == channelTextBlock.Tag);
            if (channel.type is 0 or 5)
            {
                if (channelIdMessagePairs.ContainsKey(channelTextBlock.Tag.ToString()))
                {
                    ChannelName.Text = "# " + channel.name;

                    Message[] messages = channelIdMessagePairs[channelTextBlock.Tag.ToString()];
                    MessagePanel.Children.Clear();
                    for (int j = messages.Length - 1; j >= 0; j--)
                    {
                        MessagePanel.Children.Add(CreateMessageBlock(messages[j]));
                        if (messages[j].attachments.Length > 0)
                        {
                            for (int k = 0; k < messages[j].attachments.Length; k++)
                            {
                                if (messages[j].attachments[k].height != null)
                                    MessagePanel.Children.Add(CreateImage(messages[j], k));
                            }
                        }
                    }
                    MessageScrollViewer.ScrollToEnd();
                }
                else
                {
                    if (channel != guildIdSelectedChannelPairs[selectedGuild.id])
                    {
                        ChannelName.Text = "# " + channel.name;
                        guildIdSelectedChannelPairs[selectedGuild.id] = channel;
                        HttpClient client = new();
                        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, $"https://discord.com/api/v9/channels/{channel.id}/messages?limit=100");
                        request.Headers.Add("authorization", token);

                        var response = await client.SendAsync(request);
                        try
                        {
                            Message[] messages = JsonSerializer.Deserialize<Message[]>(await response.Content.ReadAsStringAsync());

                            MessagePanel.Children.Clear();
                            for (int j = messages.Length - 1; j >= 0; j--)
                            {
                                MessagePanel.Children.Add(CreateMessageBlock(messages[j]));
                                if (messages[j].attachments.Length > 0)
                                {
                                    for (int k = 0; k < messages[j].attachments.Length; k++)
                                    {
                                        if (messages[j].attachments[k].height != null)
                                            MessagePanel.Children.Add(CreateImage(messages[j], k));
                                    }
                                }
                            }
                            channelIdMessagePairs.Add(channel.id, messages);
                            MessageScrollViewer.ScrollToEnd();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(ex.ToString() + "\n" + "That may be because that's a channel you can't access. Whoopsie, I guess.");
                        }
                    }
                }
            }
            else if (channel.type is 2 or 13)
            {
                return;

                var payload = new
                {
                    op = 4,
                    d = new
                    {
                        guild_id = channel.guild_id,
                        channel_id = channel.id,
                        self_mute = false,
                        self_deaf = false
                    }
                };

                voiceGuildId = channel.guild_id;
                await ws.SendAsync(new ArraySegment<byte>(Encoding.ASCII.GetBytes(JsonSerializer.Serialize(payload))), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }

        private async void GuildIcon_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Ellipse? serverIconEllipse = sender as Ellipse;
            Guild newSelectedGuild = Array.Find(guilds, guild => guild.id == serverIconEllipse.Tag);
            if (newSelectedGuild != selectedGuild)
            {
                selectedGuild = newSelectedGuild;
                Channel[] channels = guildIdChannelPairs[selectedGuild.id];
                ChannelsPanel.Children.Clear();
                foreach (Channel channel in channels)
                {
                    ChannelsPanel.Children.Add(CreateChannelBlock(channel));
                }

                Channel selectedChannel = guildIdSelectedChannelPairs[selectedGuild.id];
                if (channelIdMessagePairs.ContainsKey(selectedChannel.id))
                {
                    Message[] messages = channelIdMessagePairs[selectedChannel.id];
                    MessagePanel.Children.Clear();
                    for (int j = messages.Length - 1; j >= 0; j--)
                    {
                        MessagePanel.Children.Add(CreateMessageBlock(messages[j]));
                        if (messages[j].attachments.Length > 0)
                        {
                            for (int k = 0; k < messages[j].attachments.Length; k++)
                            {
                                if (messages[j].attachments[k].height != null)
                                    MessagePanel.Children.Add(CreateImage(messages[j], k));
                            }
                        }
                    }
                    MessageScrollViewer.ScrollToEnd();
                }
                else
                {
                    if (selectedChannel.type is 0 or 5)
                    {
                        guildIdSelectedChannelPairs[selectedGuild.id] = selectedChannel;
                        HttpClient client = new();
                        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, $"https://discord.com/api/v9/channels/{selectedChannel.id}/messages?limit=100");
                        request.Headers.Add("authorization", token);

                        var response = await client.SendAsync(request);
                        Message[] messages = JsonSerializer.Deserialize<Message[]>(await response.Content.ReadAsStringAsync());
                        MessagePanel.Children.Clear();
                        for (int j = messages.Length - 1; j >= 0; j--)
                        {
                            MessagePanel.Children.Add(CreateMessageBlock(messages[j]));
                            if (messages[j].attachments.Length > 0)
                            {
                                for (int k = 0; k < messages[j].attachments.Length; k++)
                                {
                                    if (messages[j].attachments[k].height != null)
                                        MessagePanel.Children.Add(CreateImage(messages[j], k));
                                }
                            }
                        }
                        channelIdMessagePairs.Add(selectedChannel.id, messages);
                        MessageScrollViewer.ScrollToEnd();
                    }
                }
            }
        }

        private Image CreateImage(Message message, int attatchmentNum)
        {
            Attachment attachment = message.attachments[attatchmentNum];

            BitmapImage bitmap = new();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(attachment.url);
            bitmap.DecodePixelWidth = 2048;
            bitmap.EndInit();

            Image image = new Image();
            image.Source = bitmap;
            //if (attachment.width > 400) image.Width = 400;
            //if (attachment.height > 400) image.Height = 400;
            image.Height = 400;
            image.Width = 400;
            image.HorizontalAlignment = HorizontalAlignment.Left;
            image.Margin = new Thickness(5, 0, 0, 0);

            return image;
        }

        private TextBlock CreateMessageBlock(Message message)
        {
            TextBlock messageBlock = new TextBlock();
            messageBlock.Margin = new Thickness(5, 2.5, 0, 2.5);
            messageBlock.Text = message.author.username + "\n" + message.content;
            messageBlock.Foreground = Brushes.White;
            messageBlock.Tag = message.id;
            messageBlock.MouseDown += MessageBlock_MouseDown;

            return messageBlock;
        }

        private void MessageBlock_MouseDown(object sender, MouseButtonEventArgs e)
        {

        }

        private TextBlock CreateChannelBlock(Channel channel)
        {
            TextBlock channelBlock = new TextBlock();

            if (channel.type == 4) channelBlock.FontWeight = FontWeights.Bold;
            if (channel.type == 2) channelBlock.Text = "🔊 " + channel.name;
            else if (channel.type == 0) channelBlock.Text = "#️⃣ " + channel.name;
            else channelBlock.Text = channel.name;

            channelBlock.Padding = new Thickness(5);
            channelBlock.Foreground = Brushes.White;
            channelBlock.Tag = channel.id;
            channelBlock.MouseDown += ChannelBlock_MouseDown;

            return channelBlock;
        }

        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void ButtonMinimise_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.MainWindow.WindowState = WindowState.Minimized;
        }

        private void WindowStateButton_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow.WindowState != WindowState.Maximized)
            {
                Application.Current.MainWindow.WindowState = WindowState.Maximized;
            }
            else
            {
                Application.Current.MainWindow.WindowState = WindowState.Normal;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        public class Overwrite
        {
            string id { get; set; }
            int type { get; set; }
            string allow { get; set; }
            string deny { get; set; }
        }

        public class MessageEvent
        {
            public string? t { get; set; }
            public int? s { get; set; }
            public int op { get; set; }
            public Message? d { get; set; }
        }

        public class Message
        {
            public int type { get; set; }
            public bool tts { get; set; }
            public string timestamp { get; set; }
            public string channel_id { get; set; }
            public Message? referenced_message { get; set; }
            public bool pinned { get; set; }
            public string? nonce { get; set; }
            //public User[] mentions { get; set; }
            //public Role[] mention_roles { get; set; }
            public bool mention_everyone { get; set; }
            //public Member? member { get; set; }
            public string id { get; set; }
            public int? flags { get; set; }
            //public Embed[] embeds { get; set; }
            public string? edited_timestamp { get; set; }
            public string content { get; set; }
            //public Component[] components { get; set; }
            public User author { get; set; }
            public Attachment[] attachments { get; set; }
            public string? guild_id { get; set; }
        }

        public class Channel
        {
            public string id { get; set; }
            public int type { get; set; }
            public string? guild_id { get; set; }
            public int? position { get; set; }
            public string name { get; set; }
            public string? topic { get; set; }
            public bool? nsfw { get; set; }
            public string? last_message_id { get; set; }
            public int? bitrate { get; set; }
            public int? user_limit { get; set; }
            public int? rate_limit_per_user { get; set; }
            public User[]? recipients { get; set; }
            public string? icon { get; set; }
            public string? owner_id { get; set; }
            public string? application_id { get; set; }
            public string? parent_id { get; set; }
            public string? last_pin_timestamp { get; set; }
            public string? rtc_region { get; set; }
            public int? video_quality_mode { get; set; }
            public int? message_count { get; set; }
            public int? member_count { get; set; }
            public int? default_auto_archive_duration { get; set; }
            public string? permissions { get; set; }
        }

        public class Guild
        {
            public string id { get; set; }
            public string name { get; set; }
            public string icon { get; set; }
            public bool? owner { get; set; }
            public string? permissions { get; set; }
            public string[] features { get; set; }
        }

        public class Component
        {
            public int type { get; set; }
        }

        public class Embed
        {
            public string? title { get; set; }
        }

        public class Member
        {
            public string[] roles { get; set; }
        }

        public class Role
        {
            public string id { get; set; }
            public string name { get; set; }
            public int color { get; set; }
            public bool hoist { get; set; }
            public string? icon { get; set; }
            public string? unicode_emoji { get; set; }
            public int position { get; set; }
            public string permissions { get; set; }
            public bool managed { get; set; }
            public bool mentionable { get; set; }
        }

        public class Attachment
        {
            public string id { get; set; }
            public string filename { get; set; }
            public string? description { get; set; }
            public string? content_type { get; set; }
            public int size { get; set; }
            public string url { get; set; }
            public string proxy_url { get; set; }
            public int? height { get; set; }
            public int? width { get; set; }
            public bool? ephemeral { get; set; }
        }

        public class User
        {
            public string username { get; set; }
            public int? public_flags { get; set; }
            public string id { get; set; }
            public string discriminator { get; set; }
            public string? avatar { get; set; }
        }

        public class Event
        {
            public string t { get; set; }
            public int? s { get; set; }
            public int op { get; set; }
        }

        public class Hello
        {
            public string? t { get; set; }
            public int? s { get; set; }
            public int op { get; set; }
            public HelloData? d { get; set; }
        }

        public class HelloData
        {
            public int? heartbeat_interval { get; set; }
        }

        public class SessionsReplace
        {
            public string? t { get; set; }
            public int? s { get; set; }
            public int op { get; set; }
            public SessionsReplaceData[]? d { get; set; }
        }

        public class SessionsReplaceData
        {
            public string status { get; set; }
            public string session_id { get; set; }
        }

        public class VoiceServerUpdates
        {
            public string? t { get; set; }
            public int? s { get; set; }
            public int op { get; set; }
            public VoiceServerUpdatesData? d { get; set; }
        }

        public class VoiceServerUpdatesData
        {
            public string token { get; set; }
            public string guild_id { get; set; }
            public string endpoint { get; set; }
        }

        public class Ready
        {
            public string? t { get; set; }
            public int? s { get; set; }
            public int op { get; set; }
            public ReadyData d { get; set; }
        }

        public class ReadyData
        {
            public string session_id { get; set; }
        }

        public class VoiceHello
        {
            public int op { get; set; }
            public VoiceHelloData d { get; set; }
        }

        public class VoiceHelloData
        {
            public int v { get; set; }
            public int heartbeat_interval { get; set; }
        }

        public class VoiceEvent
        {
            public int op { get; set; }
        }

        public class VoiceEventReady
        {
            public int op { get; set; }
            public VoiceEventReadyData d { get; set; }
        }

        public class VoiceEventReadyData
        {
            public int ssrc { get; set; }
            public string ip { get; set; }
            public int port { get; set; }
            public string[] modes { get; set; }
            public int heartbeat_interval { get; set; }
        }

        public class SessionDescription
        {
            public int op { get; set; }
            public SessionDescriptionData d { get; set; }
        }

        public class SessionDescriptionData
        {
            public string video_codec { get; set; }
            public int[] secret_key { get; set; }
            public string? mode { get; set; }
            public string? media_session_id { get; set; }
            public string? audio_codec { get; set; }
        }
    }
}