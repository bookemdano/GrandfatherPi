using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Media.SpeechSynthesis;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace PiUi
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        TimeSpan _interval = TimeSpan.FromMinutes(15);
        TimeSpan _alarmCheckInterval = TimeSpan.FromSeconds(30);
        DispatcherTimer _bongTimer = new DispatcherTimer();
        public MainPage()
        {
            this.InitializeComponent();
            Log("Start");
            _bongTimer.Interval = TimeSpan.FromSeconds(5);
            _bongTimer.Tick += Timer_Tick;
            _bongTimer.Start();
            var timer = new DispatcherTimer();
            timer.Interval = _alarmCheckInterval;
            timer.Tick += AlarmTimer_Tick;
            timer.Start();
        }

        private async void AlarmTimer_Tick(object sender, object e)
        {
            await SetOffset();
            if (_its.Count > 0)
            {
                foreach(var it in _its)
                {
                    if (Math.Abs((it.Dt.TimeOfDay - GetTime().TimeOfDay).TotalSeconds) < _alarmCheckInterval.TotalSeconds)
                    {
                        await SayThis(FormatTimeForSpeech(it.Dt) + " " + it.Title);
                    }
                }
            }
        }

        async private void Timer_Tick(object sender, object e)
        {
            await SetOffset();
            await SayTime();
        }

        private async Task SayTime()
        {
            try
            {
                var now = GetTime();
                if (now.Hour >= 7 && now.Hour <= 21)
                    await SayThis(FormatTimeForSpeech(now));
                var min = (double) now.Minute + (now.Second / 60.0);
                var intervalMinutes = _interval.Minutes;
                while (min >= intervalMinutes)
                    min -= intervalMinutes;
                if (min > intervalMinutes - 1)
                    min = 0;
                _bongTimer.Interval = TimeSpan.FromMinutes(intervalMinutes - min);
                Log("Interval set to " + _bongTimer.Interval.TotalMinutes.ToString("0.00") + " minutes");
            }
            catch (Exception exc)
            {
                Log(exc.Message);
            }
        }

        private string FormatTimeForSpeech(DateTimeOffset dt)
        {
            return dt.ToString("t");
        }

        string _lastSayThis;
        private async Task SayThis(string str)
        {
            Log("SayThis " + str);
            if (str == _lastSayThis)
                return;
            var speaker = new SpeechSynthesizer();
            var stream = await speaker.SynthesizeTextToStreamAsync(str);
            var me = this.media;
            me.SetSource(stream, stream.ContentType);
            me.Play();
            _lastSayThis = str;
        }
        void Log(string str)
        {
            lst.Items.Insert(0, str);
        }


        TimeSpan _offset = new TimeSpan(0);
        private DateTimeOffset GetTime()
        {
            return DateTimeOffset.Now + _offset;
        }

        private async Task<DateTimeOffset> GetNistTime()
        {
            //var url = @""http://time.gov/HTML5""; 
            var url = @"http://free.timeanddate.com/clock/i3a2durd/n419/fn8/fs16/bas2/bacfff/pa8/tt0/tw1/tm1/th1/ta1/tb4";
            var str = await GetStringFromTubes(url);
            if (str == null)
                return DateTimeOffset.MinValue;
            var start = str.IndexOf("<span id=t1>") + "<span id=t1>".Length;
            var end = str.IndexOf("</span>", start);
            var content = str.Substring(start, end - start);
            content = content.Replace("<br>", " ");
            return DateTimeOffset.Parse(content);
        }

        private static async Task<string> GetStringFromTubes(string url)
        {
            HttpClient httpClient = new HttpClient();
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
            HttpResponseMessage httpResponseMessage = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            string text = await httpResponseMessage.Content.ReadAsStringAsync();
            if (httpResponseMessage.StatusCode == HttpStatusCode.OK)
                return await httpResponseMessage.Content.ReadAsStringAsync();
            else
                return null;
        }
        public class Interesting
        {
            public Interesting(string title, DateTimeOffset dt)
            {
                Title = title;
                Dt = dt;
            }

            public string Title { get; set; }
            public DateTimeOffset Dt { get; set; }
        }
        List<Interesting> _its = new List<Interesting>();
        private async void btnNow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AddInteresting("Surprise", GetTime().AddMinutes(.5));

                await SetOffset();
                await SayThis(FormatTimeForSpeech(GetTime()));
            }
            catch (Exception exc)
            {
                Log(exc.Message);
            }
        }

        private async Task GetInterestingTimes()
        {
            Log("GetInterestingTimes");
            await AddSunriseSunset();
            AddInteresting("Dad Birthday Minute", DateTimeOffset.Parse("11:16"));
            AddInteresting("Mom Birthday Minute", DateTimeOffset.Parse("15:19"));
            AddInteresting("Twin Birthday Minute", DateTimeOffset.Parse("14:17"));
            AddInteresting("Max Birthday Minute", DateTimeOffset.Parse("12:10"));
        }

        private async Task AddSunriseSunset()
        {
            var sunriseUrl = @"http://api.sunrise-sunset.org/json?lat=39.593887&lng=-76.596008&date=today";
            var str = await GetStringFromTubes(sunriseUrl);
            var json = JsonValue.Parse(str);
            var resultJson = json.GetObject().GetNamedObject("results");
            var sunrise = resultJson.GetObject().GetNamedString("sunrise");
            var dt = new DateTimeOffset(DateTime.Parse(sunrise).ToLocalTime());
            AddInteresting("Sunrise", dt);
            var sunset = resultJson.GetObject().GetNamedString("sunset");
            dt = new DateTimeOffset(DateTime.Parse(sunset).ToLocalTime());
            AddInteresting("Sunset", dt);
        }

        private void AddInteresting(string title, DateTimeOffset dt)
        {
            _its.Add(new Interesting(title, dt));
        }

        DateTimeOffset _lastOffset = DateTimeOffset.MinValue;
        private async Task SetOffset()
        {
            if (Math.Abs((_lastOffset - DateTimeOffset.Now).TotalHours) < 1)
                return;
            Log("Local " + DateTimeOffset.Now.ToString());
            var nistTime = await GetNistTime();
            Log("NIST " + nistTime);
            _offset = nistTime - DateTimeOffset.Now;
            Log("Set Offset " + _offset);
            _lastOffset = DateTimeOffset.Now;
            Log("Offsetted " + GetTime());
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            await GetInterestingTimes();
        }
    }

    class TimeUtils
    {
                async Task RequestTime()
        {
            DatagramSocket socket = new DatagramSocket();
            socket.MessageReceived += socket_MessageReceived;
            await socket.ConnectAsync(new HostName("time.windows.com"), "123");

            using (DataWriter writer = new DataWriter(socket.OutputStream))
            {
                byte[] container = new byte[48];
                container[0] = 0x1B;

                writer.WriteBytes(container);
                await writer.StoreAsync();
            }
        }

        void socket_MessageReceived(DatagramSocket sender, DatagramSocketMessageReceivedEventArgs args)
        {
            using (DataReader reader = args.GetDataReader())
            {
                byte[] b = new byte[48];

                reader.ReadBytes(b);

                var time = GetNetworkTime(b);
                //sta.Text = time.ToString();
            }
        }
        public static DateTimeOffset GetNetworkTime(byte[] rawData)
        {
            //Offset to get to the "Transmit Timestamp" field (time at which the reply 
            //departed the server for the client, in 64-bit timestamp format."
            const byte serverReplyTime = 40;

            //Get the seconds part
            ulong intPart = BitConverter.ToUInt32(rawData, serverReplyTime);

            //Get the seconds fraction
            ulong fractPart = BitConverter.ToUInt32(rawData, serverReplyTime + 4);

            //Convert From big-endian to little-endian
            intPart = SwapEndianness(intPart);
            fractPart = SwapEndianness(fractPart);

            var milliseconds = (intPart * 1000) + ((fractPart * 1000) / 0x100000000L);

            //**UTC** time
            var networkDateTime = (new DateTime(1900, 1, 1)).AddMilliseconds((long)milliseconds);
            var rv = DateTimeOffset.FromUnixTimeMilliseconds((long) milliseconds);
            return rv;
        }

        // stackoverflow.com/a/3294698/162671
        static uint SwapEndianness(ulong x)
        {
            return (uint)(((x & 0x000000ff) << 24) +
                           ((x & 0x0000ff00) << 8) +
                           ((x & 0x00ff0000) >> 8) +
                           ((x & 0xff000000) >> 24));
        }


    }
}
