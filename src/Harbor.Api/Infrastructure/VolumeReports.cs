using System.Globalization;
using System.Text;
using Harbor.Api.Contracts;
using Harbor.Domain.Entities;

namespace Harbor.Api.Infrastructure;

public sealed class ReportLimitException(string message) : Exception(message);

public static class VolumeReports
{
    public const int MaxBuckets = 1_000;

    public static VolumeReportResponse Build(IReadOnlyList<Conversation> conversations, ReportInterval interval)
    {
        if (conversations.Count == 0) return new VolumeReportResponse(interval, 0, 0, []);
        var start = BucketStart(conversations.Min(c => c.CreatedAt), interval);
        var last = conversations.Max(c => c.FirstResolvedAt ?? c.CreatedAt);
        var span = last - start;
        var count = interval switch
        {
            ReportInterval.Hour => (int)Math.Floor(span.TotalHours) + 1,
            ReportInterval.Week => (int)Math.Floor(span.TotalDays / 7) + 1,
            _ => (int)Math.Floor(span.TotalDays) + 1,
        };
        if (count > MaxBuckets)
            throw new ReportLimitException($"The requested range needs {count} '{interval}' buckets (maximum {MaxBuckets}). Use a coarser interval or a narrower range.");
        var started = conversations.GroupBy(c => BucketStart(c.CreatedAt, interval)).ToDictionary(g => g.Key, g => g.Count());
        var closed = conversations.Where(c => c.FirstResolvedAt is not null)
            .GroupBy(c => BucketStart(c.FirstResolvedAt!.Value, interval)).ToDictionary(g => g.Key, g => g.Count());
        var points = new List<VolumePointResponse>();
        try
        {
            for (var bucket = start; bucket <= last;)
            {
                var end = Advance(bucket, interval);
                points.Add(new VolumePointResponse(bucket, end, started.GetValueOrDefault(bucket), closed.GetValueOrDefault(bucket)));
                bucket = end;
            }
        }
        catch (ArgumentOutOfRangeException)
        { throw new ReportLimitException("The final bucket exceeds the supported timestamp range."); }
        return new VolumeReportResponse(interval, conversations.Count, conversations.Count(c => c.FirstResolvedAt is not null), points);
    }

    public static byte[] Csv(VolumeReportResponse report)
    {
        var text = new StringBuilder("bucket_start_utc,bucket_end_utc,started,closed\r\n");
        foreach (var point in report.Points)
            text.Append(point.BucketStart.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)).Append(',')
                .Append(point.BucketEnd.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)).Append(',')
                .Append(point.Started.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(point.Closed.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        return new UTF8Encoding(false).GetBytes(text.ToString());
    }

    private static DateTimeOffset BucketStart(DateTimeOffset value, ReportInterval interval)
    {
        var utc = value.ToUniversalTime();
        if (interval == ReportInterval.Hour) return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
        var date = new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);
        return interval == ReportInterval.Week
            ? date.AddDays(-(((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7)) : date;
    }

    private static DateTimeOffset Advance(DateTimeOffset bucket, ReportInterval interval) => interval switch
    {
        ReportInterval.Hour => bucket.AddHours(1), ReportInterval.Week => bucket.AddDays(7), _ => bucket.AddDays(1),
    };
}
