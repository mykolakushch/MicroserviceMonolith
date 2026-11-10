using System;
using System.Collections.Generic;
using System.Linq;

namespace MicroserviceMonolith.Core.Architecture
{
    /// <summary>
    /// Describes the boundary of one module: all types in <see cref="RootNamespace"/> (and below) belong to the module,
    /// and only the types in its <see cref="PublicNamespaces"/> may be used by other modules.
    /// </summary>
    public sealed class ModuleBoundary
    {
        /// <param name="name">Module name used in reports.</param>
        /// <param name="rootNamespace">The namespace that contains the whole module, for example <c>Shop.Orders</c>.</param>
        /// <param name="publicNamespaces">
        /// The namespaces other modules may use (the module's public contracts). Default: <c>{rootNamespace}.Contracts</c>.
        /// </param>
        public ModuleBoundary(string name, string rootNamespace, params string[] publicNamespaces)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
            if (string.IsNullOrWhiteSpace(rootNamespace)) throw new ArgumentException("Root namespace is required.", nameof(rootNamespace));

            Name = name;
            RootNamespace = rootNamespace;
            PublicNamespaces = publicNamespaces is { Length: > 0 }
                ? publicNamespaces
                : new[] { rootNamespace + ".Contracts" };
        }

        public string Name { get; }

        public string RootNamespace { get; }

        public IReadOnlyList<string> PublicNamespaces { get; }

        internal bool Owns(string? @namespace) => IsInside(@namespace, RootNamespace);

        internal bool IsPublic(string? @namespace) => PublicNamespaces.Any(p => IsInside(@namespace, p));

        private static bool IsInside(string? @namespace, string root) =>
            @namespace != null &&
            (@namespace.Equals(root, StringComparison.Ordinal) || @namespace.StartsWith(root + ".", StringComparison.Ordinal));
    }

    /// <summary>
    /// A use of a non-public type of one module by another module.
    /// </summary>
    public sealed record BoundaryViolation(
        ModuleBoundary SourceModule,
        Type SourceType,
        ModuleBoundary TargetModule,
        Type TargetType,
        string Usage)
    {
        public override string ToString() =>
            $"Module '{SourceModule.Name}' uses '{TargetType.FullName}' of module '{TargetModule.Name}' " +
            $"({Usage} in {SourceType.FullName}). Other modules may only use types in: {string.Join(", ", TargetModule.PublicNamespaces)}.";
    }

    /// <summary>
    /// Thrown by <see cref="ModuleBoundaryValidator.AssertNoViolations"/>.
    /// </summary>
    public sealed class ModuleBoundaryViolationException : Exception
    {
        public ModuleBoundaryViolationException(IReadOnlyList<BoundaryViolation> violations)
            : base($"{violations.Count} module boundary violation(s):{Environment.NewLine}" +
                   string.Join(Environment.NewLine, violations.Select(v => " - " + v)))
        {
            Violations = violations;
        }

        public IReadOnlyList<BoundaryViolation> Violations { get; }
    }
}
