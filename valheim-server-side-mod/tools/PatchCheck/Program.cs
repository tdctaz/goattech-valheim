using Mono.Cecil;

// Resolves every [HarmonyPatch] target in the built mod against the game's assemblies.
//
// The mod compiles against publicized game assemblies, so the compiler proves the members exist
// at build time. It does not prove that Harmony can attach: a patch naming an overloaded method
// without an argument list is ambiguous, and a method the game has since renamed or resharded
// still compiles if some other member happens to keep the name. Every reported failure here would
// otherwise have surfaced as a silent no-op or a startup exception on a live server.
//
// Run this after every game update. It is the cheap half of re-verifying the mod.

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: PatchCheck <ServerAuthority.dll> <valheim Managed dir>");
    return 2;
}

string modPath = args[0];
string managedDir = args[1];

var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(managedDir);
var readerParameters = new ReaderParameters { AssemblyResolver = resolver };

var gameAssemblies = new List<AssemblyDefinition>();
foreach (string name in new[] { "assembly_valheim.dll", "assembly_utils.dll" })
{
    string path = Path.Combine(managedDir, name);
    if (File.Exists(path))
    {
        gameAssemblies.Add(AssemblyDefinition.ReadAssembly(path, readerParameters));
    }
}

if (gameAssemblies.Count == 0)
{
    Console.Error.WriteLine($"No game assemblies found in {managedDir}");
    return 2;
}

using AssemblyDefinition mod = AssemblyDefinition.ReadAssembly(modPath, readerParameters);

int checkedCount = 0;
int failures = 0;

foreach (TypeDefinition type in AllTypes(mod.MainModule))
{
    CustomAttribute? attribute = type.CustomAttributes
        .FirstOrDefault(a => a.AttributeType.Name == "HarmonyPatch" && a.ConstructorArguments.Count >= 2);

    if (attribute is null)
    {
        continue;
    }

    if (attribute.ConstructorArguments[0].Value is not TypeReference targetTypeRef ||
        attribute.ConstructorArguments[1].Value is not string methodName)
    {
        continue;
    }

    string[]? argumentTypes = null;
    if (attribute.ConstructorArguments.Count >= 3 &&
        attribute.ConstructorArguments[2].Value is CustomAttributeArgument[] rawArgs)
    {
        argumentTypes = rawArgs
            .Select(a => (a.Value as TypeReference)?.FullName ?? a.Value?.ToString() ?? "?")
            .ToArray();
    }

    checkedCount++;

    TypeDefinition? targetType = gameAssemblies
        .SelectMany(a => AllTypes(a.MainModule))
        .FirstOrDefault(t => t.FullName == targetTypeRef.FullName);

    if (targetType is null)
    {
        Console.WriteLine($"MISSING TYPE   {targetTypeRef.FullName}  (wanted by {type.FullName})");
        failures++;
        continue;
    }

    List<MethodDefinition> candidates = targetType.Methods
        .Where(m => m.Name == methodName)
        .ToList();

    if (candidates.Count == 0)
    {
        Console.WriteLine($"MISSING METHOD {targetType.Name}.{methodName}  (wanted by {type.FullName})");
        failures++;
        continue;
    }

    if (argumentTypes is not null)
    {
        candidates = candidates
            .Where(m => m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(argumentTypes))
            .ToList();

        if (candidates.Count == 0)
        {
            Console.WriteLine(
                $"NO OVERLOAD    {targetType.Name}.{methodName}({string.Join(", ", argumentTypes)})  " +
                $"(wanted by {type.FullName})");
            failures++;
            continue;
        }
    }
    else if (candidates.Count > 1)
    {
        Console.WriteLine(
            $"AMBIGUOUS      {targetType.Name}.{methodName} has {candidates.Count} overloads; " +
            $"the patch must name argument types  (in {type.FullName})");
        failures++;
        continue;
    }

    MethodDefinition target = candidates[0];
    string signature = string.Join(", ", target.Parameters.Select(p => p.ParameterType.Name));
    Console.WriteLine($"ok             {targetType.Name}.{target.Name}({signature})");
}

foreach (AssemblyDefinition assembly in gameAssemblies)
{
    assembly.Dispose();
}

Console.WriteLine();
Console.WriteLine($"{checkedCount} patch target(s) checked, {failures} unresolved.");
return failures == 0 ? 0 : 1;

static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module)
{
    foreach (TypeDefinition type in module.Types)
    {
        yield return type;
        foreach (TypeDefinition nested in Nested(type))
        {
            yield return nested;
        }
    }

    static IEnumerable<TypeDefinition> Nested(TypeDefinition type)
    {
        foreach (TypeDefinition nested in type.NestedTypes)
        {
            yield return nested;
            foreach (TypeDefinition deeper in Nested(nested))
            {
                yield return deeper;
            }
        }
    }
}
