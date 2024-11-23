using System.Globalization;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog.Events;
using Serilog.Formatting;

public sealed class GelfFormatter : ITextFormatter
{
	private const string GelfVersion = "1.0";
	private static readonly string Host = Dns.GetHostName();
	private static readonly string? EnvironmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
	private static readonly DateTime LinuxEpoch = new DateTime(1970, 1, 1);

	private readonly string _facility;

	public GelfFormatter(string facility)
	{
		_facility = facility;
	}

	static double DateTimeToUnixTimestamp(DateTimeOffset dateTime)
	{
		return (dateTime.ToUniversalTime() - LinuxEpoch).TotalSeconds;
	}
	
	public void Format(LogEvent e, TextWriter output)
	{
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(output);

        var renderMessage = e.RenderMessage(CultureInfo.InvariantCulture);
		
		var json = new JObject
		{
			["formatterVersion"] = GelfVersion,
			["host"] = Host,
			["level"] = GetSeverityLevel(e.Level),
			["facility"] = _facility,
			["message"] = renderMessage,
			["_timestamp"] = DateTimeToUnixTimestamp(e.Timestamp),
			["_environment"] = EnvironmentName
		};
		
		//We will persist them "Additional Fields" according to Gelf spec
		foreach (var property in e.Properties)
		{
			AddAdditionalField(json, property.Key, ParsePropertyValue(property.Value));
		}
		if (e.Exception != null)
		{
			AddAdditionalField(json, "ExceptionSource", e.Exception.Source);
			AddAdditionalField(json, "ExceptionMessage", e.Exception.Message);
			AddAdditionalField(json, "StackTrace", e.Exception.StackTrace);
		} 
		
		output.WriteLine(json.ToString(Formatting.None));
	}
	
	private static object? ParsePropertyValue(LogEventPropertyValue value)
	{
		return value switch
		{
			ScalarValue scalar => scalar.Value is string str ? str : scalar.Value, // Avoid wrapping strings in quotes
			SequenceValue sequence => ParseSequence(sequence),
			StructureValue structure => ParseStructure(structure),
			_ => value.ToString() // Fallback to string representation
		};
	}
	private static List<object?> ParseSequence(SequenceValue sequence)
	{
		var parsedSequence = new List<object?>(sequence.Elements.Count);
		foreach (var item in sequence.Elements)
		{
			var parsedItem = ParsePropertyValue(item);
			if (parsedItem != null)
			{
				parsedSequence.Add(parsedItem);
			}
		}
		return parsedSequence;
	}

	private static Dictionary<string, object> ParseStructure(StructureValue structure)
	{
		var parsedStructure = new Dictionary<string, object>(structure.Properties.Count);
		foreach (var property in structure.Properties)
		{
			var parsedItem = ParsePropertyValue(property.Value);
			if (parsedItem != null)
			{
				parsedStructure[property.Name] = parsedItem;
			}
		}
		return parsedStructure;
	}
	
	static int GetSeverityLevel(LogEventLevel level) =>
		level switch
		{
			LogEventLevel.Verbose or LogEventLevel.Debug => 7,
			LogEventLevel.Information => 6,
			LogEventLevel.Warning => 4,
			LogEventLevel.Error => 3,
			LogEventLevel.Fatal => 2,
			_ => throw new ArgumentOutOfRangeException(nameof(level))
		};

	static void AddAdditionalField(IDictionary<string, JToken> jObject, string key, object? value)
	{
		if (string.IsNullOrWhiteSpace(key)) return;

		//According to the GELF spec, libraries should NOT allow to send id as additional field (_id)
		//Server MUST skip the field because it could override the MongoDB _key field
		if (key.Equals("id", StringComparison.OrdinalIgnoreCase)) key = "id_";
		//According to the GELF spec, additional field keys should start with '_' to avoid collision
		if (!key.StartsWith("_", StringComparison.OrdinalIgnoreCase)) key = "_" + key;

		// Avoid adding null values
		if (value != null)
		{
			jObject[key] = JToken.FromObject(value);
		}
	}
}
