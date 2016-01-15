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
        bool _useTubes = true;
        public MainPage()
        {
            this.InitializeComponent();
#if DEBUG
            if (System.Diagnostics.Debugger.IsAttached)
            {
                _honorQuietTime = false;
                _checkInterval = TimeSpan.FromSeconds(5);
                _announceInterval = TimeSpan.FromMinutes(15);
                _useTubes = false;
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
                if (false)
                {
                    Log("Testing!!!");
                    Test();
                    return;
                }
                var timer = new DispatcherTimer();
                timer.Interval = _checkInterval;
                timer.Tick += AlarmTimer_Tick;
                timer.Start();
            }
            catch (Exception exc)
            {
                Error(exc.Message);
            }
        }
        DateTimeOffset? _dtOverride;
        bool _reallyTalk = true;
        private async void Test()
        {
            _reallyTalk = false;
            var start = DateTimeOffset.Parse("6:00");
            var end = DateTimeOffset.Parse("23:00");
            for (var dt = start; dt < end; dt += _checkInterval)
            {
                _dtOverride = dt;
                await ShouldISaySomething();
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
                Error(exc.Message);
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
                {
                    await SayThis(FormatTimeForSpeech(now));
                    await SayTemp();
                }
                _lastBlock = block;
            }
            else
            {
                var it = _its.FirstOrDefault(i => i.Trigger(now));
                if (it != null)
                {
                    await SayThis(FormatTimeForSpeech(it.Dt) + " " + it.Title);
                    it.Said = true;
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
            LogSaid("SAY: " + str);
            if (QuietTime(GetTime()))
                return; 
            if (str == _lastSayThis)
            {
                Log("SayThis duplicate");
                if (!force)
                    return;
            }
            await WaitOnMedia();
            var stream = await _speaker.SynthesizeTextToStreamAsync(str);
            media.SetSource(stream, stream.ContentType);
            if (_reallyTalk)
                media.Play();

            _lastSayThis = str;
        }

        private async Task WaitOnMedia()
        {
            var waitCount = 0;
            while (media.CurrentState == MediaElementState.Opening ||
                media.CurrentState == MediaElementState.Playing)
            {
                Log("WaitOnMedia " + media.CurrentState + ", " + media.Source + ", " + ++waitCount);
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
            if (waitCount > 0)
                Log("WaitOnMedia Done " + media.CurrentState + ", " + media.Source + ", " + waitCount);
        }

        List<string> _chimes = new List<string>() { "stmichael.mp3", "westminster.mp3", "whittington.mp3", "avemaria.mp3", "beethoven_9thsym.mp3" };
        Random _rnd = new Random();
        void PlayBongs(DateTimeOffset now)
        {
            var hour = now.Hour % 12;
            if (hour == 0)
                hour = 12;
            HourlyChime(hour);
            //var i = _rnd.Next(_chimes.Count());
            //var sound = @"ms-appx:///Assets/" + _chimes[i];
        }

        private async void HourlyChime(int hour)
        {
            var block = _rnd.Next(5);
            if (block == 0)
                await Chime("b", hour);
            else if (block == 1)
                await LoopSound("w", hour);
            else if (block == 2)
                await LoopSound("wh", hour);
            else if (block == 3)
                await LoopSound("a", hour);
            else 
                await LoopSound("st", hour);
        }

        void Log(string str)
        {
            lst.Items.Insert(0, DateTimeOffset.Now.ToString("HH:mm:ss") + " " + str);
        }

        void Error(string str)
        {
            Log("ERROR " + str);
        }

        void LogSaid(string str)
        {
            lstSaid.Items.Insert(0, DateTimeOffset.Now.ToString("HH:mm:ss") + " " + str);
        }

        TimeSpan _offset = new TimeSpan(0);
        private DateTimeOffset GetTime()
        {
            if (_dtOverride.HasValue)
                return _dtOverride.Value;
            return DateTimeOffset.Now + _offset;
        }

        private async Task<DateTimeOffset> GetNistTime()
        {
            //var url = @""http://time.gov/HTML5""; 
            var url = @"http://free.timeanddate.com/clock/i3a2durd/n419/fn8/fs16/bas2/bacfff/pa8/tt0/tw1/tm1/th1/ta1/tb4";
            var str = await GetStringFromTubes(url);
            if (str == null)
                return DateTimeOffset.Now;
            var start = str.IndexOf("<span id=t1>") + "<span id=t1>".Length;
            var end = str.IndexOf("</span>", start);
            var content = str.Substring(start, end - start);
            content = content.Replace("<br>", " ");
            return DateTimeOffset.Parse(content);
        }

        private async Task<string> GetStringFromTubes(string url)
        {
            if (_useTubes == false)
                return null;
            try
            {
                HttpClient httpClient = new HttpClient();
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
                HttpResponseMessage httpResponseMessage = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                string text = await httpResponseMessage.Content.ReadAsStringAsync();
                if (httpResponseMessage.StatusCode == HttpStatusCode.OK)
                    return await httpResponseMessage.Content.ReadAsStringAsync();
            }
            catch (Exception exc)
            {
                Error(exc.Message);
            }

            return null;
        }
        public class Interesting
        {
            public Interesting(string title, DateTimeOffset dt, DayOfWeek[] days)
            {
                Title = title;
                Dt = dt;
                Days = days;
                Said = false;
            }
            public override string ToString()
            {
                return Title + " " + Dt + " " + Days;
            }

            public string Title { get; set; }
            public DateTimeOffset Dt { get; set; }
            public bool Said { get; internal set; }
            public DayOfWeek[] Days { get; set; }

            internal bool Trigger(DateTimeOffset now)
            {
                if (Said)
                    return false;
                if (Days != null)
                {
                    if (!Days.Contains(now.DayOfWeek))
                        return false;
                }
                var age = (now.TimeOfDay - Dt.TimeOfDay).TotalSeconds;
                return (age >= 0 && age < TimeSpan.FromMinutes(5).TotalSeconds);
            }
        }
        List<Interesting> _its = new List<Interesting>();
        private async void btnNow_Click(object sender, RoutedEventArgs rea)
        {
            try
            {
                await LoopSound("a", 3);
                return;
                await Chime("b", 3);

                _lastOffsetSet = DateTimeOffset.MinValue;
                await CheckOffset();
                await SayTemp();
                await UpdateInterestingTimes();
                foreach (var it in _its)
                    Log(it.ToString());
                await Chime("b", GetTime().Hour);
            }
            catch (Exception exc)
            {
                Error(exc.Message);
            }
        }

        private async Task SayTemp()
        {
            var jsonString = await GetStringFromTubes("http://api.wunderground.com/api/4659f100363b14f9/conditions/q/MD/monkton.json");
            var json = JsonValue.Parse(jsonString);
            var currentJson = json.GetObject().GetNamedObject("current_observation");
            var temp = currentJson.GetObject().GetNamedNumber("temp_f");
            await SayThis("Current temperature " + temp + " degrees", true);
        }
        private async Task Chime(string stub, int hour)
        {
            LogSaid("CHIME: " + stub + "," + hour);
            if (QuietTime(GetTime()))
                return;

            var baseFile = stub + "_base.mp3";
            QueueMedia(new Uri(@"ms-appx:///Assets/Chimes/" + baseFile));

            var chimeFile = stub + "_bongs_" + hour.ToString("00") + ".mp3";
            QueueMedia(new Uri(@"ms-appx:///Assets/Chimes/" + chimeFile));
            await PlayNextSound();
        }
        Queue<Uri> _soundsToPlay = new Queue<Uri>();
        async Task PlayNextSound()
        {
            await WaitOnMedia();
            if (_soundsToPlay.Count() > 0)
            {
                media.Stop();
                ShowQueue();
                media.Source = _soundsToPlay.Dequeue();
                if (_reallyTalk)
                    media.Play();
            }
        }
        void ShowQueue()
        {
            lock(_soundsToPlay)
            {
                lstQueue.Items.Clear();
                foreach (var s in _soundsToPlay)
                    lstQueue.Items.Add(s);
            }
        }
        private async void Media_MediaEnded(object sender, RoutedEventArgs e)
        {
            ShowQueue();
            await PlayNextSound();
        }

        private void QueueMedia(Uri uri)
        {
            _soundsToPlay.Enqueue(uri);
            ShowQueue();
        }

        private async Task LoopSound(string stub, int count)
        {
            LogSaid("LOOP: " + stub + "," + count);
            if (QuietTime(GetTime()))
                return;

            QueueMedia(new Uri(@"ms-appx:///Assets/Chimes/" + stub + "_base.mp3"));
            if (count > 1)
            {
                for (int i = 0; i < count - 1; i++)
                    QueueMedia(new Uri(@"ms-appx:///Assets/Chimes/" + stub + "_mid_bong.mp3"));
            }
            QueueMedia(new Uri(@"ms-appx:///Assets/Chimes/" + stub + "_last_bong.mp3"));
            await PlayNextSound();
        }

        private async Task UpdateInterestingTimes()
        {
            Log("UpdateInterestingTimes");
            await UpdateSunriseSunset();

            UpdateInteresting("Dad Birthday Minute", DateTimeOffset.Parse("11:16"));
            UpdateInteresting("Mom Birthday Minute", DateTimeOffset.Parse("15:19"));
            UpdateInteresting("Twin Birthday Minute", DateTimeOffset.Parse("14:17"));
            UpdateInteresting("Allegra Birthday Minute", DateTimeOffset.Parse("12:24"));
            UpdateInteresting("Ramsey Birthday Minute", DateTimeOffset.Parse("12:25"));
            UpdateInteresting("Max Birthday Minute", DateTimeOffset.Parse("12:10"));
            UpdateInteresting("Max Birthday Minute", DateTimeOffset.Parse("9:31"));
            UpdateInteresting("School Bus", DateTimeOffset.Parse("7:50"), _weekdays);
            UpdateInteresting("5 minutes", DateTimeOffset.Parse("7:45"), _weekdays);
        }
        DayOfWeek[] _weekdays = new DayOfWeek[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
        private async Task UpdateSunriseSunset()
        {
            var sunriseUrl = @"http://api.sunrise-sunset.org/json?lat=39.593887&lng=-76.596008&date=today";
            var str = await GetStringFromTubes(sunriseUrl);
            if (str == null)
                return;
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

        private void UpdateInteresting(string title, DateTimeOffset dt, DayOfWeek[] days = null)
        {
            var it = _its.SingleOrDefault(i => i.Title == title);
            if (it != null)
            {
                it.Dt = dt;
                it.Said = false;
                it.Days = days;
            }
            else
                _its.Add(new Interesting(title, dt, days));
        }

        DateTimeOffset _lastOffsetSet = DateTimeOffset.MinValue;
        private Uri _playNext;

        private async Task CheckOffset()
        {
            if (Math.Abs((_lastOffsetSet - DateTimeOffset.Now).TotalHours) < 1)
                return;
            Log("Local " + DateTimeOffset.Now.ToString());
            var nistTime = await GetNistTime();
            Log("NIST " + nistTime);
            _offset = nistTime - DateTimeOffset.Now;
            Log("Set Offset " + _offset);
            _lastOffsetSet = DateTimeOffset.Now;
            Log("Offsetted " + GetTime());
        }
    }
}
