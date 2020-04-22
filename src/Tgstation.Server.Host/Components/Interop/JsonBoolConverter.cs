﻿using Newtonsoft.Json;
using System;

namespace Tgstation.Server.Host.Components.Interop
{
	/// <summary>
	/// <see cref="JsonConverter"/> for decoding bools returned by BYOND
	/// </summary>
	sealed class JsonBoolConverter : JsonConverter
	{
		/// <inheritdoc />
		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) => writer.WriteValue(((bool)value) ? 1 : 0);

		/// <inheritdoc />
		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) => reader.Value.ToString() == "1";

		/// <inheritdoc />
		public override bool CanConvert(Type objectType) => objectType == typeof(bool);
	}
}
