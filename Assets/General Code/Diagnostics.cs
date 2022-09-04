using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Diagnostics : IDisposable
{
	public static List<Diagnostics> LoggedStats { get; set; } = new List<Diagnostics>();
	public static void LogAll()
	{
		foreach (var stat in LoggedStats)
		{
			Debug.Log(stat.ToConsoleString(0, $"[{stat.StartTime:HH:mm:ss:fff}] {stat.Key}"));
		}
		LoggedStats.Clear();
	}

	public void LogLater(Exception exception = null)
	{
		StopTimer();
		if (exception != null)
			LogNow(exception);

		LoggedStats.Add(this);
	}
	public void LogNow(Exception exception = null)
	{
		StopTimer();
		if (exception != null)
			Debug.Log($"<color=red>{exception.Message}\r\n{exception.GetType().Name} occurred during this process:<color>\r\n{ToConsoleString()}");
		else
			Debug.Log(ToConsoleString());

	}


	private const string KeyTime = "Time";
	private static readonly int[] TitleSizes = new int[] { 25, 20, 18, 15, 13 };
	protected virtual string SubStatsLabel => "substats";

	public Diagnostics(string key)
	{
		Key = key;
		StartTime = DateTime.Now;
	}
	public Diagnostics StartSubstat(string key)
	{
		var newSubStat = new Diagnostics(key);
		SubStats.Add(newSubStat);
		return newSubStat;
	}

	public string Key { get; set; }
	private DateTime StartTime { get; set; }
	private DateTime? EndTime { get; set; }
	public int TotalMilliseconds => (int)(EndTime ?? DateTime.Now).Subtract(StartTime).TotalMilliseconds;
	public int OnlyThisMilliseconds => SubStats.Any() ? TotalMilliseconds - SubStats.Sum(z => z.TotalMilliseconds) : TotalMilliseconds;
	public bool IsOngoing => !EndTime.HasValue;
	public string Comment { get; private set; }
	public bool IncludeSubStatDetails { get; set; } = true;

	protected List<Diagnostics> SubStats { get; set; } = new List<Diagnostics>();
	protected Dictionary<string, float> Values { get; set; } = new Dictionary<string, float>();
	public Diagnostics StopTimer()
	{
		Dispose();
		return this;
	}
	public void Dispose()
	{
		if (!EndTime.HasValue)
			EndTime = DateTime.Now;
		foreach (var subStat in SubStats.Where(z => z.IsOngoing))
			subStat.Dispose();
	}

	public void AddFinishedSubstat(Diagnostics substat)
	{
		SubStats.Add(substat.StopTimer());
	}

	protected float? GetValue(string key)
	{
		if (key == KeyTime)
			return OnlyThisMilliseconds;
		else if (Values.ContainsKey(key))
			return Values[key];
		else
			return null;
	}

	public void SetValue(string key, float value)
	{
		if (Values.ContainsKey(key))
			Values[key] = value;
		else
			Values.Add(key, value);
	}
	public void AddToValue(string key, float value = 1f)
	{
		if (Values.ContainsKey(key))
			Values[key] += value;
		else
			Values.Add(key, value);
	}
	public void AddComment(string comment)
	{
		if (Comment == null)
			Comment = string.Empty;

		Comment += $">{comment}\r\n";
	}

	protected string ValueToConsoleString(string key)
	{
		return ValueToConsoleString(GetFormatting(key), GetValue(key), SubStats.Select(z => z.GetValue(key)).Where(z => z.HasValue).Select(z => z.Value));
	}

	protected string ValueToConsoleString(Formatting format, float? thisValue, IEnumerable<float> aggregateValues)
	{
		var result = $"<color={format.Color}>{format.Label} : ";
		var agg = aggregateValues.Any() ? GetAggregates(aggregateValues) : new Aggregates();
		var f = format.Format;

		var valueWrapper = "<b>{0}" + format.Suffix + "</b>";

		var needsSum = thisValue.HasValue && aggregateValues.Any();
		if (needsSum)
		{
			var sum = thisValue.Value + agg.Total;
			result += string.Format(valueWrapper, sum.ToString(f)) + " = ";
			valueWrapper = "{0}";
		}
		if (thisValue.HasValue)
		{
			result += string.Format(valueWrapper, thisValue.Value.ToString(f));
			valueWrapper = "{0}";
		}
		if (needsSum)
		{
			result += " + ";
		}
		if (aggregateValues.Any())
		{
			result += $"Total of {string.Format(valueWrapper, agg.Total.ToString(f))} over <b>{agg.Count}</b> {SubStatsLabel}. Min/Avg/Max : {agg.Minimum.ToString(f)}/{agg.Average.ToString(f)}/{agg.Maximum.ToString(f)}";
			//valueWrapper = "{0}"; // useless line, but consistent
		}

		return result + "</color>";
	}

	/// <summary>
	/// Creates a string representing all available data for this stat.
	/// </summary>
	/// <param name="titleFormat">How the stat's title should be formatted. Include {0} to show the key</param>
	public virtual string ToConsoleString()
	{
		return ToConsoleString(0, null);
	}

	private string ToConsoleString(int sizeIndex, string keyOverride = null)
	{
		var result = $"{(sizeIndex == 0 ? "" : "\r\n")}<size={TitleSizes[sizeIndex]}>{(keyOverride ?? Key)}</size>\r\n";
		result += ValueToConsoleString(GetFormatting(KeyTime), OnlyThisMilliseconds, SubStats.Select(z => (float)z.TotalMilliseconds)) + "\r\n";

		foreach (var valueKey in Values.Keys.Concat(SubStats.SelectMany(z => z.Values.Keys)).Distinct())
		{
			// Skip this key if it is only present in one substat.
			if (Values.ContainsKey(valueKey) || SubStats.Count(z => z.Values.ContainsKey(valueKey)) > 1)
				result += ValueToConsoleString(valueKey) + "\r\n";
		}

		if (Comment != null)
		{
			result +=
				"--------------------------------------------------\r\n" +
				Comment +
				"--------------------------------------------------\r\n";
		}

		if (SubStats.Any() && IncludeSubStatDetails)
		{
			result += $"\r\nThe {SubStatsLabel} were done in this order : {string.Join(", ", SubStats.Select(z => z.Key))}\r\n";

			// If some substats have the same key, they are grouped together and we show stats for the groups before showing stats for each substat.
			var groups = SubStats.GroupBy(z => z.Key);

			foreach (var group in groups)
			{
				// If are many substats in this group, show only the aggregates for now.
				if (group.Count() > 1)
				{
					result += $"\r\n<size={TitleSizes[sizeIndex + 1]}>{group.Key}</size>\r\n";
					result += ValueToConsoleString(GetFormatting(KeyTime), null, group.Select(z => (float)z.TotalMilliseconds)) + "\r\n";

					var keys = group.SelectMany(z => z.Values.Keys).Distinct();
					foreach (var valueKey in keys)
					{
						var groupValues = group.Select(z => z.GetValue(valueKey)).Where(z => z.HasValue).Select(z => z.Value);
						result += ValueToConsoleString(GetFormatting(valueKey), null, groupValues) + "\r\n";
					}
				}
				else
				{
					result += group.First().ToConsoleString(sizeIndex + 1, null);
				}
			}
			// After all groups are summarized, give details for each individual substat that was grouped
			foreach (var group in groups.Where(z => z.Count() > 1))
			{
				var subStatTitle = $"All {SubStatsLabel} in {group.Key}";
				result += $"\r\n<size={TitleSizes[sizeIndex + 1]}>{subStatTitle}</size>\r\n";
				for (var i = 0; i < group.Count(); i++)
				{
					result += group.ElementAt(i).ToConsoleString(sizeIndex + 2, $"{group.Key} #{(i + 1)}");
				}
			}
		}

		return result;
	}

	private Aggregates GetAggregates(IEnumerable<float> values)
	{
		if (!values.Any())
			return new Aggregates();
		else
			return new Aggregates
			{
				Count = values.Count(),
				Total = values.Sum(),
				Average = values.Average(),
				Minimum = values.Min(),
				Maximum = values.Max(),
			};
	}
	protected virtual Formatting GetFormatting(string key)
	{
		return key switch
		{
			KeyTime => new Formatting { Label = "Time spent", Suffix = "ms", Format = "F0", Color = "#ffa8a8" },
			_ => new Formatting { Label = key, Suffix = string.Empty, Format = "G", Color = "#ffffff" },
		};
	}

	protected struct Aggregates
	{
		public int Count { get; set; }
		public float Total { get; set; }
		public float Average { get; set; }
		public float Minimum { get; set; }
		public float Maximum { get; set; }
	}
	protected struct Formatting
	{
		public string Label { get; set; }
		public string Suffix { get; set; }
		public string Format { get; set; }
		public string Color { get; set; }
	}
}
