using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using MicroserviceMonolith.Core.Common;

namespace MicroserviceMonolith.Core.Architecture
{
    /// <summary>
    /// Checks that modules only depend on each other's public contracts. Meant to run as an architecture test
    /// (for example in xUnit): <c>ModuleBoundaryValidator.AssertNoViolations(assemblies, boundaries)</c>.
    /// </summary>
    /// <remarks>
    /// A module is a namespace (see <see cref="ModuleBoundary"/>). A type of module A that mentions a type of module B
    /// which is not in B's public namespaces is a violation. "Mentions" covers base types, interfaces, field/property/
    /// event types, method and constructor signatures, local variables, and everything a method body refers to in its IL:
    /// calls, object creation, field access, casts, type checks and generic arguments of those. Lambdas and async methods
    /// are covered because their compiler-generated types live in the same namespace.
    /// What it cannot see: reflection by name, dependency injection resolved by string, and usage hidden behind types that
    /// belong to no module. It checks structure at the assembly level, it is not a security boundary.
    /// </remarks>
    public static class ModuleBoundaryValidator
    {
        private const BindingFlags AllDeclared =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static readonly OpCode[] OneByteOpCodes = new OpCode[256];
        private static readonly OpCode[] TwoByteOpCodes = new OpCode[256];

        static ModuleBoundaryValidator()
        {
            foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(OpCode)) continue;
                var opCode = (OpCode)field.GetValue(null)!;
                var value = (ushort)opCode.Value;
                if (opCode.Size == 1) OneByteOpCodes[value & 0xFF] = opCode;
                else TwoByteOpCodes[value & 0xFF] = opCode;
            }
        }

        /// <summary>
        /// Finds every use of a non-public type of one module by another module.
        /// </summary>
        public static IReadOnlyList<BoundaryViolation> FindViolations(
            IEnumerable<Assembly> assemblies,
            IEnumerable<ModuleBoundary> boundaries)
        {
            if (assemblies == null) throw new ArgumentNullException(nameof(assemblies));
            if (boundaries == null) throw new ArgumentNullException(nameof(boundaries));

            var modules = boundaries.OrderByDescending(b => b.RootNamespace.Length).ToList();
            var violations = new List<BoundaryViolation>();
            var seen = new HashSet<(Type, Type, string)>();

            ModuleBoundary? ModuleOf(Type type) => modules.FirstOrDefault(m => m.Owns(type.Namespace));

            foreach (var sourceType in AssemblyScanning.GetOrderedTypes(assemblies))
            {
                var sourceModule = ModuleOf(sourceType);
                if (sourceModule == null) continue;

                foreach (var (target, usage) in ReferencedTypes(sourceType))
                {
                    var targetModule = ModuleOf(target);
                    if (targetModule == null || ReferenceEquals(targetModule, sourceModule) || targetModule.IsPublic(target.Namespace))
                    {
                        continue;
                    }

                    if (seen.Add((sourceType, target, usage)))
                    {
                        violations.Add(new BoundaryViolation(sourceModule, sourceType, targetModule, target, usage));
                    }
                }
            }

            return violations;
        }

        /// <summary>
        /// Throws <see cref="ModuleBoundaryViolationException"/> listing all violations, if there are any.
        /// </summary>
        public static void AssertNoViolations(IEnumerable<Assembly> assemblies, IEnumerable<ModuleBoundary> boundaries)
        {
            var violations = FindViolations(assemblies, boundaries);
            if (violations.Count > 0)
            {
                throw new ModuleBoundaryViolationException(violations);
            }
        }

        private static IEnumerable<(Type Type, string Usage)> ReferencedTypes(Type type)
        {
            var found = new List<(Type, string)>();

            void Add(Type? t, string usage)
            {
                foreach (var flat in Flatten(t))
                {
                    found.Add((flat, usage));
                }
            }

            try
            {
                Add(type.BaseType, "base type");
                foreach (var @interface in type.GetInterfaces()) Add(@interface, "interface");

                foreach (var field in type.GetFields(AllDeclared)) Add(field.FieldType, $"field {field.Name}");

                foreach (var property in type.GetProperties(AllDeclared))
                {
                    Add(property.PropertyType, $"property {property.Name}");
                }

                foreach (var @event in type.GetEvents(AllDeclared)) Add(@event.EventHandlerType, $"event {@event.Name}");

                foreach (var method in type.GetMethods(AllDeclared)) ScanMethod(type, method, found);
                foreach (var constructor in type.GetConstructors(AllDeclared)) ScanMethod(type, constructor, found);
            }
            catch (Exception ex) when (ex is TypeLoadException or FileNotFoundException or BadImageFormatException or NotSupportedException)
            {
                // A type that cannot be fully inspected is skipped rather than failing the whole check.
            }

            return found;
        }

        private static void ScanMethod(Type declaringType, MethodBase method, List<(Type, string)> found)
        {
            void Add(Type? t, string usage)
            {
                foreach (var flat in Flatten(t))
                {
                    found.Add((flat, usage));
                }
            }

            var name = method.IsConstructor ? "constructor" : method.Name;

            if (method is MethodInfo methodInfo)
            {
                Add(methodInfo.ReturnType, $"return type of {name}");
            }

            foreach (var parameter in method.GetParameters())
            {
                Add(parameter.ParameterType, $"parameter of {name}");
            }

            MethodBody? body;
            try
            {
                body = method.GetMethodBody();
            }
            catch (Exception ex) when (ex is NotSupportedException or BadImageFormatException)
            {
                return;
            }

            if (body == null) return;

            foreach (var local in body.LocalVariables)
            {
                Add(local.LocalType, $"local variable in {name}");
            }

            var il = body.GetILAsByteArray();
            if (il == null) return;

            var typeArguments = declaringType.IsGenericType ? declaringType.GetGenericArguments() : null;
            var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;

            foreach (var token in MetadataTokens(il))
            {
                MemberInfo? member;
                try
                {
                    member = method.Module.ResolveMember(token, typeArguments, methodArguments);
                }
                catch (Exception ex) when (ex is ArgumentException or BadImageFormatException or TypeLoadException or NotSupportedException)
                {
                    continue;
                }

                var usage = $"body of {name}";
                switch (member)
                {
                    case Type t:
                        Add(t, usage);
                        break;
                    case FieldInfo f:
                        Add(f.DeclaringType, usage);
                        Add(f.FieldType, usage);
                        break;
                    case MethodBase m:
                        Add(m.DeclaringType, usage);
                        if (m is MethodInfo mi) Add(mi.ReturnType, usage);
                        foreach (var p in m.GetParameters()) Add(p.ParameterType, usage);
                        break;
                }
            }
        }

        /// <summary>
        /// Walks the IL stream and returns the metadata tokens of type, field and method operands.
        /// </summary>
        private static IEnumerable<int> MetadataTokens(byte[] il)
        {
            var tokens = new List<int>();
            var i = 0;
            try
            {
                while (i < il.Length)
                {
                    OpCode opCode;
                    if (il[i] == 0xFE)
                    {
                        opCode = TwoByteOpCodes[il[i + 1]];
                        i += 2;
                    }
                    else
                    {
                        opCode = OneByteOpCodes[il[i]];
                        i += 1;
                    }

                    switch (opCode.OperandType)
                    {
                        case OperandType.InlineNone:
                            break;
                        case OperandType.ShortInlineBrTarget:
                        case OperandType.ShortInlineI:
                        case OperandType.ShortInlineVar:
                            i += 1;
                            break;
                        case OperandType.InlineVar:
                            i += 2;
                            break;
                        case OperandType.InlineI8:
                        case OperandType.InlineR:
                            i += 8;
                            break;
                        case OperandType.InlineSwitch:
                            i += 4 + 4 * BitConverter.ToInt32(il, i);
                            break;
                        case OperandType.InlineField:
                        case OperandType.InlineMethod:
                        case OperandType.InlineType:
                        case OperandType.InlineTok:
                            tokens.Add(BitConverter.ToInt32(il, i));
                            i += 4;
                            break;
                        default: // InlineI, ShortInlineR, InlineBrTarget, InlineString, InlineSig
                            i += 4;
                            break;
                    }
                }
            }
            catch (IndexOutOfRangeException)
            {
                // Truncated or unexpected stream: keep what was found.
            }
            catch (ArgumentException)
            {
            }

            return tokens;
        }

        private static IEnumerable<Type> Flatten(Type? type)
        {
            if (type == null || type.IsGenericParameter)
            {
                yield break;
            }

            if (type.HasElementType)
            {
                foreach (var inner in Flatten(type.GetElementType()))
                {
                    yield return inner;
                }

                yield break;
            }

            if (type.IsGenericType)
            {
                yield return type.GetGenericTypeDefinition();
                foreach (var argument in type.GetGenericArguments())
                {
                    foreach (var inner in Flatten(argument))
                    {
                        yield return inner;
                    }
                }

                yield break;
            }

            yield return type;
        }
    }
}
