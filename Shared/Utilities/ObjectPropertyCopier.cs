namespace ThriveDevCenter.Shared.Converters
{
    using System.Reflection;

    public static class ObjectPropertyCopier
    {
        /// <summary>
        ///   Copies properties from a model to an existing object
        /// </summary>
        /// <param name="target">The object to set new values on</param>
        /// <param name="source">Where to read new values</param>
        /// <typeparam name="T">The object type</typeparam>
        public static void CopyProperties<T>(T target, T source)
        {
            foreach (var property in typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                var newValue = property.GetValue(source);

                if (Equals(newValue, property.GetValue(target)))
                    continue;

                property.SetValue(target, newValue);
            }
        }
    }
}
