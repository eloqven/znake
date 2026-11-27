using System;

namespace Znake
{
    internal static class Daylight
    {
        private const double Radians = Math.PI / 180;

        public static double WidgetNightAt(DateTimeOffset localTime, double? latitude, double? longitude)
        {
            if (latitude.HasValue && longitude.HasValue)
                return NightAt(localTime, latitude, longitude);
            double hour = localTime.TimeOfDay.TotalHours;
            return Numbers.Unit(1 - Math.Min(hour, 24 - hour) / 6);
        }

        public static double NightAt(DateTimeOffset when, double? latitude, double? longitude)
        {
            if (!latitude.HasValue || !longitude.HasValue) return 0;
            DateTime utc = when.UtcDateTime;
            double hour = utc.TimeOfDay.TotalHours;
            double yearAngle = SolarYearAngle(utc);
            double declination = 0.006918 - 0.399912 * Math.Cos(yearAngle) + 0.070257 * Math.Sin(yearAngle)
                - 0.006758 * Math.Cos(2 * yearAngle) + 0.000907 * Math.Sin(2 * yearAngle)
                - 0.002697 * Math.Cos(3 * yearAngle) + 0.001480 * Math.Sin(3 * yearAngle);
            double minutes = (hour * 60 + SolarCorrection(utc, longitude.Value) + 1440) % 1440;
            double angle = Math.Abs(minutes / 4 - 180) * Radians;
            double sinProduct = Math.Sin(latitude.Value * Radians) * Math.Sin(declination);
            double cosProduct = Math.Cos(latitude.Value * Radians) * Math.Cos(declination);
            double rising = (Math.Cos(90.833 * Radians) - sinProduct) / cosProduct;
            if (rising <= -1) return 0; // Midnight sun.
            if (rising >= 1)
            {
                // Polar night has no sunrise. Use solar altitude to measure its depth.
                double altitude = Math.Asin(Numbers.Signed(sinProduct + cosProduct * Math.Cos(angle))) / Radians;
                return Numbers.Unit((-altitude - 0.833) / 17.167);
            }
            double daylightArc = Math.Acos(rising);
            return Numbers.Unit((angle - daylightArc) / (Math.PI - daylightArc));
        }

        internal static DateTimeOffset NextWidgetFullMoon(DateTimeOffset now, double? latitude, double? longitude)
        {
            DateTimeOffset earliest = now.AddHours(1);
            if (!latitude.HasValue || !longitude.HasValue)
            {
                TimeZoneInfo local = TimeZoneInfo.Local;
                DateTime day = TimeZoneInfo.ConvertTime(earliest, local).Date;
                var peak = new DateTimeOffset(day, local.GetUtcOffset(day));
                if (peak >= earliest) return peak;
                day = day.AddDays(1);
                return new DateTimeOffset(day, local.GetUtcOffset(day));
            }

            DateTime utc = earliest.UtcDateTime;
            double correction = SolarCorrection(utc, longitude.Value);
            DateTime solarDay = utc.AddMinutes(correction).Date;
            DateTime peakUtc = SolarMidnight(solarDay, longitude.Value);
            return new DateTimeOffset(peakUtc >= utc ? peakUtc : SolarMidnight(solarDay.AddDays(1), longitude.Value));
        }

        private static DateTime SolarMidnight(DateTime day, double longitude)
        {
            DateTime guess = day.AddMinutes(-SolarCorrection(day, longitude));
            return day.AddMinutes(-SolarCorrection(guess, longitude));
        }

        // NOAA's fractional-year approximation gives declination and solar time from UTC.
        private static double SolarYearAngle(DateTime utc)
        {
            int days = DateTime.IsLeapYear(utc.Year) ? 366 : 365;
            return 2 * Math.PI / days * (utc.DayOfYear - 1 + (utc.TimeOfDay.TotalHours - 12) / 24);
        }

        private static double SolarCorrection(DateTime utc, double longitude)
        {
            double angle = SolarYearAngle(utc);
            return 4 * longitude + 229.18 * (0.000075 + 0.001868 * Math.Cos(angle) - 0.032077 * Math.Sin(angle)
                - 0.014615 * Math.Cos(2 * angle) - 0.040849 * Math.Sin(2 * angle));
        }
    }
}
