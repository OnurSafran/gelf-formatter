using System.Globalization;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog.Events;
using Serilog.Formatting;

public sealed class GelfFormatter : ITextFormatter
{
	const string GelfVersion = "1.0";
	readonly string _host = Dns.GetHostName();
	static readonly DateTime LinuxEpoch = new DateTime(1970, 1, 1);
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
		var renderMessage = e.RenderMessage(CultureInfo.InvariantCulture);
		
		var message = new
		{
			formatterVersion = GelfVersion,
			host = _host,
			level = GetSeverityLevel(e.Level),
			facility = _facility,
			message = renderMessage,
			_imestamp = DateTimeToUnixTimestamp(e.Timestamp),
		};

		var json = JObject.FromObject(message);
		
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
		
		AddAdditionalField(json, "_environment", Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"));
		if (json == null) return;
		var jsonString = json.ToString(Formatting.None, null);
		output.WriteLine(jsonString);
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
		var parsedSequence = new List<object?>();
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
		var parsedStructure = new Dictionary<string, object>();
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
	
	/// <summary>
	/// Values from SyslogSeverity enum here: http://marc.info/?l=log4net-dev&m=109519564630799
	/// </summary>
	/// <param name="level"></param>
	/// <returns></returns>
	static int GetSeverityLevel(LogEventLevel level)
	{
		switch (level)
		{
			case LogEventLevel.Verbose:
				return 7;
			case LogEventLevel.Debug:
				return 7;
			case LogEventLevel.Information:
				return 6;
			case LogEventLevel.Warning:
				return 4;
			case LogEventLevel.Error:
				return 3;
			case LogEventLevel.Fatal:
				return 2;
			default:
				throw new ArgumentOutOfRangeException("level");
		}
	}

	static void AddAdditionalField(IDictionary<string, JToken> jObject, string key, object? value)
	{
		if (key == null) return;

		//According to the GELF spec, libraries should NOT allow to send id as additional field (_id)
		//Server MUST skip the field because it could override the MongoDB _key field
		if (key.Equals("id", StringComparison.OrdinalIgnoreCase))
			key = "id_";

		//According to the GELF spec, additional field keys should start with '_' to avoid collision
		if (!key.StartsWith("_", StringComparison.OrdinalIgnoreCase))
			key = "_" + key;

		jObject.Add(key, value.ToString());
	}
}
