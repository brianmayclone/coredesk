using CoreDesk.Abstractions.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoreDesk.Persistence;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(CoreDeskSettings))]
[JsonSerializable(typeof(HomeLayout))]
internal sealed partial class CoreDeskJsonSerializerContext : JsonSerializerContext;
