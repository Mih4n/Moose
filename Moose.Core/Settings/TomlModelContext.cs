using Tomlyn.Model;
using Tomlyn.Serialization;

namespace Moose.Core.Settings;

[TomlSerializable(typeof(TomlTable))]
internal partial class TomlModelContext : TomlSerializerContext;
