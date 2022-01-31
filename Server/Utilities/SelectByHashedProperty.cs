namespace ThriveDevCenter.Server.Utilities
{
    using System;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Reflection;
    using System.Security.Cryptography;
    using System.Text;

    /// <summary>
    ///   To avoid timing attacks when querying the database this class provides helpers for searching based on
    ///   a pre-hashed field in the database. This prevents an attacker from trying to discover a valid value character
    ///   by character. Because of the hashing the attacker won't be able to construct specific strings that have
    ///   hash values of their choosing to try to perform the timing attack to discover a valid value character by
    ///   character.
    /// </summary>
    public static class SelectByHashedProperty
    {
        /// <summary>
        ///   Does a search based on the hashed value of a given property. This should be resistant to timing attack
        ///   when the value column is indexed in the database. An attacker should not be able to iteratively get
        ///   closer and closer to a valid value.
        /// </summary>
        /// <remarks>
        ///   <para>
        ///     To be certain (rather than almost 100% sure) that the returned values match the real value
        ///     (and not just the hash, so this is a check against hash collision), you can add an extra Where clause
        ///     after this. Just need to use something like "AsAsyncEnumerable" to make sure that that Where is not
        ///     evaluated in the database. For example:
        ///     <code>
        ///       a.WhereHashed("id", session).ToAsyncEnumerable().FirstOrDefault(s => s.Id == session);
        ///     </code>
        ///   </para>>
        /// </remarks>
        /// <param name="source">The sequence to use this condition on</param>
        /// <param name="propertyName">
        ///   The name of the property on T to use as the primary value (ie. the non-hashed version of it)
        /// </param>
        /// <param name="rawValue">The raw value to match against. This will be hashed automatically</param>
        /// <param name="hideHashInQueryString">
        ///   If true a trick is used to avoid the SQL command having the hash value embedded in it
        /// </param>
        /// <typeparam name="T">The type the LINQ is ran on</typeparam>
        /// <returns>A sequence that only contain hash matches</returns>
        /// <exception cref="InvalidOperationException">If the target property is not marked as allowed</exception>
        public static IQueryable<T> WhereHashed<T>(this IQueryable<T> source, string propertyName, string rawValue,
            bool hideHashInQueryString = true)
        {
            var parameter = Expression.Parameter(typeof(T), "x");
            var sourceSelector = Expression.PropertyOrField(parameter, propertyName);

            var data = sourceSelector.Member.GetCustomAttribute(typeof(HashedLookUpAttribute));

            if (data == null)
                throw new InvalidOperationException("target property is not marked HashedLookUp");

            var hashedSelector = Expression.PropertyOrField(parameter,
                GetTargetPropertyName(propertyName, (HashedLookUpAttribute)data));

            var hash = HashForDatabaseValue(rawValue);

            Expression targetHashExpression;
            if (hideHashInQueryString)
            {
                targetHashExpression = ExpressionHelper.WrappedConstant(hash);
            }
            else
            {
                targetHashExpression = Expression.Constant(hash);
            }

            var equalsHash = Expression.Equal(hashedSelector, targetHashExpression);

            return source.Where(Expression.Lambda<Func<T, bool>>(equalsHash, parameter));
        }

        public static string HashForDatabaseValue(string rawValue)
        {
            return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawValue)));
        }

        /// <summary>
        ///   Returns a double hash of a value in an integer. For use as id number stand-in for types
        /// </summary>
        /// <param name="rawValue">The raw value to hash</param>
        /// <param name="onceHashedValue">
        ///   If the value has been hashed already once (not null and not empty) this is used to avoid one extra
        ///   hash calculation
        /// </param>
        /// <returns>The first bytes that fit in a long of the hash</returns>
        public static long DoubleHashAsIdStandIn(string? rawValue, string? onceHashedValue)
        {
            var hashData = SHA256.HashData(string.IsNullOrEmpty(onceHashedValue) ?
                SHA256.HashData(Encoding.UTF8.GetBytes(rawValue ?? throw new ArgumentNullException())) :
                Convert.FromBase64String(onceHashedValue));

            var tempQuery = hashData.Take(sizeof(long));

            if (BitConverter.IsLittleEndian)
                tempQuery = tempQuery.Reverse();

            return BitConverter.ToInt64(tempQuery.ToArray(), 0);
        }

        public static void ComputeHashedLookUpValues(this IContainsHashedLookUps instance)
        {
            var hashedLookup = typeof(HashedLookUpAttribute);
            var type = instance.GetType();

            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                var attribute = property.GetCustomAttribute(hashedLookup);

                if (attribute == null)
                    continue;

                var target = type.GetProperty(GetTargetPropertyName(property.Name, (HashedLookUpAttribute)attribute));

                if (target == null)
                    throw new InvalidOperationException("the property the hash should be saved in was not found");

                var valueToHash = property.GetValue(instance);

                if (valueToHash == null)
                {
                    // Make sure hash is also null
                    if (target.GetValue(instance) != null)
                        target.SetValue(instance, null);

                    continue;
                }

                // Hash this value and put in the target field
                var valueToSet = HashForDatabaseValue(valueToHash.ToString() ??
                    throw new Exception("ToString returned null for value to hash for lookup"));

                // Skip if the value is already there
                if (Equals(target.GetValue(instance), valueToSet))
                    continue;

                target.SetValue(instance, valueToSet);
            }
        }

        private static string GetTargetPropertyName(string propertyName, HashedLookUpAttribute attribute)
        {
            // TODO: allow custom name for the target field
            _ = attribute;

            return "Hashed" + propertyName;
        }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class HashedLookUpAttribute : Attribute
    {
    }

    public interface IContainsHashedLookUps
    {
    }
}
