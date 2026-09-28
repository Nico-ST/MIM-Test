// -----------------------------------------------------------------------------
// Author : N.Stillhart
// Date   : 2026-09-28
// Purpose: Serializable value objects that describe the attribute changes the
//          actor inheritance activity applies to a resource. They do not
//          depend on MIM types, so they can be persisted with the workflow
//          instance and unit tested outside of the MIM Service.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;

namespace MIM.ActorInheritance.Workflow
{
    /// <summary>
    /// Kind of change applied to an attribute. Maps 1:1 to the MIM <c>UpdateMode</c>.
    /// </summary>
    public enum AttributeChangeMode
    {
        /// <summary>Adds a value to a multi-valued attribute.</summary>
        Insert,

        /// <summary>Sets the value of a single-valued attribute.</summary>
        Modify,

        /// <summary>Removes a value from a single- or multi-valued attribute.</summary>
        Remove
    }

    /// <summary>
    /// A single attribute change. <see cref="Value"/> holds a normalized value
    /// (references are <see cref="Guid"/>s, see <c>ResourceValues</c>).
    /// </summary>
    [Serializable]
    public sealed class AttributeChange
    {
        public AttributeChange(string attributeName, AttributeChangeMode mode, object value)
        {
            if (string.IsNullOrEmpty(attributeName))
            {
                throw new ArgumentException("An attribute name is required.", nameof(attributeName));
            }

            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            this.AttributeName = attributeName;
            this.Mode = mode;
            this.Value = value;
        }

        public string AttributeName { get; private set; }

        public AttributeChangeMode Mode { get; private set; }

        public object Value { get; private set; }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} {1} '{2}'", this.Mode, this.AttributeName, this.Value);
        }
    }

    /// <summary>
    /// All changes that have to be written to one resource.
    /// </summary>
    [Serializable]
    public sealed class PendingUpdate
    {
        public PendingUpdate(Guid resourceId, IEnumerable<AttributeChange> changes)
        {
            if (resourceId == Guid.Empty)
            {
                throw new ArgumentException("A resource id is required.", nameof(resourceId));
            }

            if (changes == null)
            {
                throw new ArgumentNullException(nameof(changes));
            }

            this.ResourceId = resourceId;
            this.Changes = new List<AttributeChange>(changes);
        }

        public Guid ResourceId { get; private set; }

        public List<AttributeChange> Changes { get; private set; }
    }
}
