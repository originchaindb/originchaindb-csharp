using System.Reflection;
using System.Text.Json;

var assembly = Assembly.LoadFrom(Path.GetFullPath(args.Length == 0 ? "OriginChainDB/bin/Release/net8.0/OriginChainDB.dll" : args[0]));
var surface = new SortedDictionary<string, string>();
var nullability = new NullabilityInfoContext();
string TypeName(Type type) => type.IsGenericType ? type.GetGenericTypeDefinition().FullName + "<" + string.Join(',', type.GetGenericArguments().Select(TypeName)) + ">" : type.FullName ?? type.Name;
string Parameter(ParameterInfo p) => TypeName(p.ParameterType) + (nullability.Create(p).ReadState == NullabilityState.Nullable ? "?" : "") + " " + p.Name + (p.IsOptional ? "=" + (p.DefaultValue?.ToString() ?? "null") : "");
const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
foreach (var type in assembly.GetExportedTypes())
{
    var name = TypeName(type);
    surface[name] = (type.IsSealed ? "sealed " : "") + (type.IsAbstract ? "abstract " : "") + "base=" + (type.BaseType is null ? "" : TypeName(type.BaseType)) + " interfaces=" + string.Join(',', type.GetInterfaces().Select(TypeName).Order());
    foreach (var method in type.GetMethods(flags))
    {
        if (method.IsSpecialName) continue;
        var key = name + "." + method.Name + "(" + string.Join(',', method.GetParameters().Select(p => TypeName(p.ParameterType))) + ")";
        surface[key] = (method.IsStatic ? "static " : "") + (method.IsVirtual ? "virtual " : "") + TypeName(method.ReturnType) + (nullability.Create(method.ReturnParameter).ReadState == NullabilityState.Nullable ? "?" : "") + "(" + string.Join(',', method.GetParameters().Select(Parameter)) + ")";
    }
    foreach (var constructor in type.GetConstructors(flags))
        surface[name + ".new(" + string.Join(',', constructor.GetParameters().Select(p => TypeName(p.ParameterType))) + ")"] = string.Join(',', constructor.GetParameters().Select(Parameter));
    foreach (var property in type.GetProperties(flags))
        surface[name + "." + property.Name] = TypeName(property.PropertyType) + (nullability.Create(property).ReadState == NullabilityState.Nullable ? "?" : "") + " get=" + (property.GetMethod?.IsPublic == true) + " set=" + (property.SetMethod?.IsPublic == true);
    foreach (var field in type.GetFields(flags))
        surface[name + "." + field.Name] = TypeName(field.FieldType) + (field.IsInitOnly ? " readonly" : "") + (field.IsLiteral ? " const" : "");
}
Console.WriteLine(JsonSerializer.Serialize(surface));
