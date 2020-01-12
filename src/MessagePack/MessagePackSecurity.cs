﻿// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using MessagePack.Internal;

namespace MessagePack
{
    /// <summary>
    /// Settings related to security, particularly relevant when deserializing data from untrusted sources.
    /// </summary>
    public class MessagePackSecurity
    {
        /// <summary>
        /// Gets an instance preconfigured with settings that omit all protections. Useful for deserializing fully-trusted and valid msgpack sequences.
        /// </summary>
        public static readonly MessagePackSecurity TrustedData = new MessagePackSecurity();

        /// <summary>
        /// Gets an instance preconfigured with protections applied with reasonable settings for deserializing untrusted msgpack sequences.
        /// </summary>
        public static readonly MessagePackSecurity UntrustedData = new MessagePackSecurity
        {
            HashCollisionResistant = true,
        };

        /// <summary>
        /// The <see cref="MessagePackSecurity"/> instance that is active for all deserialization operations within this AppDomain or process.
        /// </summary>
        /// <value>Defaults to <see cref="TrustedData"/>.</value>
        public static MessagePackSecurity Active = TrustedData;

        private readonly ObjectFallbackEqualityComparer objectFallbackEqualityComparer;

        private MessagePackSecurity()
        {
            this.objectFallbackEqualityComparer = new ObjectFallbackEqualityComparer(this);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MessagePackSecurity"/> class
        /// with properties copied from a provided template.
        /// </summary>
        /// <param name="copyFrom">The template to copy from.</param>
        protected MessagePackSecurity(MessagePackSecurity copyFrom)
        {
            if (copyFrom is null)
            {
                throw new ArgumentNullException(nameof(copyFrom));
            }

            this.HashCollisionResistant = copyFrom.HashCollisionResistant;
        }

        /// <summary>
        /// Gets a value indicating whether data to be deserialized is untrusted and thus should not be allowed to create
        /// dictionaries or other hash-based collections unless the hashed type has a hash collision resistant implementation available.
        /// This can mitigate some denial of service attacks when deserializing untrusted code.
        /// </summary>
        /// <value>
        /// The value is <c>false</c> for <see cref="TrustedData"/> and <c>true</c> for <see cref="UntrustedData"/>.
        /// </value>
        public bool HashCollisionResistant { get; private set; }

        /// <summary>
        /// Gets a copy of these options with the <see cref="HashCollisionResistant"/> property set to a new value.
        /// </summary>
        /// <param name="hashCollisionResistant">The new value for the <see cref="HashCollisionResistant"/> property.</param>
        /// <returns>The new instance; or the original if the value is unchanged.</returns>
        public MessagePackSecurity WithHashCollisionResistant(bool hashCollisionResistant)
        {
            if (this.HashCollisionResistant == hashCollisionResistant)
            {
                return this;
            }

            var clone = this.Clone();
            clone.HashCollisionResistant = hashCollisionResistant;
            return clone;
        }

        /// <summary>
        /// Gets an <see cref="IEqualityComparer{T}"/> that is suitable to use with a hash-based collection.
        /// </summary>
        /// <typeparam name="T">The type of key that will be hashed in the collection.</typeparam>
        /// <returns>The <see cref="IEqualityComparer{T}"/> to use.</returns>
        /// <remarks>
        /// When <see cref="HashCollisionResistant"/> is active, this will be a collision resistant instance which may reject certain key types.
        /// When <see cref="HashCollisionResistant"/> is not active, this will be <see cref="EqualityComparer{T}.Default"/>.
        /// </remarks>
        public IEqualityComparer<T> GetEqualityComparer<T>()
        {
            return this.HashCollisionResistant ? GetHashCollisionResistantEqualityComparer<T>() : EqualityComparer<T>.Default;
        }

        /// <summary>
        /// Gets an <see cref="IEqualityComparer"/> that is suitable to use with a hash-based collection.
        /// </summary>
        /// <returns>The <see cref="IEqualityComparer"/> to use.</returns>
        /// <remarks>
        /// When <see cref="HashCollisionResistant"/> is active, this will be a collision resistant instance which may reject certain key types.
        /// When <see cref="HashCollisionResistant"/> is not active, this will be <see cref="EqualityComparer{T}.Default"/>.
        /// </remarks>
        public IEqualityComparer GetEqualityComparer()
        {
            return this.HashCollisionResistant ? GetHashCollisionResistantEqualityComparer() : EqualityComparer<object>.Default;
        }

        /// <summary>
        /// Returns a hash collision resistant equality comparer.
        /// </summary>
        /// <typeparam name="T">The type of key that will be hashed in the collection.</typeparam>
        /// <returns>A hash collision resistant equality comparer.</returns>
        protected virtual IEqualityComparer<T> GetHashCollisionResistantEqualityComparer<T>()
        {
            // For anything 32-bits and under, our fallback base secure hasher is usually adequate since it makes the hash unpredictable.
            // We should have special implementations for any value that is larger than 32-bits in order to make sure
            // that all the data gets hashed securely rather than trivially and predictably compressed into 32-bits before being hashed.
            // We also have to specially handle some 32-bit types (e.g. float) where multiple in-memory representations should hash to the same value.
            // Any type supported by the PrimitiveObjectFormatter should be added here if supporting it as a key in a collection makes sense.
            return
                // 32-bits or smaller:
                typeof(T) == typeof(bool) ? CollisionResistantHasher<T>.Instance :
                typeof(T) == typeof(char) ? CollisionResistantHasher<T>.Instance :
                typeof(T) == typeof(sbyte) ? CollisionResistantHasher<T>.Instance :
                typeof(T) == typeof(byte) ? CollisionResistantHasher<T>.Instance :
                typeof(T) == typeof(short) ? CollisionResistantHasher<T>.Instance :
                typeof(T) == typeof(ushort) ? CollisionResistantHasher<T>.Instance :
                typeof(T) == typeof(int) ? CollisionResistantHasher<T>.Instance :
                typeof(T) == typeof(uint) ? CollisionResistantHasher<T>.Instance :

                // Larger than 32-bits (or otherwise require special handling):
                typeof(T) == typeof(long) ? (IEqualityComparer<T>)Int64EqualityComparer.Instance :
                typeof(T) == typeof(ulong) ? (IEqualityComparer<T>)UInt64EqualityComparer.Instance :
                typeof(T) == typeof(float) ? (IEqualityComparer<T>)SingleEqualityComparer.Instance :
                typeof(T) == typeof(double) ? (IEqualityComparer<T>)DoubleEqualityComparer.Instance :
                typeof(T) == typeof(string) ? (IEqualityComparer<T>)StringEqualityComparer.Instance :
                typeof(T) == typeof(Guid) ? (IEqualityComparer<T>)GuidEqualityComparer.Instance :
                typeof(T) == typeof(DateTime) ? (IEqualityComparer<T>)DateTimeEqualityComparer.Instance :
                typeof(T) == typeof(DateTimeOffset) ? (IEqualityComparer<T>)DateTimeOffsetEqualityComparer.Instance :
                typeof(T) == typeof(object) ? (IEqualityComparer<T>)this.objectFallbackEqualityComparer :

                // Any type we don't explicitly whitelist here shouldn't be allowed to use as the key in a hash-based collection since it isn't known to be hash resistant.
                // This method can of course be overridden to add more hash collision resistant type support, or the deserializing party can indicate that the data is Trusted
                // so that this method doesn't even get called.
                throw new TypeAccessException($"No hash-resistant equality comparer available for type: {typeof(T)}");
        }

        /// <summary>
        /// Returns a hash collision resistant equality comparer.
        /// </summary>
        /// <returns>A hash collision resistant equality comparer.</returns>
        protected virtual IEqualityComparer GetHashCollisionResistantEqualityComparer() => (IEqualityComparer)this.GetHashCollisionResistantEqualityComparer<object>();

        /// <summary>
        /// Creates a new instance that is a copy of this one.
        /// </summary>
        /// <remarks>
        /// Derived types should override this method to instantiate their own derived type.
        /// </remarks>
        protected virtual MessagePackSecurity Clone() => new MessagePackSecurity(this);

        /// <summary>
        /// A hash collision resistant implementation of <see cref="IEqualityComparer{T}"/>.
        /// </summary>
        /// <typeparam name="T">The type of key that will be hashed.</typeparam>
        private class CollisionResistantHasher<T> : IEqualityComparer<T>, IEqualityComparer
        {
            internal static readonly CollisionResistantHasher<T> Instance = new CollisionResistantHasher<T>();

            public bool Equals(T x, T y) => EqualityComparer<T>.Default.Equals(x, y);

            bool IEqualityComparer.Equals(object x, object y) => ((IEqualityComparer)EqualityComparer<T>.Default).Equals(x, y);

            public int GetHashCode(object obj) => this.GetHashCode((T)obj);

            public virtual int GetHashCode(T value) => HashCode.Combine(value);
        }

        /// <summary>
        /// A special hash-resistent equality comparer that defers picking the actual implementation
        /// till it can check the runtime type of each value to be hashed.
        /// </summary>
        private class ObjectFallbackEqualityComparer : IEqualityComparer<object>, IEqualityComparer
        {
            private static readonly object[] EmptyObjectArray = new object[0];
            private static readonly MethodInfo GetHashCollisionResistantEqualityComparerOpenGenericMethod = typeof(MessagePackSecurity).GetTypeInfo().DeclaredMethods.Single(m => m.Name == nameof(MessagePackSecurity.GetHashCollisionResistantEqualityComparer) && m.IsGenericMethod);
            private readonly MessagePackSecurity security;
            private readonly ThreadsafeTypeKeyHashTable<IEqualityComparer> equalityComparerCache = new ThreadsafeTypeKeyHashTable<IEqualityComparer>();

            internal ObjectFallbackEqualityComparer(MessagePackSecurity security)
            {
                this.security = security ?? throw new ArgumentNullException(nameof(security));
            }

            bool IEqualityComparer<object>.Equals(object x, object y) => EqualityComparer<object>.Default.Equals(x, y);

            bool IEqualityComparer.Equals(object x, object y) => ((IEqualityComparer)EqualityComparer<object>.Default).Equals(x, y);

            public int GetHashCode(object value)
            {
                if (value is null)
                {
                    return 0;
                }

                Type valueType = value.GetType();

                // Take care to avoid recursion.
                if (valueType == typeof(object))
                {
                    // We can trust object.GetHashCode() to be collision resistant.
                    return value.GetHashCode();
                }

                if (!equalityComparerCache.TryGetValue(valueType, out IEqualityComparer equalityComparer))
                {
                    try
                    {
                        equalityComparer = (IEqualityComparer)GetHashCollisionResistantEqualityComparerOpenGenericMethod.MakeGenericMethod(valueType).Invoke(this.security, EmptyObjectArray);
                    }
                    catch (TargetInvocationException ex)
                    {
                        ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                    }

                    equalityComparerCache.TryAdd(valueType, equalityComparer);
                }

                return equalityComparer.GetHashCode(value);
            }
        }

        private class UInt64EqualityComparer : CollisionResistantHasher<ulong>
        {
            internal static new readonly UInt64EqualityComparer Instance = new UInt64EqualityComparer();

            public override int GetHashCode(ulong value) => HashCode.Combine((uint)(value >> 32), unchecked((uint)value));
        }

        private class Int64EqualityComparer : CollisionResistantHasher<long>
        {
            internal static new readonly Int64EqualityComparer Instance = new Int64EqualityComparer();

            public override int GetHashCode(long value) => HashCode.Combine((int)(value >> 32), unchecked((int)value));
        }

        private class SingleEqualityComparer : CollisionResistantHasher<float>
        {
            internal static new readonly SingleEqualityComparer Instance = new SingleEqualityComparer();

            public override unsafe int GetHashCode(float value)
            {
                // Special check for 0.0 so that the hash of 0.0 and -0.0 will equal.
                if (value == 0.0f)
                {
                    return HashCode.Combine(0);
                }

                // Standardize on the binary representation of NaN prior to hashing.
                if (float.IsNaN(value))
                {
                    value = float.NaN;
                }

                long l = *(long*)&value;
                return HashCode.Combine((int)(l >> 32), unchecked((int)l));
            }
        }

        private class DoubleEqualityComparer : CollisionResistantHasher<double>
        {
            internal static new readonly DoubleEqualityComparer Instance = new DoubleEqualityComparer();

            public override unsafe int GetHashCode(double value)
            {
                // Special check for 0.0 so that the hash of 0.0 and -0.0 will equal.
                if (value == 0.0)
                {
                    return HashCode.Combine(0);
                }

                // Standardize on the binary representation of NaN prior to hashing.
                if (double.IsNaN(value))
                {
                    value = double.NaN;
                }

                long l = *(long*)&value;
                return HashCode.Combine((int)(l >> 32), unchecked((int)l));
            }
        }

        private class GuidEqualityComparer : CollisionResistantHasher<Guid>
        {
            internal static new readonly GuidEqualityComparer Instance = new GuidEqualityComparer();

            public override unsafe int GetHashCode(Guid value)
            {
                var hash = default(HashCode);
                int* pGuid = (int*)&value;
                for (int i = 0; i < sizeof(Guid) / sizeof(int); i++)
                {
                    hash.Add(pGuid[i]);
                }

                return hash.ToHashCode();
            }
        }

        private class StringEqualityComparer : CollisionResistantHasher<string>
        {
            internal static new readonly StringEqualityComparer Instance = new StringEqualityComparer();

            public override int GetHashCode(string value)
            {
#if NETCOREAPP
                // .NET Core already has a secure string hashing function. Just use it.
                return value?.GetHashCode() ?? 0;
#else
                var hash = default(HashCode);
                for (int i = 0; i < value.Length; i++)
                {
                    hash.Add(value[i]);
                }

                return hash.ToHashCode();
#endif
            }
        }

        private class DateTimeEqualityComparer : CollisionResistantHasher<DateTime>
        {
            internal static new readonly DateTimeEqualityComparer Instance = new DateTimeEqualityComparer();

            public override unsafe int GetHashCode(DateTime value) => HashCode.Combine((int)(value.Ticks >> 32), unchecked((int)value.Ticks), value.Kind);
        }

        private class DateTimeOffsetEqualityComparer : CollisionResistantHasher<DateTimeOffset>
        {
            internal static new readonly DateTimeOffsetEqualityComparer Instance = new DateTimeOffsetEqualityComparer();

            public override unsafe int GetHashCode(DateTimeOffset value) => HashCode.Combine((int)(value.UtcTicks >> 32), unchecked((int)value.UtcTicks));
        }
    }
}