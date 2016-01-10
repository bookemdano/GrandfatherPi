using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Media.SpeechSynthesis;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace PiUi
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        TimeSpan _announceInterval = TimeSpan.FromMinutes(30);
        TimeSpan _checkInterval = TimeSpan.FromSeconds(30);
        bool _honorQuietTime = true;
        public MainPage()
        {
            this.InitializeComponent();
#if DEBUG
            if (System.Diagnostics.Debugger.IsAttached)
            {
                _honorQuietTime = false;
                _checkInterval = TimeSpan.FromSeconds(5);
                _announceInterval = TimeSpan.FromMinutes(15);
            }
#endif
        }
        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Log("Start");
                await CheckOffset();
                await ShouldISaySomething();
                var timer = new DispatcherTimer();
                timer.Interval = _checkInterval;
                timer.Tick += AlarmTimer_Tick;
                timer.Start();
            }
            catch (Exception exc)
            {
                Log(exc.Message);
            }
        }

        private async void AlarmTimer_Tick(object sender, object e)
        {
            try
            {
                await ShouldISaySomething();
            }
            catch (Exception exc)
            {
                Log(exc.Message);
            }
        }

        int _lastBlock = int.MinValue;
        DateTimeOffset _lastDate = DateTimeOffset.MinValue;
        private async Task ShouldISaySomething()
        {
            await CheckOffset();
            var now = GetTime();
            if (_lastDate.Date != now.Date)
                await UpdateInterestingTimes();
            _lastDate = now;
            var block = (int)(now.Minute / _announceInterval.TotalMinutes);
            if (_lastBlock != block)
            {
                if (now.Minute < 1)
                    PlayBongs(now);
                else
                    await SayThis(FormatTimeForSpeech(now));
                _lastBlock = block;
            }
            else
            {
                foreach (var it in _its)
                {
                    if (it.Said)
                        continue;
                    var age = (now.TimeOfDay - it.Dt.TimeOfDay).TotalSeconds;
                    if (age >= 0 && age < _checkInterval.TotalSeconds * 5)
                    {
                        await SayThis(FormatTimeForSpeech(it.Dt) + " " + it.Title);
                        it.Said = true;
                        break;
                    }
                }
            }
        }

        bool  QuietTime(DateTimeOffset dt)
        {
            if (!_honorQuietTime)
                return false;
            int startDay = 7;
            int endDay = 22;
            if (dt.DayOfWeek == DayOfWeek.Sunday)
                startDay = 10;

            if (dt.Hour >= startDay && dt.Hour < endDay)
                return false;
            return true;     
        }
        
        private string FormatTimeForSpeech(DateTimeOffset dt)
        {
            return dt.ToString("t");
        }

        SpeechSynthesizer _speaker = new SpeechSynthesizer();
        string _lastSayThis;
        private async Task SayThis(string str, bool force = false)
        {
            Log("SayThis " + str + "," + media.CurrentState);
            if (QuietTime(GetTime()))
                return; 
            if (str == _lastSayThis)
            {
                Log("SayThis duplicate");
                if (!force)
                    return;
            }
            while (media.CurrentState == MediaElementState.Opening ||
                media.CurrentState == MediaElementState.Playing)
            {
                Log("SayThis busy");
                await Task.Delay(TimeSpan.FromSeconds(.1));
            }
            var stream = await _speaker.SynthesizeTextToStreamAsync(str);
            media.SetSource(stream, stream.ContentType);
            media.Play();

            _lastSayThis = str;
        }
        List<string> _chimes = new List<string>() { "stmichael.mp3", "westminster.mp3", "whittington.mp3", "avemaria.mp3", "beethoven_9thsym.mp3" };
        Random _rnd = new Random();
        void PlayBongs(DateTimeOffset now)
        {
            var hour = now.Hour % 12;
            if (hour == 0)
                hour = 12;
            LoopSound("w_base.mp3", "w_mid_bong.mp3", hour);
            //var i = _rnd.Next(_chimes.Count());
            //var sound = @"ms-appx:///Assets/" + _chimes[i];
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
                Said = false;
            }

            public string Title { get; set; }
            public DateTimeOffset Dt { get; set; }
            public bool Said { get; internal set; }
        }
        List<Interesting> _its = new List<Interesting>();
        private async void btnNow_Click(object sender, RoutedEventArgs rea)
        {
            try
            {
                PlayBongs(GetTime());
            }
            catch (Exception exc)
            {
                Log(exc.Message);
            }
        }
        
        private void LoopSound(string baseFile, string loopFile, int count)
        {
            if (QuietTime(GetTime()))
                return;
     
            var me = this.media;
            me.Source = new Uri(@"ms-appx:///Assets/" + baseFile);
            me.Play();
            int i = 0;
            me.MediaEnded += (s, e) =>
            {
                if (i++ < count)
                {
                    me.Source = new Uri(@"ms-appx:///Assets/" + loopFile);
                    me.Play();
                }
            };
        }

        private void Media_MediaEnded(object sender, RoutedEventArgs e)
        {
            throw new NotImplementedException();
        }

        private async Task UpdateInterestingTimes()
        {
            Log("GetInterestingTimes");
            await UpdateSunriseSunset();
            UpdateInteresting("Dad Birthday Minute", DateTimeOffset.Parse("11:16"));
            UpdateInteresting("Mom Birthday Minute", DateTimeOffset.Parse("15:19"));
            UpdateInteresting("Twin Birthday Minute", DateTimeOffset.Parse("14:17"));
            UpdateInteresting("Max Birthday Minute", DateTimeOffset.Parse("12:10"));
        }

        private async Task UpdateSunriseSunset()
        {
            var sunriseUrl = @"http://api.sunrise-sunset.org/json?lat=39.593887&lng=-76.596008&date=today";
            var str = await GetStringFromTubes(sunriseUrl);
            var json = JsonValue.Parse(str);
            var resultJson = json.GetObject().GetNamedObject("results");
            UpdateInteresting("Sunrise", GetDt(resultJson, "sunrise"));
            UpdateInteresting("Sunset", GetDt(resultJson, "sunset"));
            UpdateInteresting("Solar Noon", GetDt(resultJson, "solar_noon"));
        }

        private DateTimeOffset GetDt(JsonObject resultJson, string key)
        {
            var sunrise = resultJson.GetObject().GetNamedString(key);
            return new DateTimeOffset(DateTime.Parse(sunrise).ToLocalTime());
        }

        private void UpdateInteresting(string title, DateTimeOffset dt)
        {
            var it = _its.SingleOrDefault(i => i.Title == title);
            if (it != null)
            {
                it.Dt = dt;
                it.Said = false;
            }
            else
                _its.Add(new Interesting(title, dt));
        }

        DateTimeOffset _lastOffset = DateTimeOffset.MinValue;
        private async Task CheckOffset()
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

        private async void btnNow2_Click(object sender, RoutedEventArgs e)
        {
            await SayThis("Chicken Hat", true);
        }
    }
}
