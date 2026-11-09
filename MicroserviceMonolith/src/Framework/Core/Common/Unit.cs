using System;
using System.Threading.Tasks;

namespace MicroserviceMonolith.Core.Common
{
    /// <summary>
    /// Represents a void type, since <see cref="System.Void"/> is not a valid return type in C# generics.
    /// </summary>
    public readonly struct Unit : IEquatable<Unit>, IComparable<Unit>, IComparable
    {
        private static readonly Unit _value = new();

        /// <summary>
        /// Default and only value of the <see cref="Unit"/> struct.
        /// </summary>
        public static ref readonly Unit Value => ref _value;

        /// <summary>
        /// A completed <see cref="Task"/> containing a <see cref="Unit"/>.
        /// </summary>
        public static Task<Unit> Task { get; } = System.Threading.Tasks.Task.FromResult(_value);

        /// <inheritdoc />
        public int CompareTo(Unit other) => 0;

        /// <inheritdoc />
        int IComparable.CompareTo(object? obj) => obj is Unit ? 0 : 1;

        /// <inheritdoc />
        public override int GetHashCode() => 0;

        /// <inheritdoc />
        public bool Equals(Unit other) => true;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is Unit;

        /// <inheritdoc />
        public override string ToString() => "()";

        public static bool operator ==(Unit first, Unit second) => true;
        public static bool operator !=(Unit first, Unit second) => false;
    }
}
