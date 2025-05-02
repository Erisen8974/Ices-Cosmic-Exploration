using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Common.Component.Excel;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Serilog.Core;

namespace ICE.Scheduler.Handlers
{
    internal class WeatherForecastHandler
    {
        private const double Seconds = 1;
        private const double Minutes = 60 * Seconds;
        private const double WeatherPeriod = 23 * Minutes + 20 * Seconds;

        private static ExcelSheet<Weather>? WeatherSheet;
        internal static List<WeatherForecast> weathers;

        private static DateTime _lastProcessed = DateTime.MinValue;
        private static TimeSpan _delay = TimeSpan.FromSeconds(300);

        internal static unsafe void Tick()
        {
            if (DateTime.Now - _lastProcessed < _delay) return;
            _lastProcessed = DateTime.Now;

            if (WeatherSheet == null) WeatherSheet = Svc.Data.GetExcelSheet<Weather>();

            GetForecast();
        }

        internal static unsafe (string, string, string) GetNextWeather()
        {
            WeatherManager* wm = WeatherManager.Instance();
            byte currWeatherId = wm->GetCurrentWeather();
            Weather currWeather = WeatherSheet.GetRow(currWeatherId);

            var currentWeather = weathers
                .Select((item, index) => new { item, index })
                .First(w => w.item.Name == currWeather.Name);
            var nextWeather = weathers
                .Skip(currentWeather.index + 1)
                .Select((item, index) => new { item, index })
                .First();

            return (currentWeather.item.Name, nextWeather.item.Name, FormatForecastTime(nextWeather.item.Time));
        }

        internal static unsafe void GetForecast()
        {
            WeatherManager* wm = WeatherManager.Instance();
            if (wm == null) return;
            byte currentWeatherId = wm->GetCurrentWeather();

            Weather currentWeather = WeatherSheet.GetRow(currentWeatherId);
            Weather lastWeather = currentWeather;

            weathers = [BuildResultObject(currentWeather, GetRootTime(0))];

            for (var i = 1; i <= 10; i++)
            {
                byte weatherId = wm->GetWeatherForDaytime(Svc.ClientState.TerritoryType, i);
                var weather = WeatherSheet.GetRow(weatherId)!;
                var time = GetRootTime(i * WeatherPeriod);

                if (lastWeather.RowId != weather.RowId)
                {
                    lastWeather = weather;
                    weathers.Add(BuildResultObject(weather, time));
                }
            }
            weathers = weathers.Take(3).ToList();
        }

        private static WeatherForecast BuildResultObject(Weather weather, DateTime time)
        {
            var name = weather.Name.ExtractText();
            var iconId = (uint)weather.Icon;

            return new(time, name, iconId);
        }
        private static DateTime GetRootTime(double initialOffset)
        {
            var now = DateTime.UtcNow;
            var rootTime = now.AddMilliseconds(-now.Millisecond).AddSeconds(initialOffset);
            var seconds = (long)(rootTime - DateTime.UnixEpoch).TotalSeconds % WeatherPeriod;

            rootTime = rootTime.AddSeconds(-seconds);

            return rootTime;
        }

        internal static string FormatForecastTime(DateTime forecastTime)
        {
            TimeSpan timeDifference = forecastTime - DateTime.UtcNow;
            double totalMinutes = timeDifference.TotalMinutes;

            switch (totalMinutes)
            {
                case <= 0.01:
                    return "Now";
                case < 1:
                    return $"less than a minute";
                case < 2:
                    return $"a minute";
                case < 60:
                    return $"{(int)totalMinutes} minutes";
            }

            var hours = (int)(totalMinutes / 60);
            var remainingMinutes = (int)(totalMinutes % 60);

            if (remainingMinutes == 0)
                return hours == 1
                    ? "an hour"
                    : $"{(int)hours} hours";

            string hoursStr = hours == 1
                ? "an hour"
                : $"{(int)hours} hours";

            string minutesStr = remainingMinutes == 1
                ? "a minute"
                : $"{(int)remainingMinutes} minutes";

            return $"{hoursStr} and {minutesStr}";
        }
    }

    internal class WeatherForecast(DateTime time, string name, uint iconId)
    {
        public DateTime Time = time;
        public string Name = name;
        public uint IconId = iconId;
    }
}
