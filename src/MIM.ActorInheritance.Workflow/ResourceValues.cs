// -----------------------------------------------------------------------------
// Author : N.Stillhart
// Date   : 2026-09-28
// Purpose: Adapter between MIM resource values and the normalized values used
//          by the InheritanceCalculator. All access to the MIM ResourceType
//          value API is kept in this one class.
// -----------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ResourceManagement.WebServices;
using Microsoft.ResourceManagement.WebServices.WSResourceManagement;

namespace MIM.ActorInheritance.Workflow
{
    internal static class ResourceValues
    {
        /// <summary>
        /// Reads an attribute of a resource returned by a Read/Enumerate activity as a normalized value.
        /// </summary>
        internal static object Get(ResourceType resource, string attributeName)
        {
            if (resource == null)
            {
                return null;
            }

            ResourcePropertyInfo property = resource[attributeName];
            return property == null ? null : Normalize(property.Value);
        }

        internal static Dictionary<string, object> GetAll(ResourceType resource, IEnumerable<string> attributeNames)
        {
            Dictionary<string, object> values = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (string attributeName in attributeNames)
            {
                values[attributeName] = Get(resource, attributeName);
            }

            return values;
        }

        /// <summary>
        /// Reads a single-valued reference attribute; returns <see cref="Guid.Empty"/> when it is not set.
        /// </summary>
        internal static Guid GetReference(ResourceType resource, string attributeName)
        {
            object value = Get(resource, attributeName);
            return value is Guid ? (Guid)value : Guid.Empty;
        }

        /// <summary>
        /// Converts MIM values into plain CLR values: references become <see cref="Guid"/>s and
        /// multi-valued attributes become a <see cref="List{T}"/>. Plain values are serializable,
        /// which matters because the workflow instance may be persisted by the MIM Service.
        /// </summary>
        internal static object Normalize(object value)
        {
            if (value == null)
            {
                return null;
            }

            if (value is UniqueIdentifier)
            {
                return ((UniqueIdentifier)value).GetGuid();
            }

            if (value is string || value is byte[])
            {
                return value;
            }

            IEnumerable items = value as IEnumerable;
            if (items != null)
            {
                List<object> list = new List<object>();
                foreach (object item in items)
                {
                    object normalized = Normalize(item);
                    if (normalized != null)
                    {
                        list.Add(normalized);
                    }
                }

                return list;
            }

            return value;
        }

        /// <summary>
        /// Converts a normalized value back into the type the MIM Service expects in an update request.
        /// </summary>
        internal static object Denormalize(object value)
        {
            return value is Guid ? new UniqueIdentifier((Guid)value) : value;
        }

        internal static UpdateRequestParameter[] ToUpdateParameters(IEnumerable<AttributeChange> changes)
        {
            return changes
                .Select(change => new UpdateRequestParameter(change.AttributeName, ToUpdateMode(change.Mode), Denormalize(change.Value)))
                .ToArray();
        }

        private static UpdateMode ToUpdateMode(AttributeChangeMode mode)
        {
            switch (mode)
            {
                case AttributeChangeMode.Insert:
                    return UpdateMode.Insert;
                case AttributeChangeMode.Modify:
                    return UpdateMode.Modify;
                case AttributeChangeMode.Remove:
                    return UpdateMode.Remove;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown attribute change mode.");
            }
        }
    }
}
