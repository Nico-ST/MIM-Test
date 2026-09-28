// -----------------------------------------------------------------------------
// Author : N.Stillhart
// Date   : 2026-09-28
// Purpose: MIM independent core logic of the actor inheritance workflow:
//          configuration validation and calculation of the minimal set of
//          attribute changes that make a sub-identity carry the same actor
//          attribute values as its main identity.
// -----------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MIM.ActorInheritance.Workflow
{
    /// <summary>
    /// Calculates the attribute changes needed to align a sub-identity with its main identity.
    /// </summary>
    /// <remarks>
    /// Values are expected in normalized form: <c>null</c> for "no value", a scalar
    /// (string, bool, long, DateTime, Guid, byte[]) for single-valued attributes and an
    /// <see cref="IEnumerable"/> of scalars for multi-valued attributes.
    /// </remarks>
    public static class InheritanceCalculator
    {
        private static readonly char[] ListSeparators = { ';', ',', ' ', '\t', '\r', '\n' };

        // MIM system names: letter first, then letters, digits or underscores. Enforcing this
        // also guarantees that configured names cannot inject anything into XPath filters.
        private static readonly Regex SystemNamePattern = new Regex("^[A-Za-z][A-Za-z0-9_]{0,127}$", RegexOptions.CultureInvariant);

        // Attributes that are managed by the MIM Service itself and must never be inherited.
        private static readonly string[] ProtectedAttributes =
        {
            "ObjectID", "ObjectType", "CreatedTime", "Creator", "DeletedTime", "ResourceTime", "MVObjectID"
        };

        /// <summary>
        /// Splits a list of attribute names separated by ';', ',' or whitespace.
        /// </summary>
        public static IList<string> ParseAttributeList(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new List<string>();
            }

            return value
                .Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Select(name => name.Trim())
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        public static bool IsValidSystemName(string name)
        {
            return name != null && SystemNamePattern.IsMatch(name);
        }

        /// <summary>
        /// Validates the activity configuration and returns a list of error messages (empty when valid).
        /// </summary>
        public static IList<string> ValidateConfiguration(string objectType, string mainIdentityAttribute, IEnumerable<string> inheritedAttributes)
        {
            List<string> errors = new List<string>();

            if (!IsValidSystemName(objectType))
            {
                errors.Add(string.Format("'{0}' is not a valid object type name.", objectType));
            }

            if (!IsValidSystemName(mainIdentityAttribute))
            {
                errors.Add(string.Format("'{0}' is not a valid main identity attribute name.", mainIdentityAttribute));
            }

            List<string> attributes = inheritedAttributes == null ? new List<string>() : inheritedAttributes.ToList();
            if (attributes.Count == 0)
            {
                errors.Add("At least one inherited attribute is required.");
            }

            foreach (string attribute in attributes)
            {
                if (!IsValidSystemName(attribute))
                {
                    errors.Add(string.Format("'{0}' is not a valid attribute name.", attribute));
                }
                else if (string.Equals(attribute, mainIdentityAttribute, StringComparison.Ordinal))
                {
                    errors.Add(string.Format("The main identity attribute '{0}' cannot be inherited.", attribute));
                }
                else if (ProtectedAttributes.Contains(attribute, StringComparer.OrdinalIgnoreCase))
                {
                    errors.Add(string.Format("The system attribute '{0}' cannot be inherited.", attribute));
                }
            }

            return errors;
        }

        /// <summary>
        /// Calculates the changes that turn <paramref name="currentValues"/> into <paramref name="desiredValues"/>
        /// for the given attributes. Returns an empty list when both are already equal.
        /// </summary>
        public static IList<AttributeChange> CalculateChanges(
            IEnumerable<string> attributeNames,
            IDictionary<string, object> desiredValues,
            IDictionary<string, object> currentValues)
        {
            if (attributeNames == null)
            {
                throw new ArgumentNullException(nameof(attributeNames));
            }

            List<AttributeChange> changes = new List<AttributeChange>();
            foreach (string attributeName in attributeNames)
            {
                object desired = Lookup(desiredValues, attributeName);
                object current = Lookup(currentValues, attributeName);

                if (IsMultiValue(desired) || IsMultiValue(current))
                {
                    AddMultiValueChanges(changes, attributeName, ToValueList(desired), ToValueList(current));
                }
                else
                {
                    AddSingleValueChange(changes, attributeName, desired, current);
                }
            }

            return changes;
        }

        /// <summary>
        /// Compares two normalized scalar values.
        /// </summary>
        public static bool ValuesEqual(object left, object right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            byte[] leftBytes = left as byte[];
            byte[] rightBytes = right as byte[];
            if (leftBytes != null || rightBytes != null)
            {
                return leftBytes != null && rightBytes != null && leftBytes.SequenceEqual(rightBytes);
            }

            string leftString = left as string;
            string rightString = right as string;
            if (leftString != null || rightString != null)
            {
                return string.Equals(leftString, rightString, StringComparison.Ordinal);
            }

            if (IsInteger(left) && IsInteger(right))
            {
                return Convert.ToInt64(left) == Convert.ToInt64(right);
            }

            return left.Equals(right);
        }

        private static void AddSingleValueChange(List<AttributeChange> changes, string attributeName, object desired, object current)
        {
            if (desired == null)
            {
                if (current != null)
                {
                    changes.Add(new AttributeChange(attributeName, AttributeChangeMode.Remove, current));
                }
            }
            else if (!ValuesEqual(desired, current))
            {
                changes.Add(new AttributeChange(attributeName, AttributeChangeMode.Modify, desired));
            }
        }

        private static void AddMultiValueChanges(List<AttributeChange> changes, string attributeName, List<object> desired, List<object> current)
        {
            List<object> removed = new List<object>();
            foreach (object value in current)
            {
                if (!ContainsValue(desired, value) && !ContainsValue(removed, value))
                {
                    removed.Add(value);
                    changes.Add(new AttributeChange(attributeName, AttributeChangeMode.Remove, value));
                }
            }

            List<object> inserted = new List<object>();
            foreach (object value in desired)
            {
                if (!ContainsValue(current, value) && !ContainsValue(inserted, value))
                {
                    inserted.Add(value);
                    changes.Add(new AttributeChange(attributeName, AttributeChangeMode.Insert, value));
                }
            }
        }

        private static object Lookup(IDictionary<string, object> values, string attributeName)
        {
            object value;
            if (values == null || !values.TryGetValue(attributeName, out value))
            {
                return null;
            }

            return IsEmpty(value) ? null : value;
        }

        private static bool IsMultiValue(object value)
        {
            return value is IEnumerable && !(value is string) && !(value is byte[]);
        }

        private static bool IsEmpty(object value)
        {
            if (value == null)
            {
                return true;
            }

            string text = value as string;
            if (text != null)
            {
                return text.Length == 0;
            }

            return IsMultiValue(value) && ToValueList(value).Count == 0;
        }

        private static List<object> ToValueList(object value)
        {
            List<object> list = new List<object>();
            if (value == null)
            {
                return list;
            }

            if (!IsMultiValue(value))
            {
                list.Add(value);
                return list;
            }

            foreach (object item in (IEnumerable)value)
            {
                if (!IsEmpty(item))
                {
                    list.Add(item);
                }
            }

            return list;
        }

        private static bool ContainsValue(List<object> values, object value)
        {
            return values.Any(candidate => ValuesEqual(candidate, value));
        }

        private static bool IsInteger(object value)
        {
            return value is int || value is long || value is short || value is byte;
        }
    }
}
