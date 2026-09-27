using System.Text.Json;
using System.Text.Json.Serialization;
using Headland.Core.Machines.Components;

namespace Headland.Core.Components;

/// <summary>
/// Every kind of component, by the name an entity's JSON gives it under <c>components</c>. An entity's components run
/// in this order. Each kind's def says what it goes on (see <see cref="ComponentDef.Errors"/>).
/// </summary>
public static class ComponentKinds
{
    private static readonly List<(string name, Type def)> Kinds =
    [
        ("runningGear", typeof(RunningGearDef)),
        ("motor", typeof(MotorDef)),
        ("drivable", typeof(DrivableDef)),
        ("attacherJoints", typeof(AttacherJointsDef)),
        ("frontLoaderBracket", typeof(FrontLoaderBracketDef)),
        ("attachable", typeof(AttachableDef)),
        ("fillUnits", typeof(FillUnitsDef)),
        ("animatedParts", typeof(AnimatedPartsDef)),
        ("workAreas", typeof(WorkAreasDef)),
        ("thresher", typeof(ThresherDef)),
        ("pipe", typeof(PipeDef)),
        ("tipper", typeof(TipperDef)),
        ("lights", typeof(LightsDef)),
        ("craneArm", typeof(CraneArmDef)),
        ("winch", typeof(WinchDef)),
        ("saw", typeof(SawDef)),
    ];

    public static IEnumerable<string> Names => Kinds.Select(k => k.name);

    public static Type? DefType(string name) => Kinds.Find(k => k.name == name).def;

    public static string NameOf(Type defType) =>
        Kinds.Find(k => k.def == defType).name ?? throw new InvalidOperationException($"{defType.Name} is not a registered component");

    /// <summary>Where a kind runs among an entity's components.</summary>
    public static int Order(Type defType) => Kinds.FindIndex(k => k.def == defType);
}

/// <summary>Reads an entity's <c>components</c> object (kind → def) into defs, in <see cref="ComponentKinds"/> order.</summary>
public sealed class ComponentDefsConverter : JsonConverter<List<ComponentDef>>
{
    public override List<ComponentDef> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("components must be an object: kind → settings");
        var defs = new List<ComponentDef>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var name = reader.GetString()!;
            var type = ComponentKinds.DefType(name)
                       ?? throw new JsonException($"unknown component '{name}' (known: {string.Join(", ", ComponentKinds.Names)})");
            reader.Read();
            var def = (ComponentDef?)JsonSerializer.Deserialize(ref reader, type, options)
                      ?? throw new JsonException($"component '{name}' is null");
            if (defs.Any(d => d.GetType() == type)) throw new JsonException($"component '{name}' is given twice");
            defs.Add(def);
        }
        return defs.OrderBy(d => ComponentKinds.Order(d.GetType())).ToList();
    }

    public override void Write(Utf8JsonWriter writer, List<ComponentDef> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var def in value)
        {
            writer.WritePropertyName(def.Kind);
            JsonSerializer.Serialize(writer, def, def.GetType(), options);
        }
        writer.WriteEndObject();
    }
}
